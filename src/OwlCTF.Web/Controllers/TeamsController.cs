using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Controllers;

[Authorize, Route("team")]
public sealed class TeamsController(AppDb db, ScoreboardService scoreboard, PlatformService platform) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var teamTask = db.GetTeamForUserAsync(User.UserId(), ct);
        var settingsTask = platform.GetAsync(ct);
        await Task.WhenAll(teamTask, settingsTask);
        var team = await teamTask;
        var code = team is null ? null : await db.GetTeamJoinCodeAsync(team.Id, User.UserId(), User.IsInRole("Admin"), ct);
        return View(new TeamViewModel(team, code, CountryCatalog.All, (await settingsTask).MaxTeamMembers));
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(TeamInput input, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!TeamCapacityPolicy.HasRoom(0, settings.MaxTeamMembers))
            ModelState.AddModelError("", "Team creation is unavailable until an administrator fixes the team size limit.");
        if (!TeamNamePolicy.TryNormalize(input.Name, out var normalizedName))
            ModelState.AddModelError(nameof(input.Name), $"Use 2–{TeamNamePolicy.MaxLength} characters with letters, numbers, spaces and . _ - [ ] ( ) # + ' & @ !");
        if (!CountryCatalog.IsValid(input.CountryCode)) ModelState.AddModelError(nameof(input.CountryCode), "Choose a valid country.");
        if (!TeamBracketCatalog.IsValid(input.BracketKey)) ModelState.AddModelError(nameof(input.BracketKey), "Choose a valid bracket.");
        if (!ModelState.IsValid) return View("Index", new TeamViewModel(null, null, CountryCatalog.All, settings.MaxTeamMembers));
        if (await db.GetTeamForUserAsync(User.UserId(), ct) is not null) return BadRequest("You already belong to a team.");
        try
        {
            await db.CreateTeamAsync(User.UserId(), normalizedName, input.CountryCode.ToUpperInvariant(), input.BracketKey, input.Status?.Trim(), ct);
            TempData["Message"] = "Team created.";
            return RedirectToAction(nameof(Index));
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            ModelState.AddModelError(nameof(input.Name), "That team name is already in use.");
            return View("Index", new TeamViewModel(null, null, CountryCatalog.All, settings.MaxTeamMembers));
        }
    }

    [HttpPost("join")]
    public async Task<IActionResult> Join(TeamInput input, CancellationToken ct)
    {
        if (await db.GetTeamForUserAsync(User.UserId(), ct) is not null) return BadRequest("You already belong to a team.");
        try
        {
            if (string.IsNullOrWhiteSpace(input.JoinCode))
            {
                TempData["Error"] = "That join code is invalid or belongs to a suspended or inactive team.";
                return RedirectToAction(nameof(Index));
            }
            var result = await db.JoinTeamAsync(User.UserId(), input.JoinCode, ct);
            if (result == TeamJoinResult.TeamFull)
            {
                TempData["Error"] = "That team is full.";
                return RedirectToAction(nameof(Index));
            }
            if (result == TeamJoinResult.InvalidCode)
            {
                TempData["Error"] = "That join code is invalid or belongs to a suspended or inactive team.";
                return RedirectToAction(nameof(Index));
            }
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            TempData["Error"] = "You already belong to a team.";
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings(TeamSettingsInput input, CancellationToken ct)
    {
        if (!CountryCatalog.IsValid(input.CountryCode)) ModelState.AddModelError(nameof(input.CountryCode), "Choose a valid country.");
        if (!TeamBracketCatalog.IsValid(input.BracketKey)) ModelState.AddModelError(nameof(input.BracketKey), "Choose a valid bracket.");
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the country, bracket and status. A status can use up to 50 characters.";
            return RedirectToAction(nameof(Index));
        }
        var team = await db.GetTeamForUserAsync(User.UserId(), ct);
        if (team is null) return NotFound();
        if (!await db.UpdateTeamSettingsAsync(team.Id, User.UserId(), input.CountryCode.ToUpperInvariant(), input.BracketKey, input.Status?.Trim(), ct)) return Forbid();
        TempData["Message"] = "Team profile updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("leave")]
    public async Task<IActionResult> Leave(CancellationToken ct)
    {
        if (CtfState.From(await platform.GetAsync(ct), DateTime.UtcNow).Phase == CtfPhase.Live)
        {
            TempData["Error"] = "You cannot change teams while the CTF is live.";
            return RedirectToAction(nameof(Index));
        }
        var result = await db.LeaveTeamAsync(User.UserId(), ct);
        switch (result)
        {
            case TeamExitResult.Completed:
                TempData["Message"] = "You left the team. Its solves are still part of the event history.";
                break;
            case TeamExitResult.CaptainMustDisband:
                TempData["Error"] = "The captain cannot leave the team. Disband it instead.";
                break;
            default:
                TempData["Error"] = "You no longer belong to that team.";
                break;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("disband")]
    public async Task<IActionResult> Disband(DisbandTeamInput input, CancellationToken ct)
    {
        if (CtfState.From(await platform.GetAsync(ct), DateTime.UtcNow).Phase == CtfPhase.Live)
        {
            TempData["Error"] = "You cannot disband a team while the CTF is live.";
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter the exact team name to confirm disbanding.";
            return RedirectToAction(nameof(Index));
        }

        var result = await db.DisbandTeamAsync(User.UserId(), input.TeamName, ct);
        switch (result)
        {
            case TeamExitResult.Completed:
                scoreboard.Invalidate();
                TempData["Message"] = "Team disbanded. Members were released and the join code was revoked.";
                break;
            case TeamExitResult.NameMismatch:
                TempData["Error"] = "The team name did not match. The team was not disbanded.";
                break;
            case TeamExitResult.NotCaptain:
                TempData["Error"] = "Only the team captain can disband the team.";
                break;
            default:
                TempData["Error"] = "The team was already disbanded or you no longer belong to it.";
                break;
        }
        return RedirectToAction(nameof(Index));
    }
}
