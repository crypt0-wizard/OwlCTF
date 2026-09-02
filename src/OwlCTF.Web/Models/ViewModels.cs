using System.ComponentModel.DataAnnotations;
using OwlCTF.Services;

namespace OwlCTF.Models;

public sealed record HomeViewModel(PlatformSettings Settings, CtfState State, string AboutHtml, string InstructionsHtml, string ContactHtml, IReadOnlyList<SponsorLogoRecord> SponsorLogos);
public sealed record ChallengesViewModel(IReadOnlyList<ChallengeRecord> Challenges, TeamRecord? Team, CtfState State, string Sort, IReadOnlyList<string> AvailableTags, string? SelectedTag);
public sealed record ChallengeInstancePanel(bool PlatformEnabled, bool ChallengeSolved, int MaxRenewals, int RenewalSeconds);
public sealed record ChallengeDetailViewModel(ChallengeRecord Challenge, IReadOnlyList<ChallengeFileRecord> Files, IReadOnlyList<ChallengeSolveRecord> Solves, TeamRecord? Team, CtfState State, string FlagPrefix, ChallengeInstancePanel? Instance);
public sealed record TeamViewModel(TeamRecord? Team, string? JoinCode, IReadOnlyList<OwlCTF.Services.CountryOption> Countries, int MaxTeamMembers);
public sealed record PublicTeamViewModel(PublicTeamRecord Team, IReadOnlyList<PublicTeamMemberRecord> Members, IReadOnlyList<PublicTeamSolveRecord> Solves);
public sealed record PublicMemberViewModel(PublicMemberRecord Member, IReadOnlyList<PublicMemberSolveRecord> Solves);

public sealed class SettingsInput
{
    [Required, StringLength(80, MinimumLength = 2)] public string PlatformName { get; set; } = "OwlCTF";
    [StringLength(10_000)] public string? AboutDescription { get; set; } = "";
    [StringLength(10_000)] public string? InstructionsDescription { get; set; } = "";
    [StringLength(10_000)] public string? ContactDescription { get; set; } = "";
    [StringLength(10_000)] public string? SponsorsDescription { get; set; } = "";
}

public sealed class EventScheduleInput
{
    [Display(Name = "Start (UTC)")] public DateTime? StartsAtUtc { get; set; }
    [Display(Name = "End (UTC)")] public DateTime? EndsAtUtc { get; set; }
}

public sealed class FirstBloodSettingsInput
{
    [Display(Name = "Enable first-blood announcements")] public bool Enabled { get; set; }
    [StringLength(500), Display(Name = "Discord webhook URL")] public string? WebhookUrl { get; set; }
    [Display(Name = "Remove saved webhook")] public bool RemoveWebhook { get; set; }
}

public sealed class FlagFormatInput
{
    [Required, StringLength(16, MinimumLength = 2), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Use only letters and numbers.")]
    [Display(Name = "Dynamic flag prefix")]
    public string FlagPrefix { get; set; } = "CTF";
}

public sealed class TeamCapacityInput
{
    [Range(TeamCapacityPolicy.MinimumMembers, TeamCapacityPolicy.MaximumMembers)]
    [Display(Name = "Maximum members per team")]
    public int MaxTeamMembers { get; set; } = TeamCapacityPolicy.DefaultMaxMembers;
}

public sealed class ChallengeInput
{
    public Guid? Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$"), StringLength(140)] public string Slug { get; set; } = "";
    [Required, StringLength(30_000)] public string Description { get; set; } = "";
    [Required, StringLength(100)] public string Author { get; set; } = "";
    [Required, Display(Name = "Category"), StringLength(ChallengeCategoryPolicy.MaximumKeyLength)] public string CategoryKey { get; set; } = ChallengeCategoryCatalog.DefaultKey;
    [StringLength(ChallengeCategoryPolicy.MaximumNameLength), Display(Name = "Custom category name")] public string? CustomCategoryName { get; set; }
    [StringLength(255), Display(Name = "Tags")] public string? Tags { get; set; }
    [Range(1, 100_000), Display(Name = "Initial value")] public int Initial { get; set; } = 100;
    [Range(1, 100_000), Display(Name = "Minimum value")] public int Minimum { get; set; } = 100;
    [Range(0, 100_000), Display(Name = "Decay solves")] public int Decay { get; set; }
    [StringLength(500)] public string? Flag { get; set; }
    [Required, RegularExpression("^(exact|regex)$"), Display(Name = "Flag matching")] public string FlagMatchMode { get; set; } = "exact";
    [Display(Name = "Visible to players")] public bool IsVisible { get; set; }
    [Display(Name = "Enable per-team Docker instance")] public bool DynamicInstanceEnabled { get; set; }
    [StringLength(255)] public string? DockerImage { get; set; }
    [Range(1, 65535)] public int ContainerPort { get; set; } = 8080;
    [Range(30, 86400), Display(Name = "Instance TTL (seconds)")] public int InstanceTtlSeconds { get; set; } = 1800;
    [Range(0, 20)] public int MaxInstanceRenewals { get; set; } = 3;
    [Range(10_000_000, 16_000_000_000)] public long InstanceNanoCpus { get; set; } = 500_000_000;
    [Range(16, 32768), Display(Name = "Memory limit (MB)")] public int InstanceMemoryMb { get; set; } = 256;
    [RegularExpression("^[A-Za-z_][A-Za-z0-9_]{0,79}$"), StringLength(80)] public string FlagEnvironmentVariable { get; set; } = "FLAG";
    public List<IFormFile> Files { get; set; } = [];
}

public sealed class TeamInput
{
    [Required, StringLength(TeamNamePolicy.MaxLength, MinimumLength = 2)] public string Name { get; set; } = "";
    [StringLength(32)] public string JoinCode { get; set; } = "";
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; set; } = "";
    [Required, StringLength(30)] public string BracketKey { get; set; } = "";
    [StringLength(50)] public string? Status { get; set; }
}

public sealed class TeamSettingsInput
{
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; set; } = "";
    [Required, StringLength(30)] public string BracketKey { get; set; } = OwlCTF.Services.TeamBracketCatalog.DefaultKey;
    [StringLength(50)] public string? Status { get; set; }
}

public sealed class TeamSuspensionInput
{
    public bool Suspended { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class DisbandTeamInput
{
    [Required, StringLength(80)] public string TeamName { get; set; } = "";
}

public sealed class SubmissionInput
{
    [Required] public Guid ChallengeId { get; set; }
    [Required, StringLength(500)] public string Flag { get; set; } = "";
}
