using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class EventTimingTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EventWithoutAScheduleIsUnscheduled() =>
        Assert.Equal(CtfPhase.Unscheduled, State(null, null).Phase);

    [Fact]
    public void EventBeforeItsStartIsUpcoming() =>
        Assert.Equal(CtfPhase.Upcoming, State(Now.AddMinutes(1), Now.AddHours(2)).Phase);

    [Fact]
    public void EventIsLiveAtItsStartTime() =>
        Assert.Equal(CtfPhase.Live, State(Now, Now.AddHours(2)).Phase);

    [Fact]
    public void EventIsLiveAtItsEndTime() =>
        Assert.Equal(CtfPhase.Live, State(Now.AddHours(-2), Now).Phase);

    [Fact]
    public void EventAfterItsEndTimeIsEnded() =>
        Assert.Equal(CtfPhase.Ended, State(Now.AddHours(-2), Now.AddTicks(-1)).Phase);

    [Fact]
    public void EventWithoutAnEndTimeRemainsLive() =>
        Assert.Equal(CtfPhase.Live, State(Now.AddDays(-1), null).Phase);

    [Fact]
    public void DatabaseTimeIsMarkedAsUtcWithoutChangingTheClockTime()
    {
        var databaseValue = new DateTime(2026, 8, 29, 14, 30, 45, DateTimeKind.Unspecified);

        Assert.Equal("2026-08-29T14:30:45.0000000Z", TimeDisplay.UtcIso(databaseValue));
    }

    [Fact]
    public void UtcTimeKeepsItsOriginalClockTime()
    {
        var utcValue = new DateTime(2026, 8, 29, 14, 30, 45, DateTimeKind.Utc);

        Assert.Equal("2026-08-29T14:30:45.0000000Z", TimeDisplay.UtcIso(utcValue));
    }

    private static CtfState State(DateTime? start, DateTime? end)
    {
        var settings = new PlatformSettings("OwlCTF", "About", "Contact", "Sponsors", start, end);
        return CtfState.From(settings, Now);
    }
}
