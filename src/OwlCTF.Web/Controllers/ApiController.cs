using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Services;
using OwlCTF.Models;
using Microsoft.AspNetCore.Mvc;

namespace OwlCTF.Controllers;

[ApiController, Route("api/v1")]
public sealed class ApiController(AppDb db, PlatformService platform, ScoreboardService scoreboard) : ControllerBase
{
    [HttpGet("platform")]
    public async Task<IActionResult> Platform(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        return Ok(new { settings.PlatformName, settings.AboutDescription, settings.StartsAtUtc, settings.EndsAtUtc, phase = OwlCTF.Models.CtfState.From(settings, DateTime.UtcNow).Phase.ToString() });
    }

    [HttpGet("challenges")]
    public async Task<IActionResult> Challenges(CancellationToken ct)
    {
        OwlCTF.Models.TeamRecord? team = null;
        if (User.Identity?.IsAuthenticated == true) team = await db.GetTeamForUserAsync(User.UserId(), ct);
        var rows = await db.GetChallengesAsync(team?.Id, User.IsInRole("Admin"), ct);
        return Ok(rows.Select(c => new { c.Id, c.Title, c.Slug, c.Description, c.Author, c.CategoryKey, category = ChallengeCategoryCatalog.Get(c.CategoryKey).Name, c.Initial, c.Minimum, c.Decay, c.CurrentValue, points = c.CurrentValue, c.SolveCount, c.IsSolved }));
    }

    [HttpGet("standings")]
    public async Task<IActionResult> Standings(CancellationToken ct) => Ok(await scoreboard.GetAsync(ct));

    [HttpGet("scoreboard")]
    [HttpGet("ctftime/standings")]
    [Produces("application/json")]
    [ProducesResponseType<CtftimeScoreboardResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CtftimeScoreboardResponse>> CtftimeStandings(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public,max-age=2";
        return Ok(CtftimeScoreboardResponse.From(await scoreboard.GetAsync(ct)));
    }
}
