using System.Net.Http.Json;
using OwlCTF.Models;

namespace OwlCTF.Services;

public interface IFirstBloodOutbox
{
    Task<IReadOnlyList<FirstBloodAnnouncement>> GetDueFirstBloodAnnouncementsAsync(int limit, CancellationToken ct);
    Task MarkFirstBloodSentAsync(Guid id, CancellationToken ct);
    Task MarkFirstBloodFailedAsync(Guid id, DateTime nextAttemptAtUtc, string error, CancellationToken ct);
}

public sealed record WebhookDeliveryResult(bool Succeeded, string? Error = null);

public interface IFirstBloodDiscordClient
{
    Task<WebhookDeliveryResult> SendAsync(string webhookUrl, FirstBloodAnnouncement announcement, CancellationToken ct);
    Task<WebhookDeliveryResult> SendTestAsync(string webhookUrl, string platformName, CancellationToken ct);
}

public static class DiscordWebhookAddress
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    { "discord.com", "ptb.discord.com", "canary.discord.com", "discordapp.com" };

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var webhookIndex = parts.Length > 1 && parts[0].Equals("api", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        if (webhookIndex >= 0 && parts.Length > webhookIndex && parts[webhookIndex].StartsWith('v') &&
            int.TryParse(parts[webhookIndex].AsSpan(1), out _)) webhookIndex++;
        if (webhookIndex < 0 || parts.Length != webhookIndex + 3 ||
            !parts[webhookIndex].Equals("webhooks", StringComparison.OrdinalIgnoreCase) ||
            !ulong.TryParse(parts[webhookIndex + 1], out _) || parts[webhookIndex + 2].Length < 20) return false;

        normalized = uri.GetLeftPart(UriPartial.Path);
        return true;
    }
}

public static class FirstBloodPolicy
{
    public static bool IsFirstEligibleSolve(long eligiblePriorSolveCount) => eligiblePriorSolveCount == 0;

    public static DateTime NextAttemptAtUtc(DateTime nowUtc, int completedAttempts)
    {
        var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(Math.Max(0, completedAttempts) + 1, 8)));
        return DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).AddSeconds(delaySeconds);
    }
}

public sealed class FirstBloodDiscordClient(IHttpClientFactory clients, ILogger<FirstBloodDiscordClient> logger) : IFirstBloodDiscordClient
{
    public Task<WebhookDeliveryResult> SendAsync(string webhookUrl, FirstBloodAnnouncement announcement, CancellationToken ct) =>
        PostAsync(webhookUrl, new
        {
            username = "OwlCTF First Blood",
            allowed_mentions = new { parse = Array.Empty<string>() },
            content = $"{Limit(announcement.TeamName, 80)} claimed first blood on {Limit(announcement.ChallengeTitle, 120)} for {announcement.PointsAwarded.ToString(System.Globalization.CultureInfo.InvariantCulture)} points, solved by {Limit(announcement.Username, 100)}."
        }, ct);

    public Task<WebhookDeliveryResult> SendTestAsync(string webhookUrl, string platformName, CancellationToken ct) =>
        PostAsync(webhookUrl, new
        {
            username = "OwlCTF First Blood",
            allowed_mentions = new { parse = Array.Empty<string>() },
            content = $"First-blood webhook connected for {Limit(platformName, 80)}."
        }, ct);

    private async Task<WebhookDeliveryResult> PostAsync(string webhookUrl, object payload, CancellationToken ct)
    {
        if (!DiscordWebhookAddress.TryNormalize(webhookUrl, out var normalized)) return new(false, "The saved Discord webhook URL is invalid.");
        try
        {
            using var response = await clients.CreateClient(nameof(FirstBloodDiscordClient)).PostAsJsonAsync(normalized, payload, ct);
            if (response.IsSuccessStatusCode) return new(true);
            var error = $"Discord returned HTTP {(int)response.StatusCode}.";
            logger.LogWarning("First-blood webhook returned HTTP {StatusCode}", response.StatusCode);
            return new(false, error);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "Discord webhook request timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "First-blood webhook request failed");
            return new(false, "Discord webhook request failed.");
        }
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}

public sealed class FirstBloodAnnouncementProcessor(IFirstBloodOutbox outbox, PlatformService platform, IFirstBloodDiscordClient discord, TimeProvider clock)
{
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!settings.FirstBloodEnabled || !DiscordWebhookAddress.TryNormalize(settings.FirstBloodWebhookUrl, out var webhookUrl)) return 0;
        var announcements = await outbox.GetDueFirstBloodAnnouncementsAsync(10, ct);
        foreach (var announcement in announcements)
        {
            var result = await discord.SendAsync(webhookUrl, announcement, ct);
            if (result.Succeeded) await outbox.MarkFirstBloodSentAsync(announcement.Id, ct);
            else
            {
                await outbox.MarkFirstBloodFailedAsync(announcement.Id,
                    FirstBloodPolicy.NextAttemptAtUtc(clock.GetUtcNow().UtcDateTime, announcement.AttemptCount),
                    result.Error ?? "Discord delivery failed.", ct);
            }
        }
        return announcements.Count;
    }
}

public sealed class FirstBloodAnnouncementWorker(FirstBloodAnnouncementProcessor processor, ILogger<FirstBloodAnnouncementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(2);
            try
            {
                if (await processor.ProcessBatchAsync(stoppingToken) > 0) delay = TimeSpan.FromMilliseconds(250);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "First-blood announcement processing failed");
                delay = TimeSpan.FromSeconds(10);
            }
            await Task.Delay(delay, stoppingToken);
        }
    }
}
