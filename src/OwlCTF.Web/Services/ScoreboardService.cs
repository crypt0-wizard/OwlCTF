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
            return await db.GetStandingsAsync(ct);
        }))!;
    public async Task<IReadOnlyList<TeamScoreSeries>> GetGraphAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync("standings-graph", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2);
            return await db.GetTopTeamScoreSeriesAsync(ct);
        }))!;
    public void Invalidate() { cache.Remove("standings"); cache.Remove("standings-graph"); }
}
