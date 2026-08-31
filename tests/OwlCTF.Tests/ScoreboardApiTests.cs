using OwlCTF.Models;

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
}
