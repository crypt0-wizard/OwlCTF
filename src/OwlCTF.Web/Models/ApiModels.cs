using System.Text.Json.Serialization;
using OwlCTF.Services;

namespace OwlCTF.Models;

public sealed record CtftimeScoreboardResponse(IReadOnlyList<CtftimeStandingResponse> Standings)
{
    public static CtftimeScoreboardResponse From(IReadOnlyList<StandingRecord> standings) =>
        new(ScoreboardRules.EligibleStandings(standings).Select(row => new CtftimeStandingResponse(
            row.Rank,
            row.TeamName,
            row.Score,
            row.LastSolveAtUtc is { } lastSolve
                ? new DateTimeOffset(DateTime.SpecifyKind(lastSolve, DateTimeKind.Utc)).ToUnixTimeSeconds()
                : null)).ToArray());
}

public sealed record CtftimeStandingResponse(
    long Pos,
    string Team,
    decimal Score,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LastAccept);
