namespace OwlCTF.Options;

public sealed class DiscordOptions
{
    public const string Section = "Discord";
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
}
public sealed class StorageOptions
{
    public const string Section = "Storage";
    public string RootPath { get; init; } = "App_Data/challenge-files";
    public string DataProtectionKeysPath { get; init; } = "App_Data/keys";
    public long MaxFileBytes { get; init; } = 52_428_800;
}
public sealed class SecurityOptions
{
    public const string Section = "Security";
    public string FlagPepper { get; init; } = "";
}

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public bool ApplyEfMigrationsOnStartup { get; init; } = true;
}

public sealed class DynamicInstanceOptions
{
    public const string Section = "DynamicInstances";
    public bool Enabled { get; init; }
    public string? DockerEndpoint { get; init; }
    public string PublicHost { get; init; } = "localhost";
    [System.ComponentModel.DataAnnotations.Range(1, 10_000)] public int GlobalConcurrencyLimit { get; init; } = 20;
    [System.ComponentModel.DataAnnotations.Range(5, 86_400)] public int DefaultTtlSeconds { get; init; } = 1_800;
    [System.ComponentModel.DataAnnotations.Range(5, 86_400)] public int RenewalSeconds { get; init; } = 900;
    [System.ComponentModel.DataAnnotations.Range(5, 604_800)] public int MaximumLifetimeSeconds { get; init; } = 7_200;
    [System.ComponentModel.DataAnnotations.Range(5, 3_600)] public int ReaperIntervalSeconds { get; init; } = 30;
    [System.ComponentModel.DataAnnotations.Range(1, 300)] public int StopTimeoutSeconds { get; init; } = 10;
    public string? AdminWebhookUrl { get; init; }
    public bool AutoBanOnCheat { get; init; }
    public bool BlockFlaggedTeams { get; init; } = true;
}
