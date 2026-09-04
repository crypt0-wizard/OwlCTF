using OwlCTF.Services;

namespace OwlCTF.Models;

public sealed record PlatformSettings(string PlatformName, string AboutDescription, string ContactDescription, string SponsorsDescription, DateTime? StartsAtUtc, DateTime? EndsAtUtc, string? NavbarLogoPath = null, string? FaviconPath = BrandingStorage.DefaultFaviconPath, bool FirstBloodEnabled = false, string? FirstBloodWebhookUrl = null, string FlagPrefix = "CTF", int MaxTeamMembers = TeamCapacityPolicy.DefaultMaxMembers, string InstructionsDescription = "")
{
    public bool LoginEnabled { get; init; } = true;
}
public sealed record UserRecord(Guid Id, string DiscordId, string Username, string? AvatarHash, bool IsAdmin);
public sealed record AdminUserRecord(Guid Id, string Username, string DiscordId, bool IsAdmin, DateTime LastLoginAtUtc);
public sealed record ProfileRecord(Guid Id, string DiscordId, string Username, string? AvatarHash, bool IsAdmin, DateTime CreatedAtUtc, DateTime LastLoginAtUtc, Guid? TeamId, string? TeamName, decimal Score, long SolveCount);
public sealed record TeamRecord(Guid Id, string Name, Guid CaptainUserId, DateTime CreatedAtUtc, string? CountryCode, string? Status, string BracketKey, bool IsSuspended);
public sealed record AdminTeamRecord(Guid Id, string Name, string? CountryCode, string? Status, string BracketKey, string JoinCode, bool IsSuspended);
public sealed record AdminManagedTeamRecord(Guid Id, string Name, string? CountryCode, string BracketKey, string? Status, DateTime CreatedAtUtc, string CaptainUsername, long MemberCount, decimal Score, long SolveCount, string JoinCode, bool IsSuspended, string? SuspensionReason, DateTime? SuspendedAtUtc, bool IsBanned, bool IsAutoBanned, string? SecurityReason, DateTime? BannedAtUtc, bool IsDisbanded, DateTime? DisbandedAtUtc);
public sealed record ChallengeRecord(Guid Id, string Title, string Slug, string Description, string Author, string CategoryKey, string Tags, int Initial, int Minimum, int Decay, int CurrentValue, bool IsVisible, long SolveCount, int IsSolvedValue)
{
    public bool IsSolved => IsSolvedValue != 0;
    public int Points => CurrentValue;
    public IReadOnlyList<string> TagList => ChallengeTagPolicy.FromStored(Tags);
}
public sealed record AdminManagedChallengeRecord(Guid Id, string Title, string Slug, string Author, string CategoryKey, int Initial, int Minimum, int Decay, int CurrentValue, bool IsVisible, long SolveCount, long FileCount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{ public int Points => CurrentValue; }
public sealed record AdminSubmissionLogRecord(
    long Id, Guid ChallengeId, string ChallengeTitle, Guid TeamId, string TeamName, string? CountryCode,
    Guid UserId, string Username, string SubmittedFlag, bool IsCorrect, string? IpAddress, DateTime SubmittedAtUtc,
    string? CheatIncidentId, string? FlagOwnerTeamId, string? FlagOwnerTeamName, string? FlagOwnerChallengeId,
    string? FlagOwnerChallengeTitle, int AutoBanAppliedValue, DateTime? ManualBanAtUtc, bool SubmittingTeamIsBanned)
{
    public bool AutoBanApplied => AutoBanAppliedValue != 0;
}
public sealed record AdminSubmissionLogSummary(long Total, long Correct, long Incorrect, long CrossTeam);
public sealed record AdminSubmissionLogPage(IReadOnlyList<AdminSubmissionLogRecord> Attempts, long MatchCount, AdminSubmissionLogSummary Summary);
public sealed record SubmissionRecordResult(long AttemptId, bool Awarded);
public sealed record ChallengeFileRecord(Guid Id, Guid ChallengeId, string OriginalName, string StorageName, long SizeBytes, string Sha256);
public sealed record ChallengeSolveRecord(long Rank, Guid TeamId, string TeamName, string? CountryCode, Guid UserId, string SolverUsername, int PointsAwarded, DateTime SolvedAtUtc);
public sealed record RecentSolveRecord(Guid Id, Guid ChallengeId, string ChallengeTitle, string CategoryKey, Guid TeamId, string TeamName, string? CountryCode, Guid UserId, string Username, int PointsAwarded, DateTime SolvedAtUtc);
public sealed record PublicSolveFeedRecord(Guid Id, Guid ChallengeId, string ChallengeTitle, Guid TeamId, string TeamName, Guid UserId, string Username, int PointsAwarded, DateTime SolvedAtUtc, long SolveRank);
public sealed record PublicTeamRestrictionRecord(Guid TeamId, string TeamName, string Kind, DateTime OccurredAtUtc)
{
    public IReadOnlyList<string> Members { get; init; } = [];
}
public sealed record FirstBloodAnnouncement(Guid Id, Guid ChallengeId, Guid SolveId, Guid TeamId, Guid UserId, string ChallengeTitle, string TeamName, string Username, int PointsAwarded, DateTime SolvedAtUtc, int AttemptCount);
public sealed record ChallengeSecret(string FlagHash, string? FlagRegex, int CurrentValue, bool IsVisible);
public sealed record CustomChallengeCategoryRecord(string Key, string Name);
public sealed record StandingRecord(long Rank, Guid TeamId, string TeamName, string? CountryCode, string BracketKey, decimal Score, long SolveCount, DateTime? LastSolveAtUtc);
public sealed record ScorePoint(DateTime AtUtc, decimal Score);
public sealed record TeamScoreSeries(Guid TeamId, string TeamName, string? CountryCode, IReadOnlyList<ScorePoint> Points);
public sealed record PublicTeamRecord(Guid Id, string Name, string? CountryCode, string? Status, string BracketKey, decimal Score, long SolveCount, bool IsDisbanded);
public sealed record PublicTeamMemberRecord(Guid Id, string DiscordId, string Username, string? AvatarHash, decimal PointsEarned, long SolveCount);
public sealed record PublicTeamSolveRecord(Guid ChallengeId, string ChallengeTitle, string CategoryKey, int PointsAwarded, DateTime SolvedAtUtc, Guid SolverUserId, string SolverUsername);
public sealed record PublicMemberRecord(Guid Id, string DiscordId, string Username, string? AvatarHash, Guid TeamId, string TeamName, string? CountryCode, string BracketKey);
public sealed record PublicMemberSolveRecord(Guid ChallengeId, string ChallengeTitle, string CategoryKey, int PointsAwarded, DateTime SolvedAtUtc);
public sealed record StandingsViewModel(IReadOnlyList<StandingRecord> Standings, IReadOnlyList<TeamScoreSeries> Series, DateTime ChartStartUtc, DateTime ChartEndUtc, CtfState State);
public sealed record AdminDashboardViewModel(PlatformSettings Settings);
public sealed record AdminTeamsViewModel(IReadOnlyList<AdminManagedTeamRecord> Teams, string Sort, string Direction);
public sealed record AdminChallengesViewModel(IReadOnlyList<AdminManagedChallengeRecord> Challenges, string Sort, string Direction);
public sealed record AdminUsersViewModel(IReadOnlyList<AdminUserRecord> Users, string Query, string Sort, string Direction, int TotalUsers, int AdministratorCount);
public sealed record AdminSubmissionLogsViewModel(IReadOnlyList<AdminSubmissionLogRecord> Attempts, AdminSubmissionLogSummary Summary, string Query, string Result, string Direction, int Page, int PageSize, long MatchCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(MatchCount / (double)PageSize));
}
public enum CtfPhase { Unscheduled, Upcoming, Live, Ended }
public enum TeamJoinResult { Joined, InvalidCode, TeamFull }
public enum TeamExitResult { Completed, NotMember, CaptainMustDisband, NotCaptain, NameMismatch }
public sealed record CtfState(CtfPhase Phase, DateTime? StartsAtUtc, DateTime? EndsAtUtc)
{
    public static CtfState From(PlatformSettings settings, DateTime nowUtc)
    {
        if (settings.StartsAtUtc is null && settings.EndsAtUtc is null) return new(CtfPhase.Unscheduled, null, null);
        if (settings.StartsAtUtc is { } start && nowUtc < start) return new(CtfPhase.Upcoming, start, settings.EndsAtUtc);
        if (settings.EndsAtUtc is null || nowUtc <= settings.EndsAtUtc) return new(CtfPhase.Live, settings.StartsAtUtc, settings.EndsAtUtc);
        return new(CtfPhase.Ended, settings.StartsAtUtc, settings.EndsAtUtc);
    }
}
