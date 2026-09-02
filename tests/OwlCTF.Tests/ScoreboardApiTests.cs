using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class ScoreboardApiTests
{
    [Fact]
    public void CtftimeFeedMapsRankTeamScoreAndLastAccept()
    {
        var solvedAt = new DateTime(2026, 8, 29, 19, 6, 0, DateTimeKind.Utc);
        StandingRecord[] standings =
        [
            new(1, Guid.NewGuid(), "Packet Owls", "PK", "open", 1250, 8, solvedAt),
            new(2, Guid.NewGuid(), "Byte Club", null, "college", 900, 5, null)
        ];

        var feed = CtftimeScoreboardResponse.From(standings);

        Assert.Collection(
            feed.Standings,
            first =>
            {
                Assert.Equal(1, first.Pos);
                Assert.Equal("Packet Owls", first.Team);
                Assert.Equal(1250, first.Score);
                Assert.Equal(new DateTimeOffset(solvedAt).ToUnixTimeSeconds(), first.LastAccept);
            },
            second =>
            {
                Assert.Equal(2, second.Pos);
                Assert.Equal("Byte Club", second.Team);
                Assert.Equal(900, second.Score);
                Assert.Null(second.LastAccept);
            });
    }

    [Fact]
    public void CtftimeFeedKeepsTheServiceOrdering()
    {
        StandingRecord[] standings =
        [
            new(3, Guid.NewGuid(), "Third", null, "open", 300, 3, null),
            new(1, Guid.NewGuid(), "First", null, "open", 900, 9, null)
        ];

        var feed = CtftimeScoreboardResponse.From(standings);

        Assert.Equal(["Third", "First"], feed.Standings.Select(row => row.Team));
    }

    [Fact]
    public void ZeroAndNegativeScoresAreExcludedAndRanksStayContiguous()
    {
        StandingRecord[] standings =
        [
            new(1, Guid.NewGuid(), "Scored", null, "open", 250, 2, null),
            new(2, Guid.NewGuid(), "Zero", null, "open", 0, 1, null),
            new(3, Guid.NewGuid(), "Negative", null, "open", -10, 1, null),
            new(4, Guid.NewGuid(), "Also scored", null, "open", 100, 1, null)
        ];

        var eligible = ScoreboardRules.EligibleStandings(standings);
        var feed = CtftimeScoreboardResponse.From(standings);

        Assert.Equal(["Scored", "Also scored"], eligible.Select(row => row.TeamName));
        Assert.Equal([1L, 2L], eligible.Select(row => row.Rank));
        Assert.Equal(["Scored", "Also scored"], feed.Standings.Select(row => row.Team));
        Assert.Equal([1L, 2L], feed.Standings.Select(row => row.Pos));
    }

    [Fact]
    public void GraphExcludesTeamsWithoutAPositiveFinalScore()
    {
        var now = DateTime.UtcNow;
        TeamScoreSeries[] series =
        [
            new(Guid.NewGuid(), "Scored", null, [new(now, 100)]),
            new(Guid.NewGuid(), "No points", null, [new(now, 0)]),
            new(Guid.NewGuid(), "No solves", null, [])
        ];

        var eligible = ScoreboardRules.EligibleSeries(series);

        Assert.Single(eligible);
        Assert.Equal("Scored", eligible[0].TeamName);
    }

    [Fact]
    public void StandingsQueriesExcludeEveryTeamContainingAnAdministrator()
    {
        var root = FindRepositoryRoot();
        var data = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "AppDb.cs"));
        const string exclusion = "WHERE adminMember.TeamId=t.Id AND adminUser.IsAdmin=TRUE";

        Assert.Equal(2, CountOccurrences(data, exclusion));
    }

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(expected, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += expected.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OwlCTF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the OwlCTF repository root.");
    }
}
