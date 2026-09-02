using System.Data;
using OwlCTF.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace OwlCTF.Data;

public sealed class EfInstanceStore(IDbContextFactory<InstanceDbContext> factory, IMemoryCache cache) : IInstanceStore, IExpiredInstanceStore, IFlagOwnershipStore
{
    public async Task<ChallengeInstanceConfig?> GetConfigAsync(Guid challengeId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.InstanceConfigs.AsNoTracking().SingleOrDefaultAsync(x => x.ChallengeId == challengeId, ct); }

    public async Task<ChallengeInstance?> GetCurrentAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.Instances.AsNoTracking().Where(x => x.TeamId == teamId && x.ChallengeId == challengeId && x.ActiveLeaseKey != null).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct); }

    public async Task<bool> HasSolvedAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.TeamChallengeSolves.AsNoTracking().AnyAsync(x => x.TeamId == teamId && x.ChallengeId == challengeId, ct); }

    public async Task<InstanceReservation> ReserveAsync(Guid teamId, Guid challengeId, string flagHash, DateTime now, int globalLimit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        _ = await db.CapacityLocks.FromSqlRaw("SELECT Id FROM InstanceCapacityLocks WHERE Id=1 FOR UPDATE").SingleAsync(ct);
        var config = await db.InstanceConfigs.SingleOrDefaultAsync(x => x.ChallengeId == challengeId && x.Enabled, ct)
            ?? throw new InstanceOperationException("This challenge does not provide a dynamic instance.", 404);
        if (await db.TeamChallengeSolves.AnyAsync(x => x.TeamId == teamId && x.ChallengeId == challengeId, ct))
            throw new InstanceOperationException("Your team has already solved this challenge.", 409);
        if (await db.Instances.AnyAsync(x => x.TeamId == teamId && x.ChallengeId == challengeId && x.ActiveLeaseKey != null, ct))
            throw new InstanceOperationException("Your team already has an active instance for this challenge.", 409);
        var activeCount = await db.Instances.CountAsync(x => x.ActiveLeaseKey != null, ct);
        if (activeCount >= globalLimit) throw new InstanceOperationException("All instance slots are currently in use. Try again shortly.", 429);
        var ttl = Math.Clamp(config.TtlSeconds, 5, 86_400);
        var instance = new ChallengeInstance
        {
            Id = Guid.NewGuid(), TeamId = teamId, ChallengeId = challengeId, Status = ChallengeInstanceStatus.Provisioning,
            ActiveLeaseKey = $"{teamId:N}:{challengeId:N}", CreatedAtUtc = now, ExpiresAtUtc = now.AddSeconds(ttl)
        };
        db.Instances.Add(instance);
        db.InstanceFlags.Add(new InstanceFlag { Id = Guid.NewGuid(), ChallengeInstanceId = instance.Id, TeamId = teamId, ChallengeId = challengeId, FlagHash = flagHash, IssuedAtUtc = now, ExpiresAtUtc = instance.ExpiresAtUtc });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException) { throw new InstanceOperationException("Your team already has an active instance for this challenge.", 409); }
        return new(instance, config);
    }

    public async Task MarkStartedAsync(Guid id, string containerId, int hostPort, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var item = await db.Instances.SingleAsync(x => x.Id == id, ct); item.ContainerId = containerId; item.HostPort = hostPort; item.Status = ChallengeInstanceStatus.Active; await db.SaveChangesAsync(ct); }

    public async Task MarkFailedAsync(Guid id, string reason, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var item = await db.Instances.SingleAsync(x => x.Id == id, ct); item.Status = ChallengeInstanceStatus.Failed; item.ActiveLeaseKey = null; item.FailureReason = Truncate(reason); await db.SaveChangesAsync(ct); }

    public async Task<ChallengeInstance?> ClaimForStopAsync(Guid teamId, Guid challengeId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await db.Instances.FromSqlInterpolated($"SELECT * FROM ChallengeInstances WHERE TeamId={teamId} AND ChallengeId={challengeId} AND ActiveLeaseKey IS NOT NULL ORDER BY CreatedAtUtc DESC LIMIT 1 FOR UPDATE").SingleOrDefaultAsync(ct);
        if (item is not null) { item.Status = ChallengeInstanceStatus.Stopping; item.ActiveLeaseKey = null; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct); return item;
    }

    public async Task MarkStoppedAsync(Guid id, ChallengeInstanceStatus status, string? failure, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var item = await db.Instances.SingleAsync(x => x.Id == id, ct); item.Status = status; item.ActiveLeaseKey = null; item.FailureReason = failure is null ? null : Truncate(failure); if (failure is null) { item.ContainerId = null; item.HostPort = null; } await db.SaveChangesAsync(ct); }

    public async Task<ChallengeInstance?> RenewAsync(Guid teamId, Guid challengeId, DateTime now, int renewalSeconds, int maximumLifetimeSeconds, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.TeamChallengeSolves.AnyAsync(x => x.TeamId == teamId && x.ChallengeId == challengeId, ct))
            throw new InstanceOperationException("Your team has already solved this challenge.", 409);
        var item = await db.Instances.FromSqlInterpolated($"SELECT * FROM ChallengeInstances WHERE TeamId={teamId} AND ChallengeId={challengeId} AND Status='Active' AND ActiveLeaseKey IS NOT NULL LIMIT 1 FOR UPDATE").SingleOrDefaultAsync(ct);
        if (item is null) { await tx.RollbackAsync(ct); return null; }
        var config = await db.InstanceConfigs.SingleAsync(x => x.ChallengeId == challengeId, ct);
        if (item.RenewalCount >= config.MaxRenewals) throw new InstanceOperationException("This instance has reached its renewal limit.", 409);
        var hardLimit = item.CreatedAtUtc.AddSeconds(maximumLifetimeSeconds);
        var proposed = (item.ExpiresAtUtc > now ? item.ExpiresAtUtc : now).AddSeconds(renewalSeconds);
        item.ExpiresAtUtc = proposed < hardLimit ? proposed : hardLimit;
        if (item.ExpiresAtUtc <= now) throw new InstanceOperationException("This instance can no longer be renewed.", 409);
        item.RenewalCount++;
        await db.InstanceFlags.Where(x => x.ChallengeInstanceId == item.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, item.ExpiresAtUtc), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return item;
    }

    public async Task<IReadOnlyList<ChallengeInstance>> ClaimExpiredAsync(DateTime now, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var items = await db.Instances.FromSqlInterpolated($"SELECT * FROM ChallengeInstances WHERE (ActiveLeaseKey IS NOT NULL AND ExpiresAtUtc<={now}) OR (Status='Failed' AND ContainerId IS NOT NULL) ORDER BY ExpiresAtUtc LIMIT {limit} FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        foreach (var item in items) { item.Status = ChallengeInstanceStatus.Stopping; item.ActiveLeaseKey = null; }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return items;
    }

    public Task CompleteExpiryAsync(Guid id, string? failure, CancellationToken ct) => MarkStoppedAsync(id, failure is null ? ChallengeInstanceStatus.Expired : ChallengeInstanceStatus.Failed, failure, ct);

    public async Task<IssuedFlagOwner?> FindIssuedFlagAsync(string flagHash, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.InstanceFlags.AsNoTracking().Where(x => x.FlagHash == flagHash).Select(x => new IssuedFlagOwner(x.ChallengeInstanceId, x.ChallengeId, x.TeamId, x.FlagHash)).SingleOrDefaultAsync(ct); }

    public async Task AddIncidentAsync(CheatIncident incident, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); db.CheatIncidents.Add(incident); await db.SaveChangesAsync(ct); }

    public async Task MarkIncidentNotifiedAsync(Guid incidentId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); await db.CheatIncidents.Where(x => x.Id == incidentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.AdminNotified, true).SetProperty(x => x.AdminNotifiedAtUtc, DateTime.UtcNow), ct); }

    public async Task BanTeamAsync(Guid teamId, string reason, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var memberIds = await db.TeamMemberships.AsNoTracking().Where(x => x.TeamId == teamId).Select(x => x.UserId).ToArrayAsync(ct);
        var changed = await db.TeamSecurityStates.Where(x => x.Id == teamId).ExecuteUpdateAsync(
            s => s.SetProperty(x => x.IsBanned, true)
                .SetProperty(x => x.BannedAtUtc, DateTime.UtcNow)
                .SetProperty(x => x.SecurityReason, Truncate(reason, 500)), ct);
        if (changed != 1) throw new InvalidOperationException("The submitting team could not be banned.");
        foreach (var userId in memberIds) cache.Remove(TeamAccessGuardMiddleware.CacheKey(userId));
    }

    public async Task MarkIncidentAutoBanAsync(Guid incidentId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); await db.CheatIncidents.Where(x => x.Id == incidentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.AutoBanApplied, true), ct); }

    public async Task<CheatIncident?> GetIncidentAsync(Guid incidentId, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.CheatIncidents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == incidentId, ct); }

    public async Task MarkIncidentManualBanAsync(Guid incidentId, Guid adminUserId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.CheatIncidents.Where(x => x.Id == incidentId).ExecuteUpdateAsync(
            s => s.SetProperty(x => x.ManualBanAtUtc, DateTime.UtcNow)
                .SetProperty(x => x.ManualBanByUserId, adminUserId), ct);
    }

    private static string Truncate(string value, int max = 1_000) => value.Length <= max ? value : value[..max];
}

public sealed class InstanceOperationException(string message, int statusCode) : Exception(message) { public int StatusCode { get; } = statusCode; }
