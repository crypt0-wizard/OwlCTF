using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySqlConnector;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Data;

public sealed class AppDb(IConfiguration configuration, JoinCodeProtector joinCodes, FirstBloodWebhookProtector firstBloodWebhooks, DynamicChallengeScoring scoring) : IFirstBloodOutbox
{
    private string ConnectionString => configuration.GetConnectionString("MariaDb") ?? throw new InvalidOperationException("ConnectionStrings:MariaDb is required.");
    private MySqlConnection Open() => new(ConnectionString);

    public async Task InitializeAsync(CancellationToken ct)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS PlatformSettings (
          Id TINYINT NOT NULL PRIMARY KEY, PlatformName VARCHAR(80) NOT NULL, AboutDescription TEXT NOT NULL,
          ContactDescription TEXT NOT NULL DEFAULT '', SponsorsDescription TEXT NOT NULL DEFAULT '',
          InstructionsDescription TEXT NOT NULL DEFAULT '',
          LoginEnabled BOOLEAN NOT NULL DEFAULT TRUE,
          StartsAtUtc DATETIME(6) NULL, EndsAtUtc DATETIME(6) NULL,
          NavbarLogoPath VARCHAR(255) NULL DEFAULT '/images/navbar-logo.png',
          FaviconPath VARCHAR(255) NULL DEFAULT '/images/favicon.png',
          FirstBloodEnabled BOOLEAN NOT NULL DEFAULT FALSE, FirstBloodWebhookUrl TEXT NULL,
          FlagPrefix VARCHAR(16) NOT NULL DEFAULT 'CTF', MaxTeamMembers INT NOT NULL DEFAULT 5,
          UpdatedAtUtc DATETIME(6) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        INSERT IGNORE INTO PlatformSettings (Id, PlatformName, AboutDescription, UpdatedAtUtc)
        VALUES (1, 'OwlCTF', 'Welcome to OwlCTF. Pick a challenge, follow your hunch and see what breaks.\n\nYou might meet web bugs, tricky ciphers, suspicious files, odd binaries and a few surprises.\n\nSign in with Discord, team up and start hunting flags.', UTC_TIMESTAMP(6));
        CREATE TABLE IF NOT EXISTS Users (
          Id CHAR(36) NOT NULL PRIMARY KEY, DiscordId VARCHAR(32) NOT NULL UNIQUE, Username VARCHAR(100) NOT NULL,
          AvatarHash VARCHAR(100) NULL, IsAdmin BOOLEAN NOT NULL DEFAULT FALSE, CreatedAtUtc DATETIME(6) NOT NULL,
          LastLoginAtUtc DATETIME(6) NOT NULL, INDEX IX_Users_IsAdmin (IsAdmin)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS Teams (
          Id CHAR(36) NOT NULL PRIMARY KEY, Name VARCHAR(80) NOT NULL UNIQUE, CaptainUserId CHAR(36) NOT NULL,
          JoinCodeHash CHAR(64) NOT NULL UNIQUE, JoinCodeProtected TEXT NULL, CountryCode CHAR(2) NULL,
          Status VARCHAR(50) NULL, BracketKey VARCHAR(30) NOT NULL DEFAULT 'open',
          IsSuspended BOOLEAN NOT NULL DEFAULT FALSE, SuspensionReason VARCHAR(500) NULL, SuspendedAtUtc DATETIME(6) NULL,
          IsDisbanded BOOLEAN NOT NULL DEFAULT FALSE, DisbandedAtUtc DATETIME(6) NULL,
          IsBanned BOOLEAN NOT NULL DEFAULT FALSE, IsFlagged BOOLEAN NOT NULL DEFAULT FALSE, IsHidden BOOLEAN NOT NULL DEFAULT FALSE,
          SecurityReason VARCHAR(500) NULL, BannedAtUtc DATETIME(6) NULL, FlaggedAtUtc DATETIME(6) NULL,
          CreatedAtUtc DATETIME(6) NOT NULL,
          CONSTRAINT FK_Teams_Captain FOREIGN KEY (CaptainUserId) REFERENCES Users(Id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS TeamMembers (
          UserId CHAR(36) NOT NULL PRIMARY KEY, TeamId CHAR(36) NOT NULL, JoinedAtUtc DATETIME(6) NOT NULL,
          CONSTRAINT FK_TeamMembers_User FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
          CONSTRAINT FK_TeamMembers_Team FOREIGN KEY (TeamId) REFERENCES Teams(Id) ON DELETE CASCADE,
          INDEX IX_TeamMembers_TeamId (TeamId)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS Challenges (
          Id CHAR(36) NOT NULL PRIMARY KEY, Title VARCHAR(120) NOT NULL, Slug VARCHAR(140) NOT NULL UNIQUE,
          Description TEXT NOT NULL, Author VARCHAR(100) NOT NULL, CategoryKey VARCHAR(40) NOT NULL DEFAULT 'reverse-engineering',
          Points INT NOT NULL, Initial INT NOT NULL, Minimum INT NOT NULL, Decay INT NOT NULL DEFAULT 0,
          CurrentValue INT NOT NULL, FlagHash CHAR(64) NOT NULL, FlagRegex VARCHAR(500) NULL,
          IsVisible BOOLEAN NOT NULL DEFAULT FALSE, CreatedAtUtc DATETIME(6) NOT NULL, UpdatedAtUtc DATETIME(6) NOT NULL,
          INDEX IX_Challenges_Visible (IsVisible, CurrentValue)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS CustomChallengeCategories (
          `Key` VARCHAR(40) NOT NULL PRIMARY KEY, Name VARCHAR(60) NOT NULL UNIQUE, CreatedAtUtc DATETIME(6) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS ChallengeTags (
          ChallengeId CHAR(36) NOT NULL, Tag VARCHAR(24) NOT NULL, SortOrder TINYINT UNSIGNED NOT NULL,
          PRIMARY KEY (ChallengeId,Tag), UNIQUE KEY UX_ChallengeTags_Order (ChallengeId,SortOrder), INDEX IX_ChallengeTags_Tag (Tag),
          CONSTRAINT FK_ChallengeTags_Challenge FOREIGN KEY (ChallengeId) REFERENCES Challenges(Id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS ChallengeFiles (
          Id CHAR(36) NOT NULL PRIMARY KEY, ChallengeId CHAR(36) NOT NULL, OriginalName VARCHAR(255) NOT NULL,
          StorageName VARCHAR(80) NOT NULL UNIQUE, SizeBytes BIGINT NOT NULL, Sha256 CHAR(64) NOT NULL,
          CreatedAtUtc DATETIME(6) NOT NULL, CONSTRAINT FK_ChallengeFiles_Challenge FOREIGN KEY (ChallengeId) REFERENCES Challenges(Id) ON DELETE CASCADE,
          INDEX IX_ChallengeFiles_ChallengeId (ChallengeId)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS Solves (
          Id CHAR(36) NOT NULL PRIMARY KEY, ChallengeId CHAR(36) NOT NULL, TeamId CHAR(36) NOT NULL,
          UserId CHAR(36) NOT NULL, PointsAwarded INT NOT NULL, ValueAwarded INT NOT NULL, SolvedAtUtc DATETIME(6) NOT NULL,
          CONSTRAINT FK_Solves_Challenge FOREIGN KEY (ChallengeId) REFERENCES Challenges(Id),
          CONSTRAINT FK_Solves_Team FOREIGN KEY (TeamId) REFERENCES Teams(Id),
          CONSTRAINT FK_Solves_User FOREIGN KEY (UserId) REFERENCES Users(Id),
          UNIQUE KEY UX_Solves_ChallengeTeam (ChallengeId, TeamId), INDEX IX_Solves_Standings (TeamId, SolvedAtUtc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS SubmissionAttempts (
          Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, ChallengeId CHAR(36) NOT NULL, TeamId CHAR(36) NOT NULL,
          UserId CHAR(36) NOT NULL, SubmittedFlag VARCHAR(500) NOT NULL DEFAULT '', IpAddress VARCHAR(45) NULL,
          IsCorrect BOOLEAN NOT NULL, SubmittedAtUtc DATETIME(6) NOT NULL,
          INDEX IX_Attempts_UserTime (UserId, SubmittedAtUtc), INDEX IX_Attempts_ChallengeTime (ChallengeId, SubmittedAtUtc),
          INDEX IX_Attempts_Time (SubmittedAtUtc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS ChallengeInstanceConfigs (
          ChallengeId CHAR(36) NOT NULL PRIMARY KEY, Enabled BOOLEAN NOT NULL DEFAULT FALSE,
          DockerImage VARCHAR(255) NOT NULL, ContainerPort INT NOT NULL, TtlSeconds INT NOT NULL,
          MaxRenewals INT NOT NULL, NanoCpus BIGINT NOT NULL, MemoryBytes BIGINT NOT NULL,
          FlagEnvironmentVariable VARCHAR(80) NOT NULL, UpdatedAtUtc DATETIME(6) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS ChallengeInstances (
          Id CHAR(36) NOT NULL PRIMARY KEY, TeamId CHAR(36) NOT NULL, ChallengeId CHAR(36) NOT NULL,
          ContainerId VARCHAR(128) NULL, HostPort INT NULL, Status VARCHAR(20) NOT NULL,
          ActiveLeaseKey VARCHAR(80) NULL, CreatedAtUtc DATETIME(6) NOT NULL, ExpiresAtUtc DATETIME(6) NOT NULL,
          RenewalCount INT NOT NULL DEFAULT 0, FailureReason VARCHAR(1000) NULL,
          UNIQUE KEY UX_ChallengeInstances_ActiveLease (ActiveLeaseKey),
          INDEX IX_ChallengeInstances_Expiry (Status,ExpiresAtUtc), INDEX IX_ChallengeInstances_TeamChallenge (TeamId,ChallengeId)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS InstanceFlags (
          Id CHAR(36) NOT NULL PRIMARY KEY, ChallengeInstanceId CHAR(36) NOT NULL,
          ChallengeId CHAR(36) NOT NULL, TeamId CHAR(36) NOT NULL, FlagHash CHAR(64) NOT NULL,
          IssuedAtUtc DATETIME(6) NOT NULL, ExpiresAtUtc DATETIME(6) NOT NULL,
          UNIQUE KEY UX_InstanceFlags_Hash (FlagHash), INDEX IX_InstanceFlags_Instance (ChallengeInstanceId)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS CheatIncidents (
          Id CHAR(36) NOT NULL PRIMARY KEY, SubmittingTeamId CHAR(36) NOT NULL, OwningTeamId CHAR(36) NOT NULL,
          SubmittingUserId CHAR(36) NOT NULL, SubmittedChallengeId CHAR(36) NOT NULL, OwningChallengeId CHAR(36) NOT NULL,
          SubmissionAttemptId BIGINT NULL,
          OccurredAtUtc DATETIME(6) NOT NULL, Evidence VARCHAR(2000) NOT NULL, AdminNotified BOOLEAN NOT NULL DEFAULT FALSE,
          AdminNotifiedAtUtc DATETIME(6) NULL, AutoBanApplied BOOLEAN NOT NULL DEFAULT FALSE,
          ManualBanAtUtc DATETIME(6) NULL, ManualBanByUserId CHAR(36) NULL,
          UNIQUE KEY UX_CheatIncidents_SubmissionAttempt (SubmissionAttemptId),
          INDEX IX_CheatIncidents_Time (OccurredAtUtc), INDEX IX_CheatIncidents_Submitter (SubmittingTeamId,OccurredAtUtc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS FirstBloodAnnouncements (
          Id CHAR(36) NOT NULL PRIMARY KEY, ChallengeId CHAR(36) NOT NULL, SolveId CHAR(36) NOT NULL,
          TeamId CHAR(36) NOT NULL, UserId CHAR(36) NOT NULL, ChallengeTitle VARCHAR(120) NOT NULL,
          TeamName VARCHAR(80) NOT NULL, Username VARCHAR(100) NOT NULL, PointsAwarded INT NOT NULL,
          SolvedAtUtc DATETIME(6) NOT NULL, SentAtUtc DATETIME(6) NULL, AttemptCount INT NOT NULL DEFAULT 0,
          NextAttemptAtUtc DATETIME(6) NOT NULL, LastError VARCHAR(1000) NULL, ClaimExpiresAtUtc DATETIME(6) NULL,
          UNIQUE KEY UX_FirstBlood_Challenge (ChallengeId), UNIQUE KEY UX_FirstBlood_Solve (SolveId),
          INDEX IX_FirstBlood_Pending (SentAtUtc,NextAttemptAtUtc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        CREATE TABLE IF NOT EXISTS InstanceCapacityLocks (Id TINYINT NOT NULL PRIMARY KEY) ENGINE=InnoDB;
        INSERT IGNORE INTO InstanceCapacityLocks (Id) VALUES (1);
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS ContactDescription TEXT NOT NULL DEFAULT '';
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS SponsorsDescription TEXT NOT NULL DEFAULT '';
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS InstructionsDescription TEXT NOT NULL DEFAULT '';
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS NavbarLogoPath VARCHAR(255) NULL DEFAULT '/images/navbar-logo.png';
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS FaviconPath VARCHAR(255) NULL DEFAULT '/images/favicon.png';
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS FirstBloodEnabled BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS FirstBloodWebhookUrl VARCHAR(500) NULL;
        ALTER TABLE PlatformSettings MODIFY COLUMN FirstBloodWebhookUrl TEXT NULL;
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS FlagPrefix VARCHAR(16) NOT NULL DEFAULT 'CTF';
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS MaxTeamMembers INT NOT NULL DEFAULT 5;
        ALTER TABLE PlatformSettings ADD COLUMN IF NOT EXISTS LoginEnabled BOOLEAN NOT NULL DEFAULT TRUE;
        UPDATE PlatformSettings SET MaxTeamMembers=5 WHERE MaxTeamMembers < 1 OR MaxTeamMembers > 100;
        UPDATE PlatformSettings
        SET AboutDescription='Welcome to OwlCTF. Pick a challenge, follow your hunch and see what breaks.\n\nYou might meet web bugs, tricky ciphers, suspicious files, odd binaries and a few surprises.\n\nSign in with Discord, team up and start hunting flags.'
        WHERE SHA2(AboutDescription, 256) IN (
            '3d4ea8fa25e68453a9f80887d4a59f33fea7f3944cd4c1c0cc039a0dbbe7a26e',
            '2ebefa7658ed028694156c8c0fa2c278f995e0edf47e5bda08d70429ef30591f'
        );
        UPDATE PlatformSettings SET NavbarLogoPath='/images/navbar-logo.png'
        WHERE NavbarLogoPath IN ('/images/brand-mark.png','/images/owlctf-brand-symbol.png','/images/owlctf-navbar-logo.png','/images/owlctf-navbar-playful-v3.png','/images/owlctf-navbar-playful-v2.png','/images/owlctf-navbar-playful.png','/images/owlctf-mark.png','/images/owlctf-logo.png','/images/owlctf-logo-warm.png');
        UPDATE PlatformSettings SET FaviconPath='/images/favicon.png'
        WHERE FaviconPath IN ('/images/owlctf-favicon-professional.png','/images/owlctf-favicon.png');
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS JoinCodeProtected TEXT NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS CountryCode CHAR(2) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS Status VARCHAR(50) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS BracketKey VARCHAR(30) NOT NULL DEFAULT 'open';
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS IsSuspended BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS SuspensionReason VARCHAR(500) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS SuspendedAtUtc DATETIME(6) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS IsDisbanded BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS DisbandedAtUtc DATETIME(6) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS IsBanned BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS IsFlagged BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS IsHidden BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS SecurityReason VARCHAR(500) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS BannedAtUtc DATETIME(6) NULL;
        ALTER TABLE Teams ADD COLUMN IF NOT EXISTS FlaggedAtUtc DATETIME(6) NULL;
        UPDATE Teams SET BracketKey='open' WHERE BracketKey NOT IN ('open','high-school','college');
        ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS CategoryKey VARCHAR(40) NOT NULL DEFAULT 'reverse-engineering';
        ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS Initial INT NOT NULL DEFAULT 100;
        ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS Minimum INT NOT NULL DEFAULT 100;
        ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS Decay INT NOT NULL DEFAULT 0;
        ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS CurrentValue INT NOT NULL DEFAULT 100;
        ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS FlagRegex VARCHAR(500) NULL;
        ALTER TABLE Solves ADD COLUMN IF NOT EXISTS ValueAwarded INT NOT NULL DEFAULT 0;
        ALTER TABLE SubmissionAttempts ADD COLUMN IF NOT EXISTS SubmittedFlag VARCHAR(500) NOT NULL DEFAULT '';
        ALTER TABLE SubmissionAttempts ADD COLUMN IF NOT EXISTS IpAddress VARCHAR(45) NULL;
        ALTER TABLE SubmissionAttempts ADD INDEX IF NOT EXISTS IX_Attempts_Time (SubmittedAtUtc);
        ALTER TABLE CheatIncidents ADD COLUMN IF NOT EXISTS SubmissionAttemptId BIGINT NULL;
        ALTER TABLE CheatIncidents ADD COLUMN IF NOT EXISTS ManualBanAtUtc DATETIME(6) NULL;
        ALTER TABLE CheatIncidents ADD COLUMN IF NOT EXISTS ManualBanByUserId CHAR(36) NULL;
        ALTER TABLE CheatIncidents ADD UNIQUE INDEX IF NOT EXISTS UX_CheatIncidents_SubmissionAttempt (SubmissionAttemptId);
        ALTER TABLE FirstBloodAnnouncements ADD COLUMN IF NOT EXISTS ClaimExpiresAtUtc DATETIME(6) NULL;
        UPDATE Challenges SET CategoryKey='reverse-engineering'
        WHERE CategoryKey NOT IN ('reverse-engineering','web','cryptography','pwn','forensics','osint','steganography','mobile','hardware','blockchain','programming','miscellaneous')
          AND CategoryKey NOT IN (SELECT `Key` FROM CustomChallengeCategories);
        UPDATE Challenges SET Initial=Points,Minimum=Points,CurrentValue=Points
        WHERE Decay=0 AND Initial=100 AND Minimum=100 AND CurrentValue=100 AND Points<>100;
        UPDATE Solves SET ValueAwarded=PointsAwarded WHERE ValueAwarded=0 AND PointsAwarded>0;
        """;
        await using var connection = Open();
        await connection.OpenAsync(ct);
        var acquired = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT GET_LOCK('owlctf_schema_init',120)", cancellationToken: ct));
        if (acquired != 1) throw new InvalidOperationException("Could not acquire the database initialization lock.");
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
            await BackfillJoinCodesAsync(connection, ct);
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition("SELECT RELEASE_LOCK('owlctf_schema_init')"));
        }
    }

    public async Task<PlatformSettings> GetSettingsAsync(CancellationToken ct)
    {
        await using var db = Open();
        var value = await db.QuerySingleAsync<PlatformSettings>(new CommandDefinition("SELECT PlatformName,AboutDescription,COALESCE(ContactDescription,'') ContactDescription,COALESCE(SponsorsDescription,'') SponsorsDescription,StartsAtUtc,EndsAtUtc,NavbarLogoPath,COALESCE(NULLIF(TRIM(FaviconPath),''),'/images/favicon.png') FaviconPath,FirstBloodEnabled,FirstBloodWebhookUrl,COALESCE(NULLIF(TRIM(FlagPrefix),''),'CTF') FlagPrefix,MaxTeamMembers,COALESCE(InstructionsDescription,'') InstructionsDescription FROM PlatformSettings WHERE Id=1", cancellationToken: ct));
        value = value with { LoginEnabled = await db.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT LoginEnabled FROM PlatformSettings WHERE Id=1", cancellationToken: ct)) };
        var webhookUrl = firstBloodWebhooks.Unprotect(value.FirstBloodWebhookUrl);
        if (webhookUrl is null && DiscordWebhookAddress.TryNormalize(value.FirstBloodWebhookUrl, out var legacyUrl)) webhookUrl = legacyUrl;
        return value with { StartsAtUtc = Utc(value.StartsAtUtc), EndsAtUtc = Utc(value.EndsAtUtc), FirstBloodWebhookUrl = webhookUrl };
    }

    public async Task<bool> CanConnectAsync(CancellationToken ct)
    {
        try { await using var db = Open(); await db.OpenAsync(ct); return await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct)) == 1; }
        catch { return false; }
    }

    public async Task UpdateSettingsAsync(PlatformSettings settings, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET PlatformName=@PlatformName,AboutDescription=@AboutDescription,ContactDescription=@ContactDescription,SponsorsDescription=@SponsorsDescription,InstructionsDescription=@InstructionsDescription,StartsAtUtc=@StartsAtUtc,EndsAtUtc=@EndsAtUtc,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", settings, cancellationToken: ct));
    }

    public async Task UpdateHomePageAsync(string platformName, string aboutDescription, string instructionsDescription, string contactDescription, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE PlatformSettings
            SET PlatformName=@platformName,AboutDescription=@aboutDescription,
                InstructionsDescription=@instructionsDescription,
                ContactDescription=@contactDescription,
                UpdatedAtUtc=UTC_TIMESTAMP(6)
            WHERE Id=1
            """, new { platformName, aboutDescription, instructionsDescription, contactDescription }, cancellationToken: ct));
    }

    public async Task UpdateEventScheduleAsync(DateTime? startsAtUtc, DateTime? endsAtUtc, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE PlatformSettings
            SET StartsAtUtc=@startsAtUtc,EndsAtUtc=@endsAtUtc,UpdatedAtUtc=UTC_TIMESTAMP(6)
            WHERE Id=1
            """, new { startsAtUtc, endsAtUtc }, cancellationToken: ct));
    }

    public async Task UpdateNavbarLogoAsync(string? navbarLogoPath, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET NavbarLogoPath=@navbarLogoPath,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", new { navbarLogoPath }, cancellationToken: ct));
    }

    public async Task UpdateFaviconAsync(string faviconPath, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET FaviconPath=@faviconPath,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", new { faviconPath }, cancellationToken: ct));
    }

    public async Task UpdateFirstBloodSettingsAsync(bool enabled, string? webhookUrl, CancellationToken ct)
    {
        await using var db = Open();
        var protectedWebhookUrl = firstBloodWebhooks.Protect(webhookUrl);
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET FirstBloodEnabled=@enabled,FirstBloodWebhookUrl=@protectedWebhookUrl,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", new { enabled, protectedWebhookUrl }, cancellationToken: ct));
    }

    public async Task UpdateFlagPrefixAsync(string flagPrefix, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET FlagPrefix=@flagPrefix,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", new { flagPrefix }, cancellationToken: ct));
    }

    public async Task UpdateTeamCapacityAsync(int maxTeamMembers, CancellationToken ct)
    {
        if (!TeamCapacityPolicy.IsValidLimit(maxTeamMembers))
            throw new ArgumentOutOfRangeException(nameof(maxTeamMembers));
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET MaxTeamMembers=@maxTeamMembers,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", new { maxTeamMembers }, cancellationToken: ct));
    }

    public async Task<UserRecord> UpsertDiscordUserAsync(string discordId, string username, string? avatar, CancellationToken ct)
    {
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT Id FROM PlatformSettings WHERE Id=1 FOR UPDATE", transaction: tx, cancellationToken: ct));
        var existing = await db.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition("SELECT Id, DiscordId, Username, AvatarHash, IsAdmin FROM Users WHERE DiscordId=@discordId", new { discordId }, tx, cancellationToken: ct));
        if (existing is not null)
        {
            await db.ExecuteAsync(new CommandDefinition("UPDATE Users SET Username=@username, AvatarHash=@avatar, LastLoginAtUtc=UTC_TIMESTAMP(6) WHERE Id=@id", new { username, avatar, id = existing.Id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return existing with { Username = username, AvatarHash = avatar };
        }
        var isAdmin = await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Users WHERE IsAdmin=TRUE", transaction: tx, cancellationToken: ct)) == 0;
        var user = new UserRecord(Guid.NewGuid(), discordId, username, avatar, isAdmin);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO Users (Id,DiscordId,Username,AvatarHash,IsAdmin,CreatedAtUtc,LastLoginAtUtc) VALUES (@Id,@DiscordId,@Username,@AvatarHash,@IsAdmin,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))", user, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return user;
    }

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Open();
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT COALESCE(MAX(IsAdmin),FALSE) FROM Users WHERE Id=@userId", new { userId }, cancellationToken: ct));
    }

    public async Task<ProfileRecord?> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Open();
        var profile = await db.QuerySingleOrDefaultAsync<ProfileDbRecord>(new CommandDefinition("""
            SELECT u.Id,u.DiscordId,u.Username,u.AvatarHash,u.IsAdmin,u.CreatedAtUtc,u.LastLoginAtUtc,
              CAST(t.Id AS CHAR) TeamIdValue,t.Name TeamName,COALESCE(SUM(s.ValueAwarded),0) Score,COUNT(s.Id) SolveCount
            FROM Users u
            LEFT JOIN TeamMembers tm ON tm.UserId=u.Id
            LEFT JOIN Teams t ON t.Id=tm.TeamId
            LEFT JOIN Solves s ON s.TeamId=t.Id
            WHERE u.Id=@userId
            GROUP BY u.Id,u.DiscordId,u.Username,u.AvatarHash,u.IsAdmin,u.CreatedAtUtc,u.LastLoginAtUtc,t.Id,t.Name
            """, new { userId }, cancellationToken: ct));
        if (profile is null) return null;
        Guid? teamId = Guid.TryParse(profile.TeamIdValue, out var parsedTeamId) ? parsedTeamId : null;
        return new ProfileRecord(
            profile.Id, profile.DiscordId, profile.Username, profile.AvatarHash, profile.IsAdmin,
            profile.CreatedAtUtc, profile.LastLoginAtUtc, teamId, profile.TeamName, profile.Score, profile.SolveCount);
    }

    public async Task<TeamRecord?> GetTeamForUserAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleOrDefaultAsync<TeamRecord>(new CommandDefinition("SELECT t.Id,t.Name,t.CaptainUserId,t.CreatedAtUtc,t.CountryCode,t.Status,t.BracketKey,t.IsSuspended FROM Teams t JOIN TeamMembers m ON m.TeamId=t.Id WHERE m.UserId=@userId AND t.IsDisbanded=FALSE", new { userId }, cancellationToken: ct));
    }

    public async Task<string> CreateTeamAsync(Guid userId, string name, string countryCode, string bracketKey, string? status, CancellationToken ct)
    {
        var rawCode = NewJoinCode();
        var codeHash = Sha256(rawCode);
        var protectedCode = joinCodes.Protect(rawCode);
        var teamId = Guid.NewGuid();
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var maxTeamMembers = await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT MaxTeamMembers FROM PlatformSettings WHERE Id=1 FOR UPDATE", transaction: tx, cancellationToken: ct));
        if (!TeamCapacityPolicy.HasRoom(0, maxTeamMembers))
            throw new InvalidOperationException("The configured team member limit does not allow creating a team.");
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO Teams (Id,Name,CaptainUserId,JoinCodeHash,JoinCodeProtected,CountryCode,BracketKey,Status,CreatedAtUtc) VALUES (@teamId,@name,@userId,@codeHash,@protectedCode,@countryCode,@bracketKey,@status,UTC_TIMESTAMP(6))", new { teamId, name, userId, codeHash, protectedCode, countryCode, bracketKey, status }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO TeamMembers (UserId,TeamId,JoinedAtUtc) VALUES (@userId,@teamId,UTC_TIMESTAMP(6))", new { userId, teamId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return rawCode;
    }

    public async Task<TeamJoinResult> JoinTeamAsync(Guid userId, string code, CancellationToken ct)
    {
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var teamId = await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Id FROM Teams WHERE JoinCodeHash=@hash AND IsSuspended=FALSE AND IsDisbanded=FALSE FOR UPDATE",
            new { hash = Sha256(code.Trim().ToUpperInvariant()) }, tx, cancellationToken: ct));
        if (!Guid.TryParse(teamId, out var parsed))
        {
            await tx.RollbackAsync(ct);
            return TeamJoinResult.InvalidCode;
        }
        var maxTeamMembers = await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT MaxTeamMembers FROM PlatformSettings WHERE Id=1", transaction: tx, cancellationToken: ct));
        var currentMemberCount = await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM TeamMembers WHERE TeamId=@parsed", new { parsed }, tx, cancellationToken: ct));
        if (!TeamCapacityPolicy.HasRoom(currentMemberCount, maxTeamMembers))
        {
            await tx.RollbackAsync(ct);
            return TeamJoinResult.TeamFull;
        }
        await db.ExecuteAsync(new CommandDefinition(
            "INSERT INTO TeamMembers (UserId,TeamId,JoinedAtUtc) VALUES (@userId,@parsed,UTC_TIMESTAMP(6))",
            new { userId, parsed }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return TeamJoinResult.Joined;
    }

    public async Task<string?> GetTeamJoinCodeAsync(Guid teamId, Guid requesterUserId, bool isAdmin, CancellationToken ct)
    {
        await using var db = Open();
        var protectedCode = await db.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT JoinCodeProtected FROM Teams WHERE Id=@teamId AND IsDisbanded=FALSE AND (CaptainUserId=@requesterUserId OR @isAdmin=TRUE)", new { teamId, requesterUserId, isAdmin }, cancellationToken: ct));
        return joinCodes.Unprotect(protectedCode);
    }

    public async Task<bool> UpdateTeamSettingsAsync(Guid teamId, Guid requesterUserId, string countryCode, string bracketKey, string? status, CancellationToken ct)
    {
        await using var db = Open();
        return await db.ExecuteAsync(new CommandDefinition("UPDATE Teams SET CountryCode=@countryCode,BracketKey=@bracketKey,Status=@status WHERE Id=@teamId AND CaptainUserId=@requesterUserId AND IsDisbanded=FALSE", new { teamId, requesterUserId, countryCode, bracketKey, status }, cancellationToken: ct)) == 1;
    }

    public async Task<TeamExitResult> LeaveTeamAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var membership = await db.QuerySingleOrDefaultAsync<TeamExitDbRecord>(new CommandDefinition("""
            SELECT t.Id TeamId,t.Name TeamName,t.CaptainUserId
            FROM TeamMembers m JOIN Teams t ON t.Id=m.TeamId
            WHERE m.UserId=@userId AND t.IsDisbanded=FALSE
            FOR UPDATE
            """, new { userId }, tx, cancellationToken: ct));
        if (membership is null)
        {
            await tx.RollbackAsync(ct);
            return TeamExitResult.NotMember;
        }
        if (membership.CaptainUserId == userId)
        {
            await tx.RollbackAsync(ct);
            return TeamExitResult.CaptainMustDisband;
        }
        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TeamMembers WHERE UserId=@userId AND TeamId=@teamId",
            new { userId, teamId = membership.TeamId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return TeamExitResult.Completed;
    }

    public async Task<TeamExitResult> DisbandTeamAsync(Guid userId, string confirmedName, CancellationToken ct)
    {
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var membership = await db.QuerySingleOrDefaultAsync<TeamExitDbRecord>(new CommandDefinition("""
            SELECT t.Id TeamId,t.Name TeamName,t.CaptainUserId
            FROM TeamMembers m JOIN Teams t ON t.Id=m.TeamId
            WHERE m.UserId=@userId AND t.IsDisbanded=FALSE
            FOR UPDATE
            """, new { userId }, tx, cancellationToken: ct));
        if (membership is null)
        {
            await tx.RollbackAsync(ct);
            return TeamExitResult.NotMember;
        }
        if (membership.CaptainUserId != userId)
        {
            await tx.RollbackAsync(ct);
            return TeamExitResult.NotCaptain;
        }
        if (!string.Equals(membership.TeamName, confirmedName.Trim(), StringComparison.Ordinal))
        {
            await tx.RollbackAsync(ct);
            return TeamExitResult.NameMismatch;
        }
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE Teams SET IsDisbanded=TRUE,DisbandedAtUtc=UTC_TIMESTAMP(6),
              IsSuspended=TRUE,SuspensionReason='Disbanded by captain',
              SuspendedAtUtc=UTC_TIMESTAMP(6),JoinCodeProtected=NULL
            WHERE Id=@teamId AND IsDisbanded=FALSE
            """, new { teamId = membership.TeamId }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TeamMembers WHERE TeamId=@teamId",
            new { teamId = membership.TeamId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return TeamExitResult.Completed;
    }

    public async Task<IReadOnlyList<AdminTeamRecord>> GetAdminTeamsAsync(CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<AdminTeamDbRecord>(new CommandDefinition("SELECT Id,Name,CountryCode,Status,BracketKey,JoinCodeProtected,IsSuspended FROM Teams ORDER BY Name", cancellationToken: ct));
        return rows.Select(t => new AdminTeamRecord(t.Id, t.Name, t.CountryCode, t.Status, t.BracketKey, joinCodes.Unprotect(t.JoinCodeProtected) ?? "Unavailable", t.IsSuspended)).ToArray();
    }

    public async Task<IReadOnlyList<AdminManagedTeamRecord>> GetManagedTeamsAsync(CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<AdminManagedTeamDbRecord>(new CommandDefinition("""
            SELECT t.Id,t.Name,t.CountryCode,t.BracketKey,t.Status,t.CreatedAtUtc,
              captain.Username CaptainUsername,
              (SELECT COUNT(*) FROM TeamMembers members WHERE members.TeamId=t.Id) MemberCount,
              (SELECT COALESCE(SUM(s.ValueAwarded),0) FROM Solves s WHERE s.TeamId=t.Id) Score,
              (SELECT COUNT(*) FROM Solves s WHERE s.TeamId=t.Id) SolveCount,
              t.JoinCodeProtected,t.IsSuspended,t.SuspensionReason,t.SuspendedAtUtc,
              t.IsBanned,
              (t.IsBanned=TRUE
                AND t.SecurityReason LIKE 'Automatic action for cross-team instance flag incident %'
                AND EXISTS(SELECT 1 FROM CheatIncidents incident WHERE incident.SubmittingTeamId=t.Id AND incident.AutoBanApplied=TRUE)) IsAutoBannedValue,
              t.SecurityReason,t.BannedAtUtc,t.IsDisbanded,t.DisbandedAtUtc
            FROM Teams t
            JOIN Users captain ON captain.Id=t.CaptainUserId
            ORDER BY t.Name
            """, cancellationToken: ct));
        return rows.Select(team => new AdminManagedTeamRecord(
            team.Id, team.Name, team.CountryCode, team.BracketKey, team.Status, team.CreatedAtUtc,
            team.CaptainUsername, team.MemberCount, team.Score, team.SolveCount,
            joinCodes.Unprotect(team.JoinCodeProtected) ?? "Unavailable",
            team.IsSuspended, team.SuspensionReason, Utc(team.SuspendedAtUtc),
            team.IsBanned, team.IsAutoBannedValue != 0, team.SecurityReason, Utc(team.BannedAtUtc),
            team.IsDisbanded, Utc(team.DisbandedAtUtc))).ToArray();
    }

    public async Task<bool> SetTeamSuspensionAsync(Guid teamId, bool suspended, string? reason, CancellationToken ct)
    {
        await using var db = Open();
        return await db.ExecuteAsync(new CommandDefinition("""
            UPDATE Teams
            SET IsSuspended=@suspended,
                SuspensionReason=CASE WHEN @suspended THEN @reason ELSE NULL END,
                SuspendedAtUtc=CASE WHEN @suspended THEN UTC_TIMESTAMP(6) ELSE NULL END
            WHERE Id=@teamId AND IsDisbanded=FALSE
            """, new { teamId, suspended, reason }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<ChallengeRecord>> GetChallengesAsync(Guid? teamId, bool includeHidden, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<ChallengeRecord>(new CommandDefinition("""
            SELECT c.Id,c.Title,c.Slug,c.Description,c.Author,c.CategoryKey,
              COALESCE((SELECT GROUP_CONCAT(ct.Tag ORDER BY ct.SortOrder SEPARATOR ',') FROM ChallengeTags ct WHERE ct.ChallengeId=c.Id),'') Tags,
              c.Initial,c.Minimum,c.Decay,c.CurrentValue,c.IsVisible,
              (SELECT COUNT(*) FROM Solves s JOIN Teams st ON st.Id=s.TeamId WHERE s.ChallengeId=c.Id AND st.IsBanned=FALSE AND st.IsHidden=FALSE AND st.IsSuspended=FALSE AND st.IsDisbanded=FALSE) SolveCount,
              EXISTS(SELECT 1 FROM Solves s2 WHERE s2.ChallengeId=c.Id AND s2.TeamId=@teamId) IsSolvedValue
            FROM Challenges c WHERE (@includeHidden=TRUE OR c.IsVisible=TRUE) ORDER BY c.CurrentValue,c.Title
            """, new { teamId, includeHidden }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomChallengeCategoryRecord>> GetCustomChallengeCategoriesAsync(CancellationToken ct)
    {
        await using var db = Open();
        return (await db.QueryAsync<CustomChallengeCategoryRecord>(new CommandDefinition(
            "SELECT `Key`,Name FROM CustomChallengeCategories ORDER BY Name,`Key`", cancellationToken: ct))).AsList();
    }

    public async Task<bool> CustomChallengeCategoryExistsAsync(string key, CancellationToken ct)
    {
        await using var db = Open();
        return await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM CustomChallengeCategories WHERE `Key`=@key", new { key }, cancellationToken: ct)) == 1;
    }

    public async Task<bool> TryAddCustomChallengeCategoryAsync(string key, string name, CancellationToken ct)
    {
        await using var db = Open();
        try
        {
            return await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO CustomChallengeCategories (`Key`,Name,CreatedAtUtc) VALUES (@key,@name,UTC_TIMESTAMP(6))",
                new { key, name }, cancellationToken: ct)) == 1;
        }
        catch (MySqlException ex) when (ex.Number == 1062) { return false; }
    }

    public async Task<IReadOnlyList<AdminManagedChallengeRecord>> GetManagedChallengesAsync(CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<AdminManagedChallengeRecord>(new CommandDefinition("""
            SELECT c.Id,c.Title,c.Slug,c.Author,c.CategoryKey,c.Initial,c.Minimum,c.Decay,c.CurrentValue,c.IsVisible,
              (SELECT COUNT(*) FROM Solves s JOIN Teams st ON st.Id=s.TeamId WHERE s.ChallengeId=c.Id AND st.IsBanned=FALSE AND st.IsHidden=FALSE AND st.IsSuspended=FALSE AND st.IsDisbanded=FALSE) SolveCount,
              (SELECT COUNT(*) FROM ChallengeFiles f WHERE f.ChallengeId=c.Id) FileCount,
              c.CreatedAtUtc,c.UpdatedAtUtc
            FROM Challenges c
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<ChallengeRecord?> GetChallengeAsync(Guid id, Guid? teamId, bool includeHidden, CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleOrDefaultAsync<ChallengeRecord>(new CommandDefinition("""
            SELECT c.Id,c.Title,c.Slug,c.Description,c.Author,c.CategoryKey,
              COALESCE((SELECT GROUP_CONCAT(ct.Tag ORDER BY ct.SortOrder SEPARATOR ',') FROM ChallengeTags ct WHERE ct.ChallengeId=c.Id),'') Tags,
              c.Initial,c.Minimum,c.Decay,c.CurrentValue,c.IsVisible,
              (SELECT COUNT(*) FROM Solves s JOIN Teams st ON st.Id=s.TeamId WHERE s.ChallengeId=c.Id AND st.IsBanned=FALSE AND st.IsHidden=FALSE AND st.IsSuspended=FALSE AND st.IsDisbanded=FALSE) SolveCount,
              EXISTS(SELECT 1 FROM Solves s2 WHERE s2.ChallengeId=c.Id AND s2.TeamId=@teamId) IsSolvedValue
            FROM Challenges c WHERE c.Id=@id AND (@includeHidden=TRUE OR c.IsVisible=TRUE)
            """, new { id, teamId, includeHidden }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ChallengeFileRecord>> GetChallengeFilesAsync(Guid challengeId, CancellationToken ct)
    {
        await using var db = Open();
        return (await db.QueryAsync<ChallengeFileRecord>(new CommandDefinition("SELECT Id,ChallengeId,OriginalName,StorageName,SizeBytes,Sha256 FROM ChallengeFiles WHERE ChallengeId=@challengeId ORDER BY OriginalName", new { challengeId }, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<ChallengeSolveRecord>> GetChallengeSolvesAsync(Guid challengeId, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<ChallengeSolveRecord>(new CommandDefinition("""
            SELECT CAST(ROW_NUMBER() OVER (ORDER BY s.SolvedAtUtc,s.Id) AS SIGNED) `Rank`,
              t.Id TeamId,t.Name TeamName,t.CountryCode,u.Id UserId,u.Username SolverUsername,
              s.ValueAwarded PointsAwarded,s.SolvedAtUtc
            FROM Solves s
            JOIN Teams t ON t.Id=s.TeamId
            JOIN Users u ON u.Id=s.UserId
            WHERE s.ChallengeId=@challengeId AND t.IsBanned=FALSE AND t.IsHidden=FALSE AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            ORDER BY s.SolvedAtUtc,s.Id
            """, new { challengeId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<RecentSolveRecord>> GetRecentSolvesAsync(int limit, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<RecentSolveRecord>(new CommandDefinition("""
            SELECT s.Id,c.Id ChallengeId,c.Title ChallengeTitle,c.CategoryKey,
              t.Id TeamId,t.Name TeamName,t.CountryCode,u.Id UserId,u.Username,
              s.ValueAwarded PointsAwarded,s.SolvedAtUtc
            FROM Solves s
            JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
            JOIN Teams t ON t.Id=s.TeamId
            JOIN Users u ON u.Id=s.UserId
            WHERE t.IsBanned=FALSE AND t.IsHidden=FALSE AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            ORDER BY s.SolvedAtUtc DESC,s.Id DESC
            LIMIT @limit
            """, new { limit = Math.Clamp(limit, 1, 250) }, cancellationToken: ct));
        return rows.Select(Utc).ToArray();
    }

    public async Task<IReadOnlyList<PublicSolveFeedRecord>> GetPublicSolveFeedAsync(int limit, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<PublicSolveFeedRecord>(new CommandDefinition("""
            WITH EligibleSolves AS (
              SELECT s.Id,s.ChallengeId,c.Title ChallengeTitle,s.TeamId,t.Name TeamName,
                s.UserId,u.Username,s.ValueAwarded PointsAwarded,s.SolvedAtUtc,
                CAST(ROW_NUMBER() OVER (PARTITION BY s.ChallengeId ORDER BY s.SolvedAtUtc,s.Id) AS SIGNED) SolveRank
              FROM Solves s
              JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
              JOIN Teams t ON t.Id=s.TeamId
              JOIN Users u ON u.Id=s.UserId
              WHERE t.IsBanned=FALSE AND t.IsHidden=FALSE AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            )
            SELECT Id,ChallengeId,ChallengeTitle,TeamId,TeamName,UserId,Username,PointsAwarded,SolvedAtUtc,SolveRank
            FROM EligibleSolves
            ORDER BY SolvedAtUtc DESC,Id DESC
            LIMIT @limit
            """, new { limit = Math.Clamp(limit, 1, 250) }, cancellationToken: ct));
        return rows.Select(row => row with { SolvedAtUtc = DateTime.SpecifyKind(row.SolvedAtUtc, DateTimeKind.Utc) }).ToArray();
    }

    public async Task<RecentSolveRecord?> GetRecentSolveAsync(Guid challengeId, Guid teamId, CancellationToken ct)
    {
        await using var db = Open();
        var row = await db.QuerySingleOrDefaultAsync<RecentSolveRecord>(new CommandDefinition("""
            SELECT s.Id,c.Id ChallengeId,c.Title ChallengeTitle,c.CategoryKey,
              t.Id TeamId,t.Name TeamName,t.CountryCode,u.Id UserId,u.Username,
              s.ValueAwarded PointsAwarded,s.SolvedAtUtc
            FROM Solves s
            JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
            JOIN Teams t ON t.Id=s.TeamId
            JOIN Users u ON u.Id=s.UserId
            WHERE s.ChallengeId=@challengeId AND s.TeamId=@teamId AND t.IsBanned=FALSE AND t.IsHidden=FALSE AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            """, new { challengeId, teamId }, cancellationToken: ct));
        return row is null ? null : Utc(row);
    }

    public async Task<ChallengeFileRecord?> GetFileAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleOrDefaultAsync<ChallengeFileRecord>(new CommandDefinition("SELECT f.Id,f.ChallengeId,f.OriginalName,f.StorageName,f.SizeBytes,f.Sha256 FROM ChallengeFiles f JOIN Challenges c ON c.Id=f.ChallengeId WHERE f.Id=@id AND c.IsVisible=TRUE", new { id }, cancellationToken: ct));
    }

    public async Task<ChallengeSecret?> GetChallengeSecretAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleOrDefaultAsync<ChallengeSecret>(new CommandDefinition("SELECT FlagHash,FlagRegex,CurrentValue,IsVisible FROM Challenges WHERE Id=@id", new { id }, cancellationToken: ct));
    }

    public async Task<SubmissionRecordResult> RecordSubmissionAsync(Guid challengeId, Guid teamId, Guid userId, string submittedFlag, string? ipAddress, bool correct, CancellationToken ct)
    {
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO SubmissionAttempts (ChallengeId,TeamId,UserId,SubmittedFlag,IpAddress,IsCorrect,SubmittedAtUtc)
            VALUES (@challengeId,@teamId,@userId,@submittedFlag,@ipAddress,@correct,UTC_TIMESTAMP(6))
            """, new { challengeId, teamId, userId, submittedFlag, ipAddress, correct }, tx, cancellationToken: ct));
        var attemptId = await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", transaction: tx, cancellationToken: ct));
        if (!correct) { await tx.CommitAsync(ct); return new(attemptId, false); }

        var challenge = await db.QuerySingleOrDefaultAsync<ChallengeScoringDbRow>(new CommandDefinition(
            "SELECT Initial,Minimum,Decay,CurrentValue FROM Challenges WHERE Id=@challengeId FOR UPDATE",
            new { challengeId }, tx, cancellationToken: ct));
        if (challenge is null) { await tx.RollbackAsync(ct); return new(attemptId, false); }
        if (await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM Solves WHERE ChallengeId=@challengeId AND TeamId=@teamId)",
            new { challengeId, teamId }, tx, cancellationToken: ct)))
        { await tx.CommitAsync(ct); return new(attemptId, false); }

        var eligiblePriorSolveCount = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM Solves s JOIN Teams t ON t.Id=s.TeamId
            WHERE s.ChallengeId=@challengeId AND t.IsBanned=FALSE AND t.IsHidden=FALSE
              AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            """, new { challengeId }, tx, cancellationToken: ct));
        var valueAwarded = scoring.Calculate(challenge.Initial, challenge.Minimum, challenge.Decay, eligiblePriorSolveCount);
        if (valueAwarded != challenge.CurrentValue)
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE Challenges SET CurrentValue=@valueAwarded,Points=@valueAwarded WHERE Id=@challengeId",
                new { valueAwarded, challengeId }, tx, cancellationToken: ct));
        var solveId = Guid.NewGuid();
        var solvedAtUtc = DateTime.UtcNow;
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO Solves (Id,ChallengeId,TeamId,UserId,PointsAwarded,ValueAwarded,SolvedAtUtc)
            VALUES (@solveId,@challengeId,@teamId,@userId,@valueAwarded,@valueAwarded,@solvedAtUtc)
            """, new { solveId, challengeId, teamId, userId, valueAwarded, solvedAtUtc }, tx, cancellationToken: ct));
        if (FirstBloodPolicy.IsFirstEligibleSolve(eligiblePriorSolveCount))
        {
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT IGNORE INTO FirstBloodAnnouncements
                  (Id,ChallengeId,SolveId,TeamId,UserId,ChallengeTitle,TeamName,Username,PointsAwarded,SolvedAtUtc,NextAttemptAtUtc)
                SELECT @announcementId,@challengeId,@solveId,@teamId,@userId,c.Title,t.Name,u.Username,@valueAwarded,@solvedAtUtc,@solvedAtUtc
                FROM Challenges c JOIN Teams t ON t.Id=@teamId JOIN Users u ON u.Id=@userId
                JOIN PlatformSettings p ON p.Id=1
                WHERE c.Id=@challengeId AND p.FirstBloodEnabled=TRUE AND NULLIF(TRIM(p.FirstBloodWebhookUrl),'') IS NOT NULL
                """, new { announcementId = Guid.NewGuid(), challengeId, solveId, teamId, userId, valueAwarded, solvedAtUtc }, tx, cancellationToken: ct));
        }
        var eligibleSolveCount = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM Solves s JOIN Teams t ON t.Id=s.TeamId
            WHERE s.ChallengeId=@challengeId AND t.IsBanned=FALSE AND t.IsHidden=FALSE
              AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            """, new { challengeId }, tx, cancellationToken: ct));
        var plan = scoring.Plan(valueAwarded, challenge.Initial, challenge.Minimum, challenge.Decay, eligibleSolveCount);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE Challenges SET CurrentValue=@NextCurrentValue,Points=@NextCurrentValue,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=@challengeId",
            new { plan.NextCurrentValue, challengeId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return new(attemptId, true);
    }

    public async Task<IReadOnlyList<FirstBloodAnnouncement>> GetDueFirstBloodAnnouncementsAsync(int limit, CancellationToken ct)
    {
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var rows = await db.QueryAsync<FirstBloodAnnouncement>(new CommandDefinition("""
            SELECT a.Id,a.ChallengeId,a.SolveId,a.TeamId,a.UserId,a.ChallengeTitle,a.TeamName,a.Username,
              a.PointsAwarded,a.SolvedAtUtc,a.AttemptCount
            FROM FirstBloodAnnouncements a JOIN PlatformSettings p ON p.Id=1
            WHERE a.SentAtUtc IS NULL AND a.NextAttemptAtUtc<=UTC_TIMESTAMP(6)
              AND (a.ClaimExpiresAtUtc IS NULL OR a.ClaimExpiresAtUtc<=UTC_TIMESTAMP(6))
              AND p.FirstBloodEnabled=TRUE AND NULLIF(TRIM(p.FirstBloodWebhookUrl),'') IS NOT NULL
            ORDER BY a.SolvedAtUtc,a.Id LIMIT @limit
            FOR UPDATE SKIP LOCKED
            """, new { limit = Math.Clamp(limit, 1, 25) }, tx, cancellationToken: ct));
        var claimed = rows.ToArray();
        if (claimed.Length > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE FirstBloodAnnouncements SET ClaimExpiresAtUtc=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 30 SECOND) WHERE Id IN @ids",
                new { ids = claimed.Select(row => row.Id).ToArray() }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return claimed.Select(x => x with { SolvedAtUtc = DateTime.SpecifyKind(x.SolvedAtUtc, DateTimeKind.Utc) }).ToArray();
    }

    public async Task MarkFirstBloodSentAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE FirstBloodAnnouncements SET SentAtUtc=UTC_TIMESTAMP(6),AttemptCount=AttemptCount+1,LastError=NULL,ClaimExpiresAtUtc=NULL WHERE Id=@id AND SentAtUtc IS NULL", new { id }, cancellationToken: ct));
    }

    public async Task MarkFirstBloodFailedAsync(Guid id, DateTime nextAttemptAtUtc, string error, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE FirstBloodAnnouncements SET AttemptCount=AttemptCount+1,NextAttemptAtUtc=@nextAttemptAtUtc,LastError=@error,ClaimExpiresAtUtc=NULL WHERE Id=@id AND SentAtUtc IS NULL", new { id, nextAttemptAtUtc, error = error.Length > 1000 ? error[..1000] : error }, cancellationToken: ct));
    }

    public async Task<AdminSubmissionLogPage> GetSubmissionLogsAsync(string query, bool? correctFilter, bool descending, int page, int pageSize, CancellationToken ct)
    {
        var direction = descending ? "DESC" : "ASC";
        var offset = (Math.Max(1, page) - 1) * pageSize;
        var pattern = $"%{query}%";
        var sql = $"""
            SELECT COUNT(*)
            FROM SubmissionAttempts a
            JOIN Challenges c ON c.Id=a.ChallengeId
            JOIN Teams t ON t.Id=a.TeamId
            JOIN Users u ON u.Id=a.UserId
            LEFT JOIN CheatIncidents i ON i.SubmissionAttemptId=a.Id
            LEFT JOIN Teams owner ON owner.Id=i.OwningTeamId
            LEFT JOIN Challenges ownerChallenge ON ownerChallenge.Id=i.OwningChallengeId
            WHERE (@query='' OR c.Title LIKE @pattern OR t.Name LIKE @pattern OR u.Username LIKE @pattern
              OR a.SubmittedFlag LIKE @pattern OR COALESCE(a.IpAddress,'') LIKE @pattern
              OR COALESCE(owner.Name,'') LIKE @pattern OR COALESCE(ownerChallenge.Title,'') LIKE @pattern)
              AND (@correctFilter IS NULL OR a.IsCorrect=@correctFilter);

            SELECT COUNT(*) Total,
              CAST(COALESCE(SUM(a.IsCorrect=TRUE),0) AS SIGNED) Correct,
              CAST(COALESCE(SUM(a.IsCorrect=FALSE),0) AS SIGNED) Incorrect,
              (SELECT COUNT(*) FROM CheatIncidents i WHERE i.SubmissionAttemptId IS NOT NULL) CrossTeam
            FROM SubmissionAttempts a;

            SELECT a.Id,c.Id ChallengeId,c.Title ChallengeTitle,t.Id TeamId,t.Name TeamName,t.CountryCode,
              u.Id UserId,u.Username,COALESCE(a.SubmittedFlag,'') SubmittedFlag,a.IsCorrect,
              a.IpAddress,a.SubmittedAtUtc,CAST(i.Id AS CHAR) CheatIncidentId,
              CAST(owner.Id AS CHAR) FlagOwnerTeamId,owner.Name FlagOwnerTeamName,
              CAST(ownerChallenge.Id AS CHAR) FlagOwnerChallengeId,ownerChallenge.Title FlagOwnerChallengeTitle,
              COALESCE(i.AutoBanApplied,FALSE) AutoBanAppliedValue,i.ManualBanAtUtc,
              t.IsBanned SubmittingTeamIsBanned
            FROM SubmissionAttempts a
            JOIN Challenges c ON c.Id=a.ChallengeId
            JOIN Teams t ON t.Id=a.TeamId
            JOIN Users u ON u.Id=a.UserId
            LEFT JOIN CheatIncidents i ON i.SubmissionAttemptId=a.Id
            LEFT JOIN Teams owner ON owner.Id=i.OwningTeamId
            LEFT JOIN Challenges ownerChallenge ON ownerChallenge.Id=i.OwningChallengeId
            WHERE (@query='' OR c.Title LIKE @pattern OR t.Name LIKE @pattern OR u.Username LIKE @pattern
              OR a.SubmittedFlag LIKE @pattern OR COALESCE(a.IpAddress,'') LIKE @pattern
              OR COALESCE(owner.Name,'') LIKE @pattern OR COALESCE(ownerChallenge.Title,'') LIKE @pattern)
              AND (@correctFilter IS NULL OR a.IsCorrect=@correctFilter)
            ORDER BY a.SubmittedAtUtc {direction},a.Id {direction}
            LIMIT @pageSize OFFSET @offset;
            """;

        await using var db = Open();
        using var results = await db.QueryMultipleAsync(new CommandDefinition(
            sql, new { query, pattern, correctFilter, pageSize, offset }, cancellationToken: ct));
        var matchCount = await results.ReadSingleAsync<long>();
        var summary = await results.ReadSingleAsync<AdminSubmissionLogSummary>();
        var attempts = (await results.ReadAsync<AdminSubmissionLogRecord>()).AsList();
        return new(attempts, matchCount, summary);
    }

    public async Task<IReadOnlyList<StandingRecord>> GetStandingsAsync(CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<StandingRecord>(new CommandDefinition("""
            SELECT CAST(ROW_NUMBER() OVER (ORDER BY Score DESC, LastSolveAtUtc ASC, TeamName ASC) AS SIGNED) `Rank`, q.* FROM (
              SELECT t.Id TeamId,t.Name TeamName,t.CountryCode,t.BracketKey,SUM(s.ValueAwarded) Score,COUNT(s.Id) SolveCount,MAX(s.SolvedAtUtc) LastSolveAtUtc
              FROM Teams t
              JOIN Solves s ON s.TeamId=t.Id
              JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
              WHERE t.IsSuspended=FALSE AND t.IsDisbanded=FALSE AND t.IsBanned=FALSE AND t.IsHidden=FALSE
                AND NOT EXISTS (
                  SELECT 1 FROM TeamMembers adminMember
                  JOIN Users adminUser ON adminUser.Id=adminMember.UserId
                  WHERE adminMember.TeamId=t.Id AND adminUser.IsAdmin=TRUE
                )
              GROUP BY t.Id,t.Name,t.CountryCode,t.BracketKey
              HAVING SUM(s.ValueAwarded)>0
            ) q ORDER BY `Rank` LIMIT 500
            """, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<PublicTeamRestrictionRecord>> GetPublicTeamRestrictionsAsync(CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<PublicTeamRestrictionRecord>(new CommandDefinition("""
            SELECT t.Id TeamId,t.Name TeamName,
              CASE
                WHEN t.IsBanned=TRUE
                  AND t.SecurityReason LIKE 'Automatic action for cross-team instance flag incident %'
                  AND EXISTS(
                    SELECT 1 FROM CheatIncidents incident
                    WHERE incident.SubmittingTeamId=t.Id AND incident.AutoBanApplied=TRUE)
                  THEN 'auto-banned'
                WHEN t.IsBanned=TRUE THEN 'banned'
                ELSE 'suspended'
              END Kind,
              COALESCE(t.BannedAtUtc,t.SuspendedAtUtc,t.CreatedAtUtc) OccurredAtUtc
            FROM Teams t
            WHERE t.IsDisbanded=FALSE AND (t.IsBanned=TRUE OR t.IsSuspended=TRUE)
            ORDER BY OccurredAtUtc,t.Name
            """, cancellationToken: ct));
        var restrictions = rows.AsList();
        if (restrictions.Count == 0) return restrictions;
        var members = await db.QueryAsync<RestrictionMemberDbRow>(new CommandDefinition("""
            SELECT tm.TeamId,u.Username
            FROM TeamMembers tm
            JOIN Users u ON u.Id=tm.UserId
            JOIN Teams t ON t.Id=tm.TeamId
            WHERE t.IsDisbanded=FALSE AND (t.IsBanned=TRUE OR t.IsSuspended=TRUE)
            ORDER BY u.Username,u.Id
            """, cancellationToken: ct));
        var byTeam = members.ToLookup(member => member.TeamId, member => member.Username);
        return restrictions.Select(team => team with { Members = byTeam[team.TeamId].ToArray() }).ToArray();
    }

    public async Task<bool> IsLoginEnabledAsync(CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleAsync<bool>(new CommandDefinition("SELECT LoginEnabled FROM PlatformSettings WHERE Id=1", cancellationToken: ct));
    }

    public async Task UpdateLoginEnabledAsync(bool enabled, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE PlatformSettings SET LoginEnabled=@enabled,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=1", new { enabled }, cancellationToken: ct));
    }

    private sealed record RestrictionMemberDbRow(Guid TeamId, string Username);

    public async Task<IReadOnlyList<TeamScoreSeries>> GetTopTeamScoreSeriesAsync(CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<ScoreGraphDbRow>(new CommandDefinition("""
            WITH TopTeams AS (
              SELECT t.Id,t.Name,t.CountryCode,SUM(s.ValueAwarded) TotalScore,MAX(s.SolvedAtUtc) LastSolveAtUtc
              FROM Teams t
              JOIN Solves s ON s.TeamId=t.Id
              JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
              WHERE t.IsSuspended=FALSE AND t.IsDisbanded=FALSE AND t.IsBanned=FALSE AND t.IsHidden=FALSE
                AND NOT EXISTS (
                  SELECT 1 FROM TeamMembers adminMember
                  JOIN Users adminUser ON adminUser.Id=adminMember.UserId
                  WHERE adminMember.TeamId=t.Id AND adminUser.IsAdmin=TRUE
                )
              GROUP BY t.Id,t.Name,t.CountryCode
              HAVING SUM(s.ValueAwarded)>0
              ORDER BY TotalScore DESC,LastSolveAtUtc ASC,t.Name ASC
              LIMIT 10
            ), Timeline AS (
              SELECT s.TeamId,s.SolvedAtUtc,
                SUM(s.ValueAwarded) OVER (PARTITION BY s.TeamId ORDER BY s.SolvedAtUtc,s.Id ROWS UNBOUNDED PRECEDING) Score
              FROM Solves s
              JOIN TopTeams top ON top.Id=s.TeamId
              JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
            )
            SELECT top.Id TeamId,top.Name TeamName,top.CountryCode,timeline.SolvedAtUtc,timeline.Score
            FROM TopTeams top LEFT JOIN Timeline timeline ON timeline.TeamId=top.Id
            ORDER BY top.TotalScore DESC,top.LastSolveAtUtc ASC,top.Name ASC,timeline.SolvedAtUtc ASC
            """, cancellationToken: ct));
        return rows.GroupBy(r => new { r.TeamId, r.TeamName, r.CountryCode })
            .Select(g => new TeamScoreSeries(g.Key.TeamId, g.Key.TeamName, g.Key.CountryCode,
                g.Where(r => r.SolvedAtUtc is not null && r.Score is not null)
                 .Select(r => new ScorePoint(DateTime.SpecifyKind(r.SolvedAtUtc!.Value, DateTimeKind.Utc), r.Score!.Value)).ToArray()))
            .ToArray();
    }

    public async Task<PublicTeamRecord?> GetPublicTeamAsync(Guid teamId, CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleOrDefaultAsync<PublicTeamRecord>(new CommandDefinition("""
            SELECT t.Id,t.Name,t.CountryCode,t.Status,t.BracketKey,COALESCE(SUM(s.ValueAwarded),0) Score,COUNT(s.Id) SolveCount,t.IsDisbanded
            FROM Teams t LEFT JOIN Solves s ON s.TeamId=t.Id
            WHERE t.Id=@teamId
            GROUP BY t.Id,t.Name,t.CountryCode,t.Status,t.BracketKey,t.IsDisbanded
            """, new { teamId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PublicTeamMemberRecord>> GetPublicTeamMembersAsync(Guid teamId, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<PublicTeamMemberRecord>(new CommandDefinition("""
            SELECT u.Id,u.DiscordId,u.Username,u.AvatarHash,
              COALESCE(SUM(CASE WHEN c.IsVisible=TRUE THEN s.ValueAwarded ELSE 0 END),0) PointsEarned,
              COUNT(CASE WHEN c.IsVisible=TRUE THEN s.Id END) SolveCount
            FROM TeamMembers tm
            JOIN Users u ON u.Id=tm.UserId
            LEFT JOIN Solves s ON s.UserId=u.Id AND s.TeamId=tm.TeamId
            LEFT JOIN Challenges c ON c.Id=s.ChallengeId
            WHERE tm.TeamId=@teamId
            GROUP BY u.Id,u.DiscordId,u.Username,u.AvatarHash
            ORDER BY PointsEarned DESC,SolveCount DESC,u.Username
            """, new { teamId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<PublicTeamSolveRecord>> GetPublicTeamSolvesAsync(Guid teamId, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<PublicTeamSolveRecord>(new CommandDefinition("""
            SELECT c.Id ChallengeId,c.Title ChallengeTitle,c.CategoryKey,s.ValueAwarded PointsAwarded,s.SolvedAtUtc,
              u.Id SolverUserId,u.Username SolverUsername
            FROM Solves s
            JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
            JOIN Users u ON u.Id=s.UserId
            WHERE s.TeamId=@teamId
            ORDER BY s.SolvedAtUtc DESC,s.Id DESC
            """, new { teamId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PublicMemberRecord?> GetPublicMemberAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Open();
        return await db.QuerySingleOrDefaultAsync<PublicMemberRecord>(new CommandDefinition("""
            SELECT u.Id,u.DiscordId,u.Username,u.AvatarHash,t.Id TeamId,t.Name TeamName,t.CountryCode,t.BracketKey
            FROM Users u
            JOIN TeamMembers tm ON tm.UserId=u.Id
            JOIN Teams t ON t.Id=tm.TeamId
            WHERE u.Id=@userId AND t.IsDisbanded=FALSE
            """, new { userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PublicMemberSolveRecord>> GetPublicMemberSolvesAsync(Guid userId, Guid teamId, CancellationToken ct)
    {
        await using var db = Open();
        var rows = await db.QueryAsync<PublicMemberSolveRecord>(new CommandDefinition("""
            SELECT c.Id ChallengeId,c.Title ChallengeTitle,c.CategoryKey,s.ValueAwarded PointsAwarded,s.SolvedAtUtc
            FROM Solves s
            JOIN Challenges c ON c.Id=s.ChallengeId AND c.IsVisible=TRUE
            WHERE s.UserId=@userId AND s.TeamId=@teamId
            ORDER BY s.SolvedAtUtc DESC,s.Id DESC
            """, new { userId, teamId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Guid> SaveChallengeAsync(Guid? id, string title, string slug, string description, string author, string categoryKey, IReadOnlyList<string> tags, int initial, int minimum, int decay, string flagHash, string? flagRegex, bool visible, CancellationToken ct)
    {
        var challengeId = id ?? Guid.NewGuid(); await using var db = Open(); await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        if (id is not null)
            _ = await db.QuerySingleOrDefaultAsync<ChallengeLockDbRow>(new CommandDefinition(
                "SELECT Id FROM Challenges WHERE Id=@challengeId FOR UPDATE", new { challengeId }, tx, cancellationToken: ct));
        var solveCount = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM Solves s JOIN Teams t ON t.Id=s.TeamId
            WHERE s.ChallengeId=@challengeId AND t.IsBanned=FALSE AND t.IsHidden=FALSE
              AND t.IsSuspended=FALSE AND t.IsDisbanded=FALSE
            """, new { challengeId }, tx, cancellationToken: ct));
        var currentValue = scoring.Calculate(initial, minimum, decay, solveCount);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO Challenges (Id,Title,Slug,Description,Author,CategoryKey,Points,Initial,Minimum,Decay,CurrentValue,FlagHash,FlagRegex,IsVisible,CreatedAtUtc,UpdatedAtUtc)
            VALUES (@challengeId,@title,@slug,@description,@author,@categoryKey,@currentValue,@initial,@minimum,@decay,@currentValue,@flagHash,@flagRegex,@visible,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE Title=@title,Slug=@slug,Description=@description,Author=@author,CategoryKey=@categoryKey,
              Points=@currentValue,Initial=@initial,Minimum=@minimum,Decay=@decay,CurrentValue=@currentValue,
              FlagHash=@flagHash,FlagRegex=@flagRegex,IsVisible=@visible,UpdatedAtUtc=UTC_TIMESTAMP(6)
            """, new { challengeId, title, slug, description, author, categoryKey, initial, minimum, decay, currentValue, flagHash, flagRegex, visible }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ChallengeTags WHERE ChallengeId=@challengeId",
            new { challengeId }, tx, cancellationToken: ct));
        if (tags.Count > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ChallengeTags (ChallengeId,Tag,SortOrder) VALUES (@challengeId,@tag,@sortOrder)",
                tags.Select((tag, sortOrder) => new { challengeId, tag, sortOrder }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return challengeId;
    }

    public async Task AddFileAsync(ChallengeFileRecord file, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO ChallengeFiles (Id,ChallengeId,OriginalName,StorageName,SizeBytes,Sha256,CreatedAtUtc) VALUES (@Id,@ChallengeId,@OriginalName,@StorageName,@SizeBytes,@Sha256,UTC_TIMESTAMP(6))", file, cancellationToken: ct));
    }

    public async Task<ChallengeFileRecord?> DeleteFileRecordAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var file = await db.QuerySingleOrDefaultAsync<ChallengeFileRecord>(new CommandDefinition("SELECT Id,ChallengeId,OriginalName,StorageName,SizeBytes,Sha256 FROM ChallengeFiles WHERE Id=@id FOR UPDATE", new { id }, tx, cancellationToken: ct));
        if (file is not null) await db.ExecuteAsync(new CommandDefinition("DELETE FROM ChallengeFiles WHERE Id=@id", new { id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return file;
    }

    public async Task ArchiveChallengeAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open();
        await db.ExecuteAsync(new CommandDefinition("UPDATE Challenges SET IsVisible=FALSE,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=@id", new { id }, cancellationToken: ct));
    }

    public async Task<(bool Deleted, IReadOnlyList<string> StorageNames)> DeleteChallengeAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var challengeId = await db.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT CAST(Id AS CHAR) FROM Challenges WHERE Id=@id FOR UPDATE", new { id }, tx, cancellationToken: ct));
        if (!Guid.TryParse(challengeId, out _))
        {
            await tx.RollbackAsync(ct);
            return (false, Array.Empty<string>());
        }

        var storageNames = (await db.QueryAsync<string>(new CommandDefinition(
            "SELECT StorageName FROM ChallengeFiles WHERE ChallengeId=@id", new { id }, tx, cancellationToken: ct))).ToArray();
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM SubmissionAttempts WHERE ChallengeId=@id", new { id }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM Solves WHERE ChallengeId=@id", new { id }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM Challenges WHERE Id=@id", new { id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (true, storageNames);
    }

    public async Task<bool> SetChallengeVisibilityAsync(Guid id, bool visible, CancellationToken ct)
    {
        await using var db = Open();
        return await db.ExecuteAsync(new CommandDefinition(
            "UPDATE Challenges SET IsVisible=@visible,UpdatedAtUtc=UTC_TIMESTAMP(6) WHERE Id=@id",
            new { id, visible }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<AdminUserRecord>> GetUsersAsync(CancellationToken ct)
    {
        await using var db = Open();
        return (await db.QueryAsync<AdminUserRecord>(new CommandDefinition("SELECT Id,Username,DiscordId,IsAdmin,LastLoginAtUtc FROM Users ORDER BY IsAdmin DESC,CreatedAtUtc", cancellationToken: ct))).AsList();
    }

    public async Task<bool> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct)
    {
        await using var db = Open(); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT Id FROM PlatformSettings WHERE Id=1 FOR UPDATE", transaction: tx, cancellationToken: ct));
        if (!isAdmin)
        {
            var adminCount = await db.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Users WHERE IsAdmin=TRUE", transaction: tx, cancellationToken: ct));
            var targetIsAdmin = await db.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT IsAdmin FROM Users WHERE Id=@userId", new { userId }, tx, cancellationToken: ct));
            if (targetIsAdmin && adminCount <= 1) { await tx.RollbackAsync(ct); return false; }
        }
        await db.ExecuteAsync(new CommandDefinition("UPDATE Users SET IsAdmin=@isAdmin WHERE Id=@userId", new { userId, isAdmin }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return true;
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewJoinCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    private static DateTime? Utc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    private static RecentSolveRecord Utc(RecentSolveRecord value) => value with { SolvedAtUtc = DateTime.SpecifyKind(value.SolvedAtUtc, DateTimeKind.Utc) };

    private async Task BackfillJoinCodesAsync(MySqlConnection db, CancellationToken ct)
    {
        var teamIds = await db.QueryAsync<Guid>(new CommandDefinition("SELECT Id FROM Teams WHERE JoinCodeProtected IS NULL AND IsDisbanded=FALSE", cancellationToken: ct));
        foreach (var teamId in teamIds)
        {
            var code = NewJoinCode();
            await db.ExecuteAsync(new CommandDefinition("UPDATE Teams SET JoinCodeHash=@hash,JoinCodeProtected=@protectedCode WHERE Id=@teamId AND JoinCodeProtected IS NULL", new { teamId, hash = Sha256(code), protectedCode = joinCodes.Protect(code) }, cancellationToken: ct));
        }
    }

    private sealed record AdminTeamDbRecord(Guid Id, string Name, string? CountryCode, string? Status, string BracketKey, string? JoinCodeProtected, bool IsSuspended);
    private sealed record ProfileDbRecord(Guid Id, string DiscordId, string Username, string? AvatarHash, bool IsAdmin, DateTime CreatedAtUtc, DateTime LastLoginAtUtc, string? TeamIdValue, string? TeamName, decimal Score, long SolveCount);
    private sealed record AdminManagedTeamDbRecord(Guid Id, string Name, string? CountryCode, string BracketKey, string? Status, DateTime CreatedAtUtc, string CaptainUsername, long MemberCount, decimal Score, long SolveCount, string? JoinCodeProtected, bool IsSuspended, string? SuspensionReason, DateTime? SuspendedAtUtc, bool IsBanned, int IsAutoBannedValue, string? SecurityReason, DateTime? BannedAtUtc, bool IsDisbanded, DateTime? DisbandedAtUtc);
    private sealed record TeamExitDbRecord(Guid TeamId, string TeamName, Guid CaptainUserId);
    private sealed record ChallengeScoringDbRow(int Initial, int Minimum, int Decay, int CurrentValue);
    private sealed record ChallengeLockDbRow(Guid Id);
    private sealed class ScoreGraphDbRow
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public string? CountryCode { get; set; }
        public DateTime? SolvedAtUtc { get; set; }
        public decimal? Score { get; set; }
    }
}
