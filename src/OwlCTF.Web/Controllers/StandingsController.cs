using OwlCTF.Services;
using OwlCTF.Models;
using Microsoft.AspNetCore.Mvc;

namespace OwlCTF.Controllers;

public sealed class StandingsController(ScoreboardService scoreboard, PlatformService platform) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        var series = await scoreboard.GetGraphAsync(ct);
        var now = DateTime.UtcNow;
        var earliestSolve = series.SelectMany(s => s.Points).Select(p => (DateTime?)p.AtUtc).Min();
        var start = settings.StartsAtUtc ?? earliestSolve ?? now.AddHours(-1);
        var end = settings.EndsAtUtc is { } scheduledEnd && scheduledEnd < now ? scheduledEnd : now;
        if (start >= end) start = end.AddHours(-1);
        return View(new StandingsViewModel(await scoreboard.GetAsync(ct), series, start, end, CtfState.From(settings, now)));
    }
}
