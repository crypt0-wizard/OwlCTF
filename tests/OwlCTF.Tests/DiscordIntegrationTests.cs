using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OwlCTF.Models;
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

    [Fact]
    public async Task FirstBloodDeliveryUsesAPlainSingleLineMessageWithoutMentions()
    {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = new FirstBloodDiscordClient(new SingleClientFactory(handler), NullLogger<FirstBloodDiscordClient>.Instance);
        var announcement = new FirstBloodAnnouncement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new string('C', 150), new string('T', 100), new string('U', 120), 500, DateTime.UtcNow, 0);

        var result = await client.SendAsync(ValidWebhook, announcement, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.Body);
        using var json = JsonDocument.Parse(handler.Body);
        var content = json.RootElement.GetProperty("content").GetString()!;
        Assert.DoesNotContain('\n', content);
        Assert.Equal(80, content.Split(" claimed", StringSplitOptions.None)[0].Length);
        Assert.Empty(json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
    }

    [Fact]
    public async Task InvalidWebhookIsRejectedWithoutSendingARequest()
    {
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = new FirstBloodDiscordClient(new SingleClientFactory(handler), NullLogger<FirstBloodDiscordClient>.Instance);

        var result = await client.SendTestAsync("https://example.com/hook", "OwlCTF", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task DiscordHttpFailureReturnsAnActionableError()
    {
        var client = new FirstBloodDiscordClient(
            new SingleClientFactory(new CapturingHandler(HttpStatusCode.BadGateway)),
            NullLogger<FirstBloodDiscordClient>.Instance);

        var result = await client.SendTestAsync(ValidWebhook, "OwlCTF", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("Discord returned HTTP 502.", result.Error);
    }

    private const string ValidWebhook = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz";

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }
}
