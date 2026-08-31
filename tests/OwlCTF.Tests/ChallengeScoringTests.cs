using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class ChallengeScoringTests
{
    private readonly DynamicChallengeScoring scoring = new();

    [Fact]
    public void QuadraticDecayMatchesCtfdAndStopsAtTheMinimum()
    {
        Assert.Equal(500, scoring.Calculate(500, 100, 10, 0));
        Assert.Equal(496, scoring.Calculate(500, 100, 10, 1));
        Assert.Equal(400, scoring.Calculate(500, 100, 10, 5));
        Assert.Equal(100, scoring.Calculate(500, 100, 10, 10));
        Assert.Equal(100, scoring.Calculate(500, 100, 10, 50));
        Assert.Equal(500, scoring.Calculate(500, 100, 0, 50));
    }

    [Fact]
    public void AwardPlanFreezesTheCurrentValueBeforeCalculatingTheNextValue()
    {
        var plan = scoring.Plan(
            currentValue: 400,
            initial: 500,
            minimum: 100,
            decay: 10,
            eligibleSolveCountAfterAward: 6);

        Assert.Equal(400, plan.ValueAwarded);
        Assert.Equal(356, plan.NextCurrentValue);
    }

    [Fact]
    public void IneligibleTeamsDoNotAffectChallengeDecay()
    {
        Assert.True(scoring.CountsForDecay(new(false, false, false, false)));
        Assert.False(scoring.CountsForDecay(new(true, false, false, false)));
        Assert.False(scoring.CountsForDecay(new(false, true, false, false)));
        Assert.False(scoring.CountsForDecay(new(false, false, true, false)));
        Assert.False(scoring.CountsForDecay(new(false, false, false, true)));
    }

    [Fact]
    public async Task ConcurrentSolversEachReceiveOneFrozenValue()
    {
        const int initial = 500;
        const int minimum = 100;
        const int decay = 10;
        var rowLock = new SemaphoreSlim(1, 1);
        var current = initial;
        long eligibleSolves = 0;

        async Task<int> AwardAsync()
        {
            await rowLock.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                var plan = scoring.Plan(current, initial, minimum, decay, ++eligibleSolves);
                current = plan.NextCurrentValue;
                await Task.Yield();
                return plan.ValueAwarded;
            }
            finally
            {
                rowLock.Release();
            }
        }

        var awards = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AwardAsync()));
        var expected = Enumerable.Range(0, 8)
            .Select(index => scoring.Calculate(initial, minimum, decay, index))
            .OrderDescending()
            .ToArray();

        Assert.Equal(expected, awards.OrderDescending().ToArray());
        Assert.Equal(scoring.Calculate(initial, minimum, decay, 8), current);
    }
}
