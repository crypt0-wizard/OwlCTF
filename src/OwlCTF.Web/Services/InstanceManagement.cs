using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OwlCTF.Data;
using OwlCTF.Options;

namespace OwlCTF.Services;

public sealed record ContainerLaunchRequest(string Image, int ContainerPort, string FlagEnvironmentVariable, string Flag,
    long NanoCpus, long MemoryBytes, Guid InstanceId, Guid TeamId, Guid ChallengeId);
public sealed record ContainerLaunchResult(string ContainerId, int HostPort);

public interface IContainerRuntime
{
    Task<ContainerLaunchResult> StartAsync(ContainerLaunchRequest request, CancellationToken ct);
    Task StopAndRemoveAsync(string containerId, CancellationToken ct);
}

public sealed record InstanceReservation(ChallengeInstance Instance, ChallengeInstanceConfig Config);
public sealed record InstanceView(Guid ChallengeId, string Status, string? Host, int? Port, DateTime? ExpiresAtUtc, int RenewalCount, string? Message);

public interface IInstanceStore
{
    Task<ChallengeInstanceConfig?> GetConfigAsync(Guid challengeId, CancellationToken ct);
    Task<ChallengeInstance?> GetCurrentAsync(Guid teamId, Guid challengeId, CancellationToken ct);
    Task<bool> HasSolvedAsync(Guid teamId, Guid challengeId, CancellationToken ct);
    Task<InstanceReservation> ReserveAsync(Guid teamId, Guid challengeId, string flagHash, DateTime now, int globalLimit, CancellationToken ct);
    Task MarkStartedAsync(Guid id, string containerId, int hostPort, CancellationToken ct);
    Task MarkFailedAsync(Guid id, string reason, CancellationToken ct);
    Task<ChallengeInstance?> ClaimForStopAsync(Guid teamId, Guid challengeId, CancellationToken ct);
    Task MarkStoppedAsync(Guid id, ChallengeInstanceStatus status, string? failure, CancellationToken ct);
    Task<ChallengeInstance?> RenewAsync(Guid teamId, Guid challengeId, DateTime now, int renewalSeconds, int maximumLifetimeSeconds, CancellationToken ct);
}

public interface IExpiredInstanceStore
{
    Task<IReadOnlyList<ChallengeInstance>> ClaimExpiredAsync(DateTime now, int limit, CancellationToken ct);
    Task CompleteExpiryAsync(Guid id, string? failure, CancellationToken ct);
}

public enum FlagOwnershipDisposition { NotInstanceFlag, OwnedBySubmittingTeam, WrongChallenge, OwnedByAnotherTeam }
public sealed record IssuedFlagOwner(Guid ChallengeInstanceId, Guid ChallengeId, Guid TeamId, string FlagHash);
public sealed record FlagOwnershipResult(
    FlagOwnershipDisposition Disposition,
    Guid? OwningTeamId = null,
    Guid? ChallengeInstanceId = null,
    Guid? OwningChallengeId = null,
    string? FlagHash = null);

public interface IFlagOwnershipStore
{
    Task<IssuedFlagOwner?> FindIssuedFlagAsync(string flagHash, CancellationToken ct);
    Task AddIncidentAsync(CheatIncident incident, CancellationToken ct);
    Task MarkIncidentNotifiedAsync(Guid incidentId, CancellationToken ct);
    Task BanTeamAsync(Guid teamId, string reason, CancellationToken ct);
    Task<bool> RevokeTeamBanAsync(Guid teamId, CancellationToken ct);
    Task MarkIncidentAutoBanAsync(Guid incidentId, CancellationToken ct);
    Task<CheatIncident?> GetIncidentAsync(Guid incidentId, CancellationToken ct);
    Task MarkIncidentManualBanAsync(Guid incidentId, Guid adminUserId, CancellationToken ct);
}

public interface ICheatIncidentNotifier
{
    Task<bool> NotifyAsync(CheatIncident incident, CancellationToken ct);
}

public sealed class InstanceLifecycleService(
    IInstanceStore store,
    IContainerRuntime runtime,
    FlagHasher hasher,
    PlatformService platform,
    IOptions<DynamicInstanceOptions> configured,
    TimeProvider clock)
{
    private readonly DynamicInstanceOptions options = configured.Value;

    public async Task<InstanceView> StartAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    {
        EnsureEnabled();
        await EnsureNotSolvedAsync(teamId, challengeId, ct);
        var flag = GenerateFlag((await platform.GetAsync(ct)).FlagPrefix);
        var reservation = await store.ReserveAsync(teamId, challengeId, hasher.Hash(flag), clock.GetUtcNow().UtcDateTime, options.GlobalConcurrencyLimit, ct);
        try
        {
            var result = await runtime.StartAsync(new(
                reservation.Config.DockerImage,
                reservation.Config.ContainerPort,
                reservation.Config.FlagEnvironmentVariable,
                flag,
                reservation.Config.NanoCpus,
                reservation.Config.MemoryBytes,
                reservation.Instance.Id,
                teamId,
                challengeId), ct);
            await store.MarkStartedAsync(reservation.Instance.Id, result.ContainerId, result.HostPort, ct);
            return ToView(reservation.Instance, ChallengeInstanceStatus.Active, result.HostPort);
        }
        catch (Exception ex)
        {
            await store.MarkFailedAsync(reservation.Instance.Id, ex.Message, CancellationToken.None);
            throw new InstanceOperationException("The challenge instance could not be started. An administrator can inspect the server log.", 503);
        }
    }

    public async Task<InstanceView> StopAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    {
        EnsureEnabled();
        var item = await store.ClaimForStopAsync(teamId, challengeId, ct)
            ?? throw new InstanceOperationException("No active instance was found.", 404);
        string? failure = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.ContainerId))
                await runtime.StopAndRemoveAsync(item.ContainerId, ct);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        await store.MarkStoppedAsync(item.Id, failure is null ? ChallengeInstanceStatus.Stopped : ChallengeInstanceStatus.Failed, failure, ct);
        if (failure is not null)
            throw new InstanceOperationException("The instance lease stopped, but Docker cleanup needs administrator attention.", 503);
        return ToView(item, ChallengeInstanceStatus.Stopped, null);
    }

    public async Task<InstanceView> RenewAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    {
        EnsureEnabled();
        await EnsureNotSolvedAsync(teamId, challengeId, ct);
        var item = await store.RenewAsync(teamId, challengeId, clock.GetUtcNow().UtcDateTime, options.RenewalSeconds, options.MaximumLifetimeSeconds, ct)
            ?? throw new InstanceOperationException("No active instance was found.", 404);
        return ToView(item, item.Status, item.HostPort);
    }

    public async Task<InstanceView> GetAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    {
        EnsureEnabled();
        _ = await store.GetConfigAsync(challengeId, ct)
            ?? throw new InstanceOperationException("This challenge does not provide a dynamic instance.", 404);
        var item = await store.GetCurrentAsync(teamId, challengeId, ct);
        return item is null
            ? new(challengeId, "Stopped", null, null, null, 0, "No active instance.")
            : ToView(item, item.Status, item.HostPort);
    }

    private InstanceView ToView(ChallengeInstance item, ChallengeInstanceStatus status, int? port) =>
        new(
            item.ChallengeId,
            status.ToString(),
            port is null ? null : options.PublicHost,
            port,
            status is ChallengeInstanceStatus.Active or ChallengeInstanceStatus.Provisioning
                ? DateTime.SpecifyKind(item.ExpiresAtUtc, DateTimeKind.Utc)
                : null,
            item.RenewalCount,
            null);

    private static string GenerateFlag(string configuredPrefix)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return FlagPrefixPolicy.Normalize(configuredPrefix) + "{" + value + "}";
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
            throw new InstanceOperationException("Dynamic challenge instances are not enabled on this platform.", 503);
    }

    private async Task EnsureNotSolvedAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    {
        if (await store.HasSolvedAsync(teamId, challengeId, ct))
            throw new InstanceOperationException("Your team has already solved this challenge.", 409);
    }
}

public sealed class InstanceExpiryProcessor(
    IExpiredInstanceStore store,
    IContainerRuntime runtime,
    TimeProvider clock,
    ILogger<InstanceExpiryProcessor> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var items = await store.ClaimExpiredAsync(clock.GetUtcNow().UtcDateTime, 50, ct);
        foreach (var item in items)
        {
            string? failure = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(item.ContainerId))
                    await runtime.StopAndRemoveAsync(item.ContainerId, ct);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                logger.LogError(ex, "Failed to remove expired instance {InstanceId}", item.Id);
            }
            await store.CompleteExpiryAsync(item.Id, failure, ct);
        }
        return items.Count;
    }
}

public sealed class InstanceExpiryReaper(
    InstanceExpiryProcessor processor,
    IOptions<DynamicInstanceOptions> configured,
    ILogger<InstanceExpiryReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configured.Value.Enabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configured.Value.ReaperIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dynamic instance expiry pass failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }
}
