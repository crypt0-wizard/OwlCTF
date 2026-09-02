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
    }

    [Fact]
    public void BlockedTeamsAlwaysReachTheirDedicatedPage()
    {
        var root = FindRepositoryRoot();
        var middleware = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Services", "TeamAccessGuardMiddleware.cs"));

        Assert.Contains("StartsWithSegments(\"/team/blocked\")", middleware, StringComparison.Ordinal);
        Assert.Contains("Redirect(\"/team/blocked\")", middleware, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Home", "TeamBlocked.cshtml"));
        Assert.Contains("Your team got benched", page, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Logout\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmissionLogsExposeCrossTeamOwnershipAndAdminAction()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Admin", "SubmissionLogs.cshtml"));
        var migration = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Data", "Migrations", "202609020001_LinkCheatIncidentsToSubmissions.cs"));

        Assert.Contains("Cross-team flag", page, StringComparison.Ordinal);
        Assert.Contains("FlagOwnerTeamName", page, StringComparison.Ordinal);
        Assert.Contains("FlagOwnerChallengeTitle", page, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"BanTeamFromIncident\"", page, StringComparison.Ordinal);
        Assert.Contains("SubmissionAttemptId", migration, StringComparison.Ordinal);
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
