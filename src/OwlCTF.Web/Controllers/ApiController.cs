using Microsoft.AspNetCore.Mvc;
using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Controllers;

[ApiController, Route("api/v1")]
public sealed class ApiController(AppDb db, PlatformService platform, ScoreboardService scoreboard, ChallengeCategoryService challengeCategories) : ControllerBase
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
        var categories = await challengeCategories.GetAllAsync(ct);
        return Ok(rows.Select(c => new { c.Id, c.Title, c.Slug, c.Description, c.Author, c.CategoryKey, category = ChallengeCategoryCatalog.Resolve(c.CategoryKey, categories).Name, c.Initial, c.Minimum, c.Decay, c.CurrentValue, points = c.CurrentValue, c.SolveCount, c.IsSolved }));
    }

    [HttpGet("standings")]
    public async Task<IActionResult> Standings(CancellationToken ct) => Ok(await scoreboard.GetAsync(ct));

    [HttpGet("solves/recent")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RecentSolves([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var settings = await platform.GetAsync(ct);
        return Ok(new
        {
            settings.PlatformName,
            solves = await db.GetPublicSolveFeedAsync(Math.Clamp(limit, 1, 250), ct)
        });
    }

    [HttpGet("teams/restrictions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TeamRestrictions(CancellationToken ct) =>
        Ok(new { teams = await db.GetPublicTeamRestrictionsAsync(ct) });

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
