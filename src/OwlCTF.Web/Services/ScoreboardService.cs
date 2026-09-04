using Microsoft.Extensions.Caching.Memory;
using OwlCTF.Data;
using OwlCTF.Models;

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
    public static IReadOnlyList<StandingRecord> FilterStandings(IEnumerable<StandingRecord> standings, StandingsFilter filter)
    {
        var rows = standings.Where(row =>
            (filter.Bracket.Length == 0 || row.BracketKey == filter.Bracket) &&
            row.TeamName.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        IOrderedEnumerable<StandingRecord> ordered = filter.Sort switch
        {
            "team" => filter.Descending ? rows.OrderByDescending(r => r.TeamName, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase),
            "bracket" => filter.Descending ? rows.OrderByDescending(r => TeamBracketCatalog.Get(r.BracketKey).Name) : rows.OrderBy(r => TeamBracketCatalog.Get(r.BracketKey).Name),
            "score" => filter.Descending ? rows.OrderByDescending(r => r.Score) : rows.OrderBy(r => r.Score),
            "solves" => filter.Descending ? rows.OrderByDescending(r => r.SolveCount) : rows.OrderBy(r => r.SolveCount),
            "last-solve" => filter.Descending ? rows.OrderBy(r => r.LastSolveAtUtc == null).ThenByDescending(r => r.LastSolveAtUtc) : rows.OrderBy(r => r.LastSolveAtUtc == null).ThenBy(r => r.LastSolveAtUtc),
            _ => filter.Descending ? rows.OrderByDescending(r => r.Rank) : rows.OrderBy(r => r.Rank)
        };
        return ordered.ThenBy(r => r.Rank).ThenBy(r => r.TeamId).ToArray();
    }

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

public sealed record StandingsFilter
{
    public string Search { get; }
    public string Bracket { get; }
    public string Sort { get; }
    public string Direction { get; }
    public bool Descending => Direction == "desc";

    public StandingsFilter(string? search = null, string? bracket = null, string? sort = null, string? direction = null)
    {
        Search = (search ?? "").Trim();
        if (Search.Length > 100) Search = Search[..100];
        var key = bracket?.Trim().ToLowerInvariant();
        Bracket = TeamBracketCatalog.IsValid(key) ? key! : "";
        Sort = sort is "team" or "bracket" or "score" or "solves" or "last-solve" ? sort : "rank";
        Direction = direction == "desc" ? "desc" : "asc";
    }
}
