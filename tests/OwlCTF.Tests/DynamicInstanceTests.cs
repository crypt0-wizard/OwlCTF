using OwlCTF.Data;
using OwlCTF.Options;
using OwlCTF.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OwlCTF.Tests;

public sealed class FlagOwnershipTests
{
    [Fact]
    public async Task TeamFlagIsAcceptedOnlyForItsChallenge()
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid(); var flag = "CTF{owned}";
        var store = new FakeOwnershipStore();
        var service = CreateService(store, flag, team, challenge, autoBan: false);
        var accepted = await service.CheckAsync(flag, team, Guid.NewGuid(), challenge, TestContext.Current.CancellationToken);
        var wrongChallenge = await service.CheckAsync(flag, team, Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(FlagOwnershipDisposition.OwnedBySubmittingTeam, accepted.Disposition);
        Assert.Equal(FlagOwnershipDisposition.WrongChallenge, wrongChallenge.Disposition);
        Assert.Empty(store.Incidents);
    }

    [Fact]
    public async Task CrossTeamFlagCreatesAnIncidentWithoutBanningByDefault()
    {
        var owner = Guid.NewGuid(); var submitter = Guid.NewGuid(); var challenge = Guid.NewGuid(); var flag = "CTF{stolen}";
        var store = new FakeOwnershipStore(); var notifier = new FakeNotifier();
        var service = CreateService(store, flag, owner, challenge, false, notifier);
        var result = await service.CheckAsync(flag, submitter, Guid.NewGuid(), challenge, TestContext.Current.CancellationToken);
        Assert.Equal(FlagOwnershipDisposition.OwnedByAnotherTeam, result.Disposition);
        Assert.Single(store.Incidents); Assert.True(notifier.Called); Assert.Null(store.BannedTeam);
        Assert.DoesNotContain(flag, store.Incidents[0].Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTeamFlagAutoBansOnlyWhenEnabled()
    {
        var owner = Guid.NewGuid(); var submitter = Guid.NewGuid(); var challenge = Guid.NewGuid(); var flag = "CTF{auto-ban-switch}";
        var store = new FakeOwnershipStore();
        var service = CreateService(store, flag, owner, challenge, true);
        await service.CheckAsync(flag, submitter, Guid.NewGuid(), challenge, TestContext.Current.CancellationToken);
        Assert.Equal(submitter, store.BannedTeam); Assert.True(store.AutoBanMarked);
    }

    private static FlagOwnershipService CreateService(FakeOwnershipStore store, string flag, Guid owner, Guid challenge, bool autoBan, FakeNotifier? notifier = null)
    {
        var hasher = new FlagHasher(Microsoft.Extensions.Options.Options.Create(new SecurityOptions { FlagPepper = new string('p', 40) }));
        store.Owner = new(Guid.NewGuid(), challenge, owner, hasher.Hash(flag));
        return new(store, hasher, notifier ?? new FakeNotifier(), Microsoft.Extensions.Options.Options.Create(new DynamicInstanceOptions { AutoBanOnCheat = autoBan }), TimeProvider.System);
    }

    private sealed class FakeNotifier : ICheatIncidentNotifier { public bool Called { get; private set; } public Task<bool> NotifyAsync(CheatIncident incident, CancellationToken ct) { Called = true; return Task.FromResult(true); } }
    private sealed class FakeOwnershipStore : IFlagOwnershipStore
    {
        public IssuedFlagOwner? Owner { get; set; } public List<CheatIncident> Incidents { get; } = []; public Guid? BannedTeam { get; private set; } public bool AutoBanMarked { get; private set; }
        public Task<IssuedFlagOwner?> FindIssuedFlagAsync(string hash, CancellationToken ct) => Task.FromResult(Owner?.FlagHash == hash ? Owner : null);
        public Task AddIncidentAsync(CheatIncident incident, CancellationToken ct) { Incidents.Add(incident); return Task.CompletedTask; }
        public Task MarkIncidentNotifiedAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task BanTeamAsync(Guid teamId, string reason, CancellationToken ct) { BannedTeam = teamId; return Task.CompletedTask; }
        public Task MarkIncidentAutoBanAsync(Guid incidentId, CancellationToken ct) { AutoBanMarked = true; return Task.CompletedTask; }
    }
}

public sealed class InstanceExpiryProcessorTests
{
    [Fact]
    public async Task ExpiryProcessorRemovesContainersAndMarksInstancesExpired()
    {
        var instance = new ChallengeInstance { Id = Guid.NewGuid(), ContainerId = "container-1", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), Status = ChallengeInstanceStatus.Active };
        var store = new FakeExpiryStore(instance); var runtime = new FakeRuntime();
        var processor = new InstanceExpiryProcessor(store, runtime, TimeProvider.System, NullLogger<InstanceExpiryProcessor>.Instance);
        var count = await processor.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count); Assert.Equal("container-1", runtime.Removed); Assert.Equal(instance.Id, store.Completed); Assert.Null(store.Failure);
    }

    [Fact]
    public async Task ExpiryProcessorRecordsCleanupFailuresForRetry()
    {
        var instance = new ChallengeInstance { Id = Guid.NewGuid(), ContainerId = "container-2", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), Status = ChallengeInstanceStatus.Active };
        var store = new FakeExpiryStore(instance); var runtime = new FakeRuntime { Failure = new InvalidOperationException("daemon unavailable") };
        var processor = new InstanceExpiryProcessor(store, runtime, TimeProvider.System, NullLogger<InstanceExpiryProcessor>.Instance);
        var count = await processor.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count); Assert.Equal(instance.Id, store.Completed); Assert.Contains("daemon unavailable", store.Failure);
    }

    private sealed class FakeExpiryStore(ChallengeInstance instance) : IExpiredInstanceStore
    {
        public Guid? Completed { get; private set; } public string? Failure { get; private set; }
        public Task<IReadOnlyList<ChallengeInstance>> ClaimExpiredAsync(DateTime now, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChallengeInstance>>([instance]);
        public Task CompleteExpiryAsync(Guid id, string? failure, CancellationToken ct) { Completed = id; Failure = failure; return Task.CompletedTask; }
    }
    private sealed class FakeRuntime : IContainerRuntime
    {
        public string? Removed { get; private set; } public Exception? Failure { get; init; }
        public Task<ContainerLaunchResult> StartAsync(ContainerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task StopAndRemoveAsync(string containerId, CancellationToken ct) { Removed = containerId; return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
    }
}
