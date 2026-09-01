using OwlCTF.Data;
using OwlCTF.Models;
using Microsoft.Extensions.Caching.Memory;

namespace OwlCTF.Services;

public sealed class ScoreboardService(AppDb db, IMemoryCache cache)
{
    public async Task<IReadOnlyList<StandingRecord>> GetAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync("standings", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2);
            return ScoreboardRules.EligibleStandings(await db.GetStandingsAsync(ct));
        }))!;
    public async Task<IReadOnlyList<TeamScoreSeries>> GetGraphAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync("standings-graph", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2);
            return ScoreboardRules.EligibleSeries(await db.GetTopTeamScoreSeriesAsync(ct));
        }))!;
    public void Invalidate() { cache.Remove("standings"); cache.Remove("standings-graph"); }
}

public static class ScoreboardRules
{
    public static IReadOnlyList<StandingRecord> EligibleStandings(IEnumerable<StandingRecord> standings) =>
        standings
            .Where(row => row.Score > 0)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();

    public static IReadOnlyList<TeamScoreSeries> EligibleSeries(IEnumerable<TeamScoreSeries> series) =>
        series
            .Where(team => team.Points.Count > 0 && team.Points[^1].Score > 0)
            .ToArray();
}
