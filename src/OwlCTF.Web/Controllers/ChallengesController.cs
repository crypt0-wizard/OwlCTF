using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Hubs;
using OwlCTF.Models;
using OwlCTF.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OwlCTF.Options;

namespace OwlCTF.Controllers;

public sealed class ChallengesController(AppDb db, PlatformService platform, FlagHasher flags, FlagOwnershipService ownership, FileStorage storage, ScoreboardService scoreboard, IHubContext<ActivityHub> activity, IInstanceStore instanceStore, IOptions<DynamicInstanceOptions> instanceOptions) : Controller
{
    public async Task<IActionResult> Index(string? sort, string? tag, CancellationToken ct)
    {
        var team = User.Identity?.IsAuthenticated == true ? await db.GetTeamForUserAsync(User.UserId(), ct) : null;
        var settings = await platform.GetAsync(ct);
        var challenges = await db.GetChallengesAsync(team?.Id, User.IsInRole("Admin"), ct);
        var availableTags = challenges.SelectMany(challenge => challenge.TagList).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var selectedTag = availableTags.FirstOrDefault(value => value.Equals(tag?.Trim(), StringComparison.OrdinalIgnoreCase));
        var filtered = selectedTag is null ? challenges : challenges.Where(challenge => challenge.TagList.Contains(selectedTag, StringComparer.Ordinal)).ToArray();
        var selectedSort = sort switch { "points-desc" or "name" or "solves" => sort, _ => "points-asc" };
        var sorted = selectedSort switch
        {
            "points-desc" => filtered.OrderByDescending(challenge => challenge.CurrentValue).ThenBy(challenge => challenge.Title),
            "name" => filtered.OrderBy(challenge => challenge.Title),
            "solves" => filtered.OrderByDescending(challenge => challenge.SolveCount).ThenBy(challenge => challenge.Title),
            _ => filtered.OrderBy(challenge => challenge.CurrentValue).ThenBy(challenge => challenge.Title)
        };
        return View(new ChallengesViewModel(sorted.ToArray(), team, CtfState.From(settings, DateTime.UtcNow), selectedSort, availableTags, selectedTag));
    }

    [Authorize, Route("challenges/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        var state = CtfState.From(settings, DateTime.UtcNow);
        if (state.Phase == CtfPhase.Upcoming && !User.IsInRole("Admin"))
        {
            TempData["Error"] = "The event has not started yet. Challenges will open when the CTF begins.";
            return RedirectToAction(nameof(Index));
        }

        var team = await db.GetTeamForUserAsync(User.UserId(), ct);
        var challenge = await db.GetChallengeAsync(id, team?.Id, User.IsInRole("Admin"), ct);
        if (challenge is null) return NotFound();
        var filesTask = db.GetChallengeFilesAsync(id, ct);
        var solvesTask = db.GetChallengeSolvesAsync(id, ct);
        var instanceConfigTask = instanceStore.GetConfigAsync(id, ct);
        await Task.WhenAll(filesTask, solvesTask, instanceConfigTask);
        var instanceConfig = await instanceConfigTask;
        var instancePanel = instanceConfig?.Enabled == true
            ? new ChallengeInstancePanel(instanceOptions.Value.Enabled, challenge.IsSolved, instanceConfig.MaxRenewals, instanceOptions.Value.RenewalSeconds)
            : null;
        return View(new ChallengeDetailViewModel(challenge, await filesTask, await solvesTask, team, state, FlagPrefixPolicy.Normalize(settings.FlagPrefix), instancePanel));
    }

    [Authorize, HttpGet("challenges/files/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (CtfState.From(settings, DateTime.UtcNow).Phase == CtfPhase.Upcoming && !User.IsInRole("Admin"))
        {
            TempData["Error"] = "The event has not started yet. Challenge files will open when the CTF begins.";
            return RedirectToAction(nameof(Index));
        }

        var file = await db.GetFileAsync(id, ct);
        if (file is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(storage.OpenRead(file.StorageName), "application/octet-stream", file.OriginalName, enableRangeProcessing: true);
    }

    [Authorize, HttpPost("challenges/submit"), EnableRateLimiting("submit")]
    public async Task<IActionResult> Submit(SubmissionInput input, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (CtfState.From(settings, DateTime.UtcNow).Phase != CtfPhase.Live) return BadRequest("Flag submission is only available while the CTF is live.");
        if (!ModelState.IsValid) return BadRequest("Invalid submission.");
        var team = await db.GetTeamForUserAsync(User.UserId(), ct);
        if (team is null) { TempData["Error"] = "Create or join a team before submitting flags."; return RedirectToAction(nameof(Detail), new { id = input.ChallengeId }); }
        if (team.IsSuspended) { TempData["Error"] = "Your team is suspended and cannot submit flags."; return RedirectToAction(nameof(Detail), new { id = input.ChallengeId }); }
        var challenge = await db.GetChallengeSecretAsync(input.ChallengeId, ct);
        if (challenge is null || !challenge.IsVisible) return NotFound();
        var ownershipResult = await ownership.CheckAsync(input.Flag, team.Id, User.UserId(), input.ChallengeId, ct);
        var correct = ownershipResult.Disposition switch
        {
            FlagOwnershipDisposition.OwnedBySubmittingTeam => true,
            FlagOwnershipDisposition.NotInstanceFlag => flags.Verify(input.Flag, challenge.FlagHash),
            _ => false
        };
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var ipAddress = remoteIp is null
            ? null
            : (remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp).ToString();
        var awarded = await db.RecordSubmissionAsync(input.ChallengeId, team.Id, User.UserId(), input.Flag, ipAddress, correct, ct);
        var autoBanned = await ownership.ReportCrossTeamMatchAsync(ownershipResult, team.Id, User.UserId(), input.ChallengeId, ct);
        if (awarded)
        {
            scoreboard.Invalidate();
            var recent = await db.GetRecentSolveAsync(input.ChallengeId, team.Id, ct);
            if (recent is not null) await activity.Clients.All.SendAsync("SolveRecorded", recent, ct);
        }
        if (autoBanned) return Redirect("/error/403");
        TempData[correct ? "Message" : "Error"] = correct ? (awarded ? "Correct flag. Points awarded." : "Your team already solved this challenge.") : "Incorrect flag.";
        return RedirectToAction(nameof(Detail), new { id = input.ChallengeId });
    }
}
