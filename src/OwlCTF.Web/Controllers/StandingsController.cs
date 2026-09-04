using Microsoft.AspNetCore.Mvc;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Controllers;

public sealed class StandingsController(ScoreboardService scoreboard, PlatformService platform) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct, string? search = null, string? bracket = null, string? sort = null, string? direction = null)
    {
        var settings = await platform.GetAsync(ct);
        var series = await scoreboard.GetGraphAsync(ct);
        var now = DateTime.UtcNow;
        var earliestSolve = series.SelectMany(s => s.Points).Select(p => (DateTime?)p.AtUtc).Min();
        var start = settings.StartsAtUtc ?? earliestSolve ?? now.AddHours(-1);
        var end = settings.EndsAtUtc is { } scheduledEnd && scheduledEnd < now ? scheduledEnd : now;
        if (start >= end) start = end.AddHours(-1);
        var standings = await scoreboard.GetAsync(ct);
        var filter = new StandingsFilter(search, bracket, sort, direction);
        ViewData["StandingsFilter"] = filter;
        ViewData["TotalTeams"] = standings.Count;
        return View(new StandingsViewModel(ScoreboardRules.FilterStandings(standings, filter), series, start, end, CtfState.From(settings, now)));
    }
}
