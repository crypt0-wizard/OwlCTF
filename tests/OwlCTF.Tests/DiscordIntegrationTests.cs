using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class DiscordIntegrationTests
{
    [Fact]
    public void ValidDiscordIdentityUsesTheAvatarCdn()
    {
        var url = DiscordAvatar.Url("123456789012345678", "a_abcdef123456", 256);

        Assert.Equal("https://cdn.discordapp.com/avatars/123456789012345678/a_abcdef123456.png?size=256", url);
    }

    [Fact]
    public void UnsafeDiscordIdentityFallsBackWithoutExposingInput()
    {
        var url = DiscordAvatar.Url("../outside", "bad/hash", 999);

        Assert.StartsWith("https://cdn.discordapp.com/embed/avatars/", url);
        Assert.DoesNotContain("outside", url);
        Assert.DoesNotContain("bad", url);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("https://discord.com/api/v10/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("https://canary.discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("http://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz", false)]
    [InlineData("https://discord.com.evil.example/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz", false)]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz?wait=true", false)]
    [InlineData("https://example.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz", false)]
    public void WebhookValidationOnlyAcceptsDiscordEndpoints(string value, bool expected) =>
        Assert.Equal(expected, DiscordWebhookAddress.TryNormalize(value, out _));

    [Fact]
    public void OnlyTheFirstEligibleSolveQueuesAFirstBloodAnnouncement()
    {
        Assert.True(FirstBloodPolicy.IsFirstEligibleSolve(0));
        Assert.False(FirstBloodPolicy.IsFirstEligibleSolve(1));
        Assert.False(FirstBloodPolicy.IsFirstEligibleSolve(100));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(7, 256)]
    [InlineData(100, 256)]
    public void FirstBloodRetryDelayIsExponentialAndCapped(int completedAttempts, int expectedSeconds)
    {
        var now = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddSeconds(expectedSeconds), FirstBloodPolicy.NextAttemptAtUtc(now, completedAttempts));
    }
}
