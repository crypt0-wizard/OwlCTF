using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class ScoreboardApiTests
{
    private static StandingRecord[] FilterRows() =>
    [
        new(1, Guid.NewGuid(), "Owls & Co", null, "open", 500, 2, new DateTime(2026, 9, 1)),
        new(2, Guid.NewGuid(), "College Owls", null, "college", 300, 3, new DateTime(2026, 9, 2)),
        new(3, Guid.NewGuid(), "School Owls", null, "high-school", 300, 1, null)
    ];

    [Theory]
    [InlineData("open", 1)]
    [InlineData("college", 2)]
    [InlineData("high-school", 3)]
    public void BracketAndSearchCombineWithoutChangingOverallRank(string bracket, long rank)
    {
        var rows = FilterRows();
        var result = ScoreboardRules.FilterStandings(rows, new(" OWLS ", bracket));
        Assert.Equal(rank, Assert.Single(result).Rank);
        Assert.Equal(3, rows.Length);
    }

    [Theory]
    [InlineData("rank", "asc", "1,2,3")]
    [InlineData("rank", "desc", "3,2,1")]
    [InlineData("team", "asc", "2,1,3")]
    [InlineData("team", "desc", "3,1,2")]
    [InlineData("bracket", "asc", "2,3,1")]
    [InlineData("bracket", "desc", "1,3,2")]
    [InlineData("score", "asc", "2,3,1")]
    [InlineData("score", "desc", "1,2,3")]
    [InlineData("solves", "asc", "3,1,2")]
    [InlineData("solves", "desc", "2,1,3")]
    [InlineData("last-solve", "asc", "1,2,3")]
    [InlineData("last-solve", "desc", "2,1,3")]
    public void SortingIsStableAndMissingDatesStayLast(string sort, string direction, string expected)
    {
        var result = ScoreboardRules.FilterStandings(FilterRows(), new(sort: sort, direction: direction));
        Assert.Equal(expected, string.Join(",", result.Select(row => row.Rank)));
    }

    [Fact]
    public void FiltersHandleLiteralSearchEmptyResultsAndInvalidOptions()
    {
        Assert.Single(ScoreboardRules.FilterStandings(FilterRows(), new("& Co")));
        Assert.Empty(ScoreboardRules.FilterStandings(FilterRows(), new("%")));
        Assert.Empty(ScoreboardRules.FilterStandings([], new()));
        var filter = new StandingsFilter(bracket: "invalid", sort: "invalid", direction: "invalid");
        Assert.Equal("", filter.Bracket);
        Assert.Equal("rank", filter.Sort);
        Assert.Equal("asc", filter.Direction);
        Assert.Equal(3, ScoreboardRules.FilterStandings(FilterRows(), filter).Count);
        Assert.Equal(100, new StandingsFilter(new string('x', 101)).Search.Length);
        Assert.Equal("college", new StandingsFilter(bracket: " COLLEGE ").Bracket);
    }

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

    [Fact]
    public void PublicSolveFeedRanksEligibleSolvesPerChallenge()
    {
        var root = FindRepositoryRoot();
        var data = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "AppDb.cs"));
        var api = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Controllers", "ApiController.cs"));

        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY s.ChallengeId", data, StringComparison.Ordinal);
        Assert.Contains("t.IsBanned=FALSE AND t.IsHidden=FALSE AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE", data, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"solves/recent\")]", api, StringComparison.Ordinal);
        Assert.Contains("settings.PlatformName", api, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicTeamRestrictionFeedDistinguishesActionsWithoutExposingReasons()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Controllers", "ApiController.cs"));
        var data = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "AppDb.cs"));

        Assert.Contains("[HttpGet(\"teams/restrictions\")]", api, StringComparison.Ordinal);
        Assert.Contains("GetPublicTeamRestrictionsAsync", api, StringComparison.Ordinal);
        Assert.Contains("THEN 'auto-banned'", data, StringComparison.Ordinal);
        Assert.Contains("WHEN t.IsBanned=TRUE THEN 'banned'", data, StringComparison.Ordinal);
        Assert.Contains("ELSE 'suspended'", data, StringComparison.Ordinal);
        Assert.DoesNotContain("SuspensionReason", api, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityReason", api, StringComparison.Ordinal);
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
