using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OwlCTF.Data;
using OwlCTF.Options;
using OwlCTF.Services;

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
        var banned = await service.ReportCrossTeamMatchAsync(result, submitter, Guid.NewGuid(), challenge, 42, TestContext.Current.CancellationToken);
        Assert.False(banned);
        Assert.Equal(42, store.Incidents[0].SubmissionAttemptId);
        Assert.Single(store.Incidents); Assert.True(notifier.Called); Assert.Null(store.BannedTeam);
        Assert.DoesNotContain(flag, store.Incidents[0].Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTeamFlagAutoBansOnlyWhenEnabled()
    {
        var owner = Guid.NewGuid(); var submitter = Guid.NewGuid(); var challenge = Guid.NewGuid(); var flag = "CTF{auto-ban-switch}";
        var store = new FakeOwnershipStore();
        var service = CreateService(store, flag, owner, challenge, true);
        var user = Guid.NewGuid();
        var result = await service.CheckAsync(flag, submitter, user, challenge, TestContext.Current.CancellationToken);
        Assert.Null(store.BannedTeam);
        var banned = await service.ReportCrossTeamMatchAsync(result, submitter, user, challenge, 43, TestContext.Current.CancellationToken);
        Assert.True(banned);
        Assert.Equal(submitter, store.BannedTeam); Assert.True(store.AutoBanMarked);
    }

    [Fact]
    public async Task UnknownFlagDoesNotCreateAnIncident()
    {
        var store = new FakeOwnershipStore();
        var service = CreateService(store, "CTF{issued}", Guid.NewGuid(), Guid.NewGuid(), false);

        var result = await service.CheckAsync("CTF{unknown}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(FlagOwnershipDisposition.NotInstanceFlag, result.Disposition);
        Assert.Empty(store.Incidents);
    }

    [Fact]
    public async Task IncompleteOwnershipEvidenceFailsClosedWithoutSideEffects()
    {
        var store = new FakeOwnershipStore();
        var notifier = new FakeNotifier();
        var service = CreateService(store, "CTF{issued}", Guid.NewGuid(), Guid.NewGuid(), true, notifier);

        var banned = await service.ReportCrossTeamMatchAsync(
            new FlagOwnershipResult(FlagOwnershipDisposition.OwnedByAnotherTeam),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 99, TestContext.Current.CancellationToken);

        Assert.False(banned);
        Assert.Empty(store.Incidents);
        Assert.False(notifier.Called);
        Assert.Null(store.BannedTeam);
    }

    [Fact]
    public async Task FailedWebhookDeliveryLeavesIncidentPendingForAdminReview()
    {
        var owner = Guid.NewGuid(); var submitter = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var store = new FakeOwnershipStore(); var notifier = new FakeNotifier { Result = false };
        var service = CreateService(store, "CTF{reported}", owner, challenge, false, notifier);
        var result = await service.CheckAsync("CTF{reported}", submitter, Guid.NewGuid(), challenge, TestContext.Current.CancellationToken);

        await service.ReportCrossTeamMatchAsync(result, submitter, Guid.NewGuid(), challenge, 100, TestContext.Current.CancellationToken);

        Assert.Single(store.Incidents);
        Assert.True(notifier.Called);
        Assert.False(store.IncidentMarkedNotified);
    }

    private static FlagOwnershipService CreateService(FakeOwnershipStore store, string flag, Guid owner, Guid challenge, bool autoBan, FakeNotifier? notifier = null)
    {
        var hasher = new FlagHasher(Microsoft.Extensions.Options.Options.Create(new SecurityOptions { FlagPepper = new string('p', 40) }));
        store.Owner = new(Guid.NewGuid(), challenge, owner, hasher.Hash(flag));
        return new(store, hasher, notifier ?? new FakeNotifier(), Microsoft.Extensions.Options.Options.Create(new DynamicInstanceOptions { AutoBanOnCheat = autoBan }), TimeProvider.System);
    }

    private sealed class FakeNotifier : ICheatIncidentNotifier { public bool Called { get; private set; } public bool Result { get; init; } = true; public Task<bool> NotifyAsync(CheatIncident incident, CancellationToken ct) { Called = true; return Task.FromResult(Result); } }
    private sealed class FakeOwnershipStore : IFlagOwnershipStore
    {
        public IssuedFlagOwner? Owner { get; set; }
        public List<CheatIncident> Incidents { get; } = []; public Guid? BannedTeam { get; private set; }
        public bool AutoBanMarked { get; private set; }
        public bool IncidentMarkedNotified { get; private set; }
        public Task<IssuedFlagOwner?> FindIssuedFlagAsync(string hash, CancellationToken ct) => Task.FromResult(Owner?.FlagHash == hash ? Owner : null);
        public Task AddIncidentAsync(CheatIncident incident, CancellationToken ct) { Incidents.Add(incident); return Task.CompletedTask; }
        public Task MarkIncidentNotifiedAsync(Guid id, CancellationToken ct) { IncidentMarkedNotified = true; return Task.CompletedTask; }
        public Task BanTeamAsync(Guid teamId, string reason, CancellationToken ct) { BannedTeam = teamId; return Task.CompletedTask; }
        public Task<bool> RevokeTeamBanAsync(Guid teamId, CancellationToken ct) { BannedTeam = null; return Task.FromResult(true); }
        public Task MarkIncidentAutoBanAsync(Guid incidentId, CancellationToken ct) { AutoBanMarked = true; return Task.CompletedTask; }
        public Task<CheatIncident?> GetIncidentAsync(Guid incidentId, CancellationToken ct) => Task.FromResult(Incidents.SingleOrDefault(incident => incident.Id == incidentId));
        public Task MarkIncidentManualBanAsync(Guid incidentId, Guid adminUserId, CancellationToken ct) => Task.CompletedTask;
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

    [Fact]
    public async Task EmptyExpiryBatchDoesNoRuntimeWork()
    {
        var store = new FakeExpiryStore();
        var runtime = new FakeRuntime();
        var processor = new InstanceExpiryProcessor(store, runtime, TimeProvider.System, NullLogger<InstanceExpiryProcessor>.Instance);

        Assert.Equal(0, await processor.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Null(runtime.Removed);
        Assert.Null(store.Completed);
    }

    [Fact]
    public async Task ReservedInstanceWithoutAContainerStillReleasesItsLease()
    {
        var instance = new ChallengeInstance { Id = Guid.NewGuid(), ContainerId = null, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), Status = ChallengeInstanceStatus.Provisioning };
        var store = new FakeExpiryStore(instance); var runtime = new FakeRuntime();
        var processor = new InstanceExpiryProcessor(store, runtime, TimeProvider.System, NullLogger<InstanceExpiryProcessor>.Instance);

        Assert.Equal(1, await processor.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Null(runtime.Removed);
        Assert.Equal(instance.Id, store.Completed);
        Assert.Null(store.Failure);
    }

    private sealed class FakeExpiryStore(params ChallengeInstance[] instances) : IExpiredInstanceStore
    {
        public Guid? Completed { get; private set; }
        public string? Failure { get; private set; }
        public Task<IReadOnlyList<ChallengeInstance>> ClaimExpiredAsync(DateTime now, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChallengeInstance>>(instances);
        public Task CompleteExpiryAsync(Guid id, string? failure, CancellationToken ct) { Completed = id; Failure = failure; return Task.CompletedTask; }
    }
    private sealed class FakeRuntime : IContainerRuntime
    {
        public string? Removed { get; private set; }
        public Exception? Failure { get; init; }
        public Task<ContainerLaunchResult> StartAsync(ContainerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task StopAndRemoveAsync(string containerId, CancellationToken ct) { Removed = containerId; return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
    }
}

public sealed class InstanceLifecycleTests
{
    private readonly Guid teamId = Guid.NewGuid();
    private readonly Guid challengeId = Guid.NewGuid();

    [Fact]
    public async Task StatusLookupUsesTheCallingTeamAndChallenge()
    {
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId, Current = ActiveInstance() };
        var service = CreateService(store, new LifecycleRuntime());

        var view = await service.GetAsync(teamId, challengeId, TestContext.Current.CancellationToken);

        Assert.Equal("Active", view.Status);
        Assert.Equal(teamId, store.ObservedTeamId);
        Assert.Equal(challengeId, store.ObservedChallengeId);
    }

    [Fact]
    public async Task AnotherTeamCannotStopAnOwnedInstance()
    {
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId, Claimed = ActiveInstance() };
        var runtime = new LifecycleRuntime();
        var service = CreateService(store, runtime);

        var error = await Assert.ThrowsAsync<InstanceOperationException>(() =>
            service.StopAsync(Guid.NewGuid(), challengeId, TestContext.Current.CancellationToken));

        Assert.Equal(404, error.StatusCode);
        Assert.Null(runtime.RemovedContainerId);
    }

    [Fact]
    public async Task StopRemovesOnlyTheClaimedContainerAndFinalizesItsLease()
    {
        var instance = ActiveInstance();
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId, Claimed = instance };
        var runtime = new LifecycleRuntime();
        var service = CreateService(store, runtime);

        var view = await service.StopAsync(teamId, challengeId, TestContext.Current.CancellationToken);

        Assert.Equal("Stopped", view.Status);
        Assert.Equal(instance.ContainerId, runtime.RemovedContainerId);
        Assert.Equal(instance.Id, store.FinalizedId);
        Assert.Equal(ChallengeInstanceStatus.Stopped, store.FinalizedStatus);
    }

    [Fact]
    public async Task DockerCleanupFailureStillReleasesTheLeaseAndFailsClosed()
    {
        var instance = ActiveInstance();
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId, Claimed = instance };
        var service = CreateService(store, new LifecycleRuntime { StopError = new InvalidOperationException("daemon unavailable") });

        var error = await Assert.ThrowsAsync<InstanceOperationException>(() =>
            service.StopAsync(teamId, challengeId, TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(instance.Id, store.FinalizedId);
        Assert.Equal(ChallengeInstanceStatus.Failed, store.FinalizedStatus);
        Assert.Contains("daemon unavailable", store.FinalizedFailure);
    }

    [Fact]
    public async Task SolvedTeamsCannotRenewBeforeTheLeaseIsTouched()
    {
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId, HasSolved = true, Renewed = ActiveInstance() };
        var service = CreateService(store, new LifecycleRuntime());

        var error = await Assert.ThrowsAsync<InstanceOperationException>(() =>
            service.RenewAsync(teamId, challengeId, TestContext.Current.CancellationToken));

        Assert.Equal(409, error.StatusCode);
        Assert.False(store.RenewCalled);
    }

    [Fact]
    public async Task AnotherTeamCannotRenewAnOwnedInstance()
    {
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId, Renewed = ActiveInstance() };
        var service = CreateService(store, new LifecycleRuntime());

        var error = await Assert.ThrowsAsync<InstanceOperationException>(() =>
            service.RenewAsync(Guid.NewGuid(), challengeId, TestContext.Current.CancellationToken));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task DisabledInstanceServiceRejectsStatusRequestsBeforeDatabaseAccess()
    {
        var store = new LifecycleStore { ExpectedTeamId = teamId, ExpectedChallengeId = challengeId };
        var service = CreateService(store, new LifecycleRuntime(), enabled: false);

        var error = await Assert.ThrowsAsync<InstanceOperationException>(() =>
            service.GetAsync(teamId, challengeId, TestContext.Current.CancellationToken));

        Assert.Equal(503, error.StatusCode);
        Assert.False(store.ConfigRequested);
    }

    private ChallengeInstance ActiveInstance() => new()
    {
        Id = Guid.NewGuid(),
        TeamId = teamId,
        ChallengeId = challengeId,
        ContainerId = "owned-container",
        HostPort = 32001,
        Status = ChallengeInstanceStatus.Active,
        ActiveLeaseKey = $"{teamId:N}:{challengeId:N}",
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(20)
    };

    private static InstanceLifecycleService CreateService(LifecycleStore store, LifecycleRuntime runtime, bool enabled = true)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationManager();
        configuration["ConnectionStrings:MariaDb"] = "Server=unused;";
        var protection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        var appDb = new AppDb(configuration, new JoinCodeProtector(protection), new FirstBloodWebhookProtector(protection), new DynamicChallengeScoring());
        var platform = new PlatformService(appDb, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var hasher = new FlagHasher(Microsoft.Extensions.Options.Options.Create(new SecurityOptions { FlagPepper = new string('p', 40) }));
        return new(store, runtime, hasher, platform, Microsoft.Extensions.Options.Options.Create(new DynamicInstanceOptions
        {
            Enabled = enabled,
            PublicHost = "instances.example",
            GlobalConcurrencyLimit = 10,
            RenewalSeconds = 300,
            MaximumLifetimeSeconds = 3600
        }), TimeProvider.System);
    }

    private sealed class LifecycleStore : IInstanceStore
    {
        public Guid ExpectedTeamId { get; init; }
        public Guid ExpectedChallengeId { get; init; }
        public ChallengeInstance? Current { get; init; }
        public ChallengeInstance? Claimed { get; init; }
        public ChallengeInstance? Renewed { get; init; }
        public bool HasSolved { get; init; }
        public bool RenewCalled { get; private set; }
        public bool ConfigRequested { get; private set; }
        public Guid ObservedTeamId { get; private set; }
        public Guid ObservedChallengeId { get; private set; }
        public Guid? FinalizedId { get; private set; }
        public ChallengeInstanceStatus? FinalizedStatus { get; private set; }
        public string? FinalizedFailure { get; private set; }

        private bool Owns(Guid team, Guid challenge) => team == ExpectedTeamId && challenge == ExpectedChallengeId;
        private void Observe(Guid team, Guid challenge) { ObservedTeamId = team; ObservedChallengeId = challenge; }
        public Task<ChallengeInstanceConfig?> GetConfigAsync(Guid challenge, CancellationToken ct) { ConfigRequested = true; return Task.FromResult<ChallengeInstanceConfig?>(challenge == ExpectedChallengeId ? new() { ChallengeId = challenge, Enabled = true } : null); }
        public Task<ChallengeInstance?> GetCurrentAsync(Guid team, Guid challenge, CancellationToken ct) { Observe(team, challenge); return Task.FromResult(Owns(team, challenge) ? Current : null); }
        public Task<bool> HasSolvedAsync(Guid team, Guid challenge, CancellationToken ct) { Observe(team, challenge); return Task.FromResult(Owns(team, challenge) && HasSolved); }
        public Task<InstanceReservation> ReserveAsync(Guid team, Guid challenge, string flagHash, DateTime now, int globalLimit, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkStartedAsync(Guid id, string containerId, int hostPort, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string reason, CancellationToken ct) => throw new NotSupportedException();
        public Task<ChallengeInstance?> ClaimForStopAsync(Guid team, Guid challenge, CancellationToken ct) { Observe(team, challenge); return Task.FromResult(Owns(team, challenge) ? Claimed : null); }
        public Task MarkStoppedAsync(Guid id, ChallengeInstanceStatus status, string? failure, CancellationToken ct) { FinalizedId = id; FinalizedStatus = status; FinalizedFailure = failure; return Task.CompletedTask; }
        public Task<ChallengeInstance?> RenewAsync(Guid team, Guid challenge, DateTime now, int renewalSeconds, int maximumLifetimeSeconds, CancellationToken ct) { RenewCalled = true; Observe(team, challenge); return Task.FromResult(Owns(team, challenge) ? Renewed : null); }
    }

    private sealed class LifecycleRuntime : IContainerRuntime
    {
        public string? RemovedContainerId { get; private set; }
        public Exception? StopError { get; init; }
        public Task<ContainerLaunchResult> StartAsync(ContainerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task StopAndRemoveAsync(string containerId, CancellationToken ct)
        {
            RemovedContainerId = containerId;
            return StopError is null ? Task.CompletedTask : Task.FromException(StopError);
        }
    }
}

public sealed class InstancePanelTests
{
    [Fact]
    public void ChallengePageWiresEveryInstanceAction()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Challenges", "Detail.cshtml"));

        Assert.Contains("id=\"startInstance\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"renewInstance\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"stopInstance\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"copyInstanceConnection\"", view, StringComparison.Ordinal);
        Assert.Contains("RequestVerificationToken: antiForgeryToken", view, StringComparison.Ordinal);
        Assert.Contains("'/api/instances/' + config.challengeId", view, StringComparison.Ordinal);
        Assert.Contains("cache: 'no-store'", view, StringComparison.Ordinal);
        Assert.Contains("config.challengeSolved", view, StringComparison.Ordinal);
    }

    [Fact]
    public void InstanceApiKeepsAntiforgeryProtection()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Controllers", "InstancesController.cs"));

        Assert.DoesNotContain("IgnoreAntiforgeryToken", controller, StringComparison.Ordinal);
        Assert.Contains("requireLiveEvent: true", controller, StringComparison.Ordinal);
        Assert.Contains("allowSuspendedTeam: true", controller, StringComparison.Ordinal);
        Assert.Contains("challenge_already_solved", controller, StringComparison.Ordinal);
        Assert.Contains("ResponseCache(NoStore = true", controller, StringComparison.Ordinal);
        Assert.Contains("GetTeamForUserAsync(User.UserId()", controller, StringComparison.Ordinal);
        Assert.Contains("action(team.Id, challengeId", controller, StringComparison.Ordinal);

        var store = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "EfInstanceStore.cs"));
        Assert.Contains("x.TeamId == teamId && x.ChallengeId == challengeId", store, StringComparison.Ordinal);
        Assert.Contains("WHERE TeamId={teamId} AND ChallengeId={challengeId}", store, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedTeamsAlwaysReachTheirDedicatedPage()
    {
        var root = FindRepositoryRoot();
        var middleware = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Services", "TeamAccessGuardMiddleware.cs"));

        Assert.Contains("StartsWithSegments(\"/team/blocked\")", middleware, StringComparison.Ordinal);
        Assert.Contains("Redirect(\"/team/blocked\")", middleware, StringComparison.Ordinal);
        Assert.Contains("x.IsSuspended || x.IsBanned", middleware, StringComparison.Ordinal);
        Assert.Contains("team_suspended", middleware, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Home", "TeamBlocked.cshtml"));
        Assert.Contains("Your team got benched", page, StringComparison.Ordinal);
        Assert.Contains("Your team is taking a timeout", page, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Logout\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmissionLogsExposeCrossTeamOwnershipAndAdminAction()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Admin", "SubmissionLogs.cshtml"));
        var migration = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "Migrations", "202609020001_LinkCheatIncidentsToSubmissions.cs"));
        var data = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "AppDb.cs"));

        Assert.Contains("Cross-team flag", page, StringComparison.Ordinal);
        Assert.Contains("FlagOwnerTeamName", page, StringComparison.Ordinal);
        Assert.Contains("FlagOwnerChallengeTitle", page, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"BanTeamFromIncident\"", page, StringComparison.Ordinal);
        Assert.Contains("SubmissionAttemptId", migration, StringComparison.Ordinal);
        Assert.Contains("CAST(i.Id AS CHAR) CheatIncidentId", data, StringComparison.Ordinal);
        Assert.Contains("CAST(owner.Id AS CHAR) FlagOwnerTeamId", data, StringComparison.Ordinal);
        Assert.Contains("AutoBanAppliedValue", data, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamManagementCanRevokeCurrentBansWithoutDeletingIncidentHistory()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Admin", "Teams.cshtml"));
        var controller = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Controllers", "AdminController.cs"));
        var store = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "EfInstanceStore.cs"));
        var data = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "AppDb.cs"));

        Assert.Contains("asp-action=\"RevokeTeamBan\"", page, StringComparison.Ordinal);
        Assert.Contains("RevokeTeamBanAsync", controller, StringComparison.Ordinal);
        Assert.Contains("x.IsBanned && !x.IsDisbanded", store, StringComparison.Ordinal);
        Assert.DoesNotContain("db.CheatIncidents.Remove", store, StringComparison.Ordinal);
        Assert.Contains("t.IsBanned=TRUE", data, StringComparison.Ordinal);
        Assert.Contains("t.SecurityReason LIKE 'Automatic action", data, StringComparison.Ordinal);
        Assert.Contains("SELECT CAST(Id AS CHAR) FROM Challenges", data, StringComparison.Ordinal);
        Assert.Contains("CAST(t.Id AS CHAR) TeamIdValue", data, StringComparison.Ordinal);
        Assert.DoesNotContain("QuerySingleOrDefaultAsync<Guid?>", data, StringComparison.Ordinal);
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
