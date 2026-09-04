using Microsoft.Extensions.Caching.Memory;
using OwlCTF.Data;
using OwlCTF.Models;

namespace OwlCTF.Services;

public sealed class PlatformService(AppDb db, IMemoryCache cache)
{
    private const string CacheKey = "platform-settings";
    // Login enforcement must not use cached settings on multi-process deployments.
    public Task<bool> IsLoginEnabledAsync(CancellationToken ct) => db.IsLoginEnabledAsync(ct);

    public async Task UpdateLoginEnabledAsync(bool enabled, CancellationToken ct)
    {
        await db.UpdateLoginEnabledAsync(enabled, ct);
        cache.Remove(CacheKey);
    }
    public async Task<PlatformSettings> GetAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await db.GetSettingsAsync(ct);
        }))!;
    public async Task UpdateAsync(PlatformSettings settings, CancellationToken ct)
    {
        await db.UpdateSettingsAsync(settings, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateNavbarLogoAsync(string? navbarLogoPath, CancellationToken ct)
    {
        await db.UpdateNavbarLogoAsync(navbarLogoPath, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateFaviconAsync(string faviconPath, CancellationToken ct)
    {
        await db.UpdateFaviconAsync(faviconPath, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateHomePageAsync(string platformName, string aboutDescription, string instructionsDescription, string contactDescription, CancellationToken ct)
    {
        await db.UpdateHomePageAsync(platformName, aboutDescription, instructionsDescription, contactDescription, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateEventScheduleAsync(DateTime? startsAtUtc, DateTime? endsAtUtc, CancellationToken ct)
    {
        await db.UpdateEventScheduleAsync(startsAtUtc, endsAtUtc, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateFirstBloodSettingsAsync(bool enabled, string? webhookUrl, CancellationToken ct)
    {
        await db.UpdateFirstBloodSettingsAsync(enabled, webhookUrl, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateFlagPrefixAsync(string flagPrefix, CancellationToken ct)
    {
        await db.UpdateFlagPrefixAsync(flagPrefix, ct);
        cache.Remove(CacheKey);
    }
    public async Task UpdateTeamCapacityAsync(int maxTeamMembers, CancellationToken ct)
    {
        await db.UpdateTeamCapacityAsync(maxTeamMembers, ct);
        cache.Remove(CacheKey);
    }
}
