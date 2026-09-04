using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using OwlCTF.Controllers;
using OwlCTF.Models;
using OwlCTF.Options;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class DiscordIntegrationTests
{
    [Fact]
    public void LoginIsEnabledByDefaultAndPausedPageDoesNotRedirectBackToLogin()
    {
        var settings = new PlatformSettings("CTF", "", "", "", null, null);
        Assert.True(settings.LoginEnabled);
        Assert.False((settings with { LoginEnabled = false }).LoginEnabled);
        Assert.IsType<ViewResult>(CreateAuthController().LoginDisabled());
    }

    [Fact]
    public void LoginToggleRequiresAdminAndForgeryProtection()
    {
        var authorize = Assert.Single(typeof(AdminController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("Admin", authorize.Roles);
        var action = typeof(AdminController).GetMethod(nameof(AdminController.SaveLoginSettings))!;
        Assert.NotEmpty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.NotEmpty(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }

    [Fact]
    public void LoginGateCoversOAuthRedirectAndCallbackBeforeCreatingUsers()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OwlCTF.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = Path.Combine(directory.FullName, "src", "OwlCTF.Web");
        var program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        var redirect = program.Split("OnRedirectToAuthorizationEndpoint =")[1].Split("OnRemoteFailure =")[0];
        Assert.Contains("IsLoginEnabledAsync", redirect);
        Assert.Contains("/auth/login-disabled", redirect);
        var callback = program.Split("OnTicketReceived =")[1].Split("UpsertDiscordUserAsync")[0];
        Assert.Contains("IsLoginEnabledAsync", callback);
        Assert.Contains("context.HandleResponse()", callback);
        Assert.Contains("/auth/login-disabled", callback);
        var auth = File.ReadAllText(Path.Combine(root, "Controllers", "AuthController.cs"));
        Assert.True(auth.IndexOf("IsLoginEnabledAsync", StringComparison.Ordinal) < auth.IndexOf("return Challenge(", StringComparison.Ordinal));
        var data = File.ReadAllText(Path.Combine(root, "Data", "AppDb.cs"));
        Assert.Contains("ADD COLUMN IF NOT EXISTS LoginEnabled BOOLEAN NOT NULL DEFAULT TRUE", data);
    }

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

    [Theory]
    [InlineData(true, "Discord sign-in was cancelled. Nothing changed.")]
    [InlineData(false, "Discord sign-in could not be completed. Please try again.")]
    public void DiscordLoginFailureReturnsHomeWithFriendlyFeedback(bool cancelled, string expected)
    {
        var controller = CreateAuthController();

        var result = Assert.IsType<RedirectToActionResult>(
            cancelled ? controller.Cancelled() : controller.DiscordFailed());

        Assert.Equal("Index", result.ActionName);
        Assert.Equal("Home", result.ControllerName);
        Assert.Equal(expected, controller.TempData["Error"]);
    }

    [Fact]
    public void OAuthRemoteFailuresAreExplicitlyHandledInsteadOfReachingTheErrorPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OwlCTF.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var program = File.ReadAllText(Path.Combine(directory!.FullName, "src", "OwlCTF.Web", "Program.cs"));

        Assert.Contains("OnRemoteFailure", program, StringComparison.Ordinal);
        Assert.Contains("context.HandleResponse()", program, StringComparison.Ordinal);
        Assert.Contains("access_denied", program, StringComparison.Ordinal);
        Assert.Contains("/auth/cancelled", program, StringComparison.Ordinal);
    }

    private static AuthController CreateAuthController()
    {
        var http = new DefaultHttpContext();
        return new AuthController(Microsoft.Extensions.Options.Options.Create(new DiscordOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempDataProvider())
        };
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

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
