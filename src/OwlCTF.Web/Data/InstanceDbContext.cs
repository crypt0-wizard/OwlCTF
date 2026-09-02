using Microsoft.EntityFrameworkCore;

namespace OwlCTF.Data;

public enum ChallengeInstanceStatus { Provisioning, Active, Stopping, Stopped, Expired, Failed }

public sealed class ChallengeInstanceConfig
{
    public Guid ChallengeId { get; set; }
    public bool Enabled { get; set; }
    public string DockerImage { get; set; } = "";
    public int ContainerPort { get; set; }
    public int TtlSeconds { get; set; } = 1_800;
    public int MaxRenewals { get; set; } = 3;
    public long NanoCpus { get; set; } = 500_000_000;
    public long MemoryBytes { get; set; } = 268_435_456;
    public string FlagEnvironmentVariable { get; set; } = "FLAG";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ChallengeInstance
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string? ContainerId { get; set; }
    public int? HostPort { get; set; }
    public ChallengeInstanceStatus Status { get; set; }
    public string? ActiveLeaseKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int RenewalCount { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class InstanceFlag
{
    public Guid Id { get; set; }
    public Guid ChallengeInstanceId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public string FlagHash { get; set; } = "";
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class CheatIncident
{
    public Guid Id { get; set; }
    public Guid SubmittingTeamId { get; set; }
    public Guid OwningTeamId { get; set; }
    public Guid SubmittingUserId { get; set; }
    public Guid SubmittedChallengeId { get; set; }
    public Guid OwningChallengeId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Evidence { get; set; } = "";
    public bool AdminNotified { get; set; }
    public DateTime? AdminNotifiedAtUtc { get; set; }
    public bool AutoBanApplied { get; set; }
}

public sealed class TeamSecurityState
{
    public Guid Id { get; set; }
    public bool IsBanned { get; set; }
    public bool IsFlagged { get; set; }
    public bool IsHidden { get; set; }
    public string? SecurityReason { get; set; }
    public DateTime? BannedAtUtc { get; set; }
    public DateTime? FlaggedAtUtc { get; set; }
}

public sealed class TeamMembership
{
    public Guid UserId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class TeamChallengeSolve
{
    public Guid ChallengeId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class InstanceCapacityLock { public byte Id { get; set; } }

public sealed class InstanceDbContext(DbContextOptions<InstanceDbContext> options) : DbContext(options)
{
    public DbSet<ChallengeInstanceConfig> InstanceConfigs => Set<ChallengeInstanceConfig>();
    public DbSet<ChallengeInstance> Instances => Set<ChallengeInstance>();
    public DbSet<InstanceFlag> InstanceFlags => Set<InstanceFlag>();
    public DbSet<CheatIncident> CheatIncidents => Set<CheatIncident>();
    public DbSet<TeamSecurityState> TeamSecurityStates => Set<TeamSecurityState>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<TeamChallengeSolve> TeamChallengeSolves => Set<TeamChallengeSolve>();
    public DbSet<InstanceCapacityLock> CapacityLocks => Set<InstanceCapacityLock>();

    protected override void OnModelCreating(ModelBuilder model)
        => ConfigureModel(model);

    internal static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ChallengeInstanceConfig>(e => { e.ToTable("ChallengeInstanceConfigs"); e.HasKey(x => x.ChallengeId); e.Property(x => x.ChallengeId).HasColumnType("char(36)"); e.Property(x => x.DockerImage).HasMaxLength(255); e.Property(x => x.FlagEnvironmentVariable).HasMaxLength(80); });
        model.Entity<ChallengeInstance>(e => { e.ToTable("ChallengeInstances"); e.HasKey(x => x.Id); GuidColumns(e, nameof(ChallengeInstance.Id), nameof(ChallengeInstance.TeamId), nameof(ChallengeInstance.ChallengeId)); e.Property(x => x.ContainerId).HasMaxLength(128); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.ActiveLeaseKey).HasMaxLength(80); e.Property(x => x.FailureReason).HasMaxLength(1_000); e.HasIndex(x => x.ActiveLeaseKey).IsUnique(); e.HasIndex(x => new { x.Status, x.ExpiresAtUtc }); });
        model.Entity<InstanceFlag>(e => { e.ToTable("InstanceFlags"); e.HasKey(x => x.Id); GuidColumns(e, nameof(InstanceFlag.Id), nameof(InstanceFlag.ChallengeInstanceId), nameof(InstanceFlag.ChallengeId), nameof(InstanceFlag.TeamId)); e.Property(x => x.FlagHash).HasMaxLength(64); e.HasIndex(x => x.FlagHash).IsUnique(); });
        model.Entity<CheatIncident>(e => { e.ToTable("CheatIncidents"); e.HasKey(x => x.Id); GuidColumns(e, nameof(CheatIncident.Id), nameof(CheatIncident.SubmittingTeamId), nameof(CheatIncident.OwningTeamId), nameof(CheatIncident.SubmittingUserId), nameof(CheatIncident.SubmittedChallengeId), nameof(CheatIncident.OwningChallengeId)); e.Property(x => x.Evidence).HasMaxLength(2_000); e.HasIndex(x => x.OccurredAtUtc); });
        model.Entity<TeamSecurityState>(e => { e.ToTable("Teams", t => t.ExcludeFromMigrations()); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnType("char(36)"); e.Property(x => x.SecurityReason).HasMaxLength(500); });
        model.Entity<TeamMembership>(e => { e.ToTable("TeamMembers", t => t.ExcludeFromMigrations()); e.HasKey(x => x.UserId); GuidColumns(e, nameof(TeamMembership.UserId), nameof(TeamMembership.TeamId)); });
        model.Entity<TeamChallengeSolve>(e => { e.ToTable("Solves", t => t.ExcludeFromMigrations()); e.HasKey(x => new { x.ChallengeId, x.TeamId }); GuidColumns(e, nameof(TeamChallengeSolve.ChallengeId), nameof(TeamChallengeSolve.TeamId)); });
        model.Entity<InstanceCapacityLock>(e => { e.ToTable("InstanceCapacityLocks"); e.HasKey(x => x.Id); });
    }

    private static void GuidColumns<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e, params string[] names) where TEntity : class
    { foreach (var name in names) e.Property<Guid>(name).HasColumnType("char(36)"); }
}
