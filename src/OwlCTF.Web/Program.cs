using System.Security.Claims;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using OwlCTF.Data;
using OwlCTF.Hubs;
using OwlCTF.Options;
using OwlCTF.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

var storageSettings = builder.Configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new();
var keyPath = Path.GetFullPath(Path.IsPathRooted(storageSettings.DataProtectionKeysPath)
    ? storageSettings.DataProtectionKeysPath
    : Path.Combine(builder.Environment.ContentRootPath, storageSettings.DataProtectionKeysPath));
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("OwlCTF");

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.Section));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.AddOptions<DynamicInstanceOptions>().Bind(builder.Configuration.GetSection(DynamicInstanceOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    var knownProxy = builder.Configuration["ReverseProxy:KnownProxy"];
    if (!string.IsNullOrWhiteSpace(knownProxy))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Parse(knownProxy));
    }
});
builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<FlagHasher>();
builder.Services.AddSingleton<JoinCodeProtector>();
builder.Services.AddSingleton<FirstBloodWebhookProtector>();
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddSingleton<BrandingStorage>();
builder.Services.AddSingleton<ContentImageStorage>();
builder.Services.AddSingleton<SponsorLogoStorage>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PlatformService>();
builder.Services.AddSingleton<MarkdownService>();
builder.Services.AddSingleton<ScoreboardService>();
builder.Services.AddSingleton<DynamicChallengeScoring>();
var mariaDbConnection = builder.Configuration.GetConnectionString("MariaDb") ?? throw new InvalidOperationException("ConnectionStrings:MariaDb is required.");
builder.Services.AddPooledDbContextFactory<InstanceDbContext>(options => options.UseMySql(mariaDbConnection, new MariaDbServerVersion(new Version(10, 6, 0))));
builder.Services.AddSingleton<EfInstanceStore>();
builder.Services.AddSingleton<IInstanceStore>(provider => provider.GetRequiredService<EfInstanceStore>());
builder.Services.AddSingleton<IExpiredInstanceStore>(provider => provider.GetRequiredService<EfInstanceStore>());
builder.Services.AddSingleton<IFlagOwnershipStore>(provider => provider.GetRequiredService<EfInstanceStore>());
builder.Services.AddSingleton<IContainerRuntime, DockerContainerRuntime>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InstanceLifecycleService>();
builder.Services.AddSingleton<InstanceExpiryProcessor>();
builder.Services.AddSingleton<FlagOwnershipService>();
builder.Services.AddSingleton<ICheatIncidentNotifier, WebhookCheatIncidentNotifier>();
builder.Services.AddHttpClient(nameof(WebhookCheatIncidentNotifier), client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IFirstBloodOutbox>(provider => provider.GetRequiredService<AppDb>());
builder.Services.AddSingleton<IFirstBloodDiscordClient, FirstBloodDiscordClient>();
builder.Services.AddSingleton<FirstBloodAnnouncementProcessor>();
builder.Services.AddHttpClient(nameof(FirstBloodDiscordClient), client => client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddHostedService<EfMigrationInitializer>();
builder.Services.AddHostedService<InstanceExpiryReaper>();
builder.Services.AddHostedService<FirstBloodAnnouncementWorker>();
builder.Services.AddControllersWithViews(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
var signalR = builder.Services.AddSignalR();
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
    signalR.AddStackExchangeRedis(redisConnection);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Discord";
})
.AddCookie(options =>
{
    options.Cookie.Name = "__Host-OwlCTF.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.LoginPath = "/auth/login";
    options.AccessDeniedPath = "/error/403";
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnValidatePrincipal = async context =>
    {
        if (context.Principal?.IsInRole("Admin") != true) return;
        if (!Guid.TryParse(context.Principal.FindFirstValue("owlctf:user_id"), out var userId)) { context.RejectPrincipal(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
        if (!await db.IsAdminAsync(userId, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
})
.AddOAuth("Discord", options =>
{
    var discord = builder.Configuration.GetSection(DiscordOptions.Section).Get<DiscordOptions>() ?? new();
    // OAuth options reject empty values during request-pipeline initialization.
    // Development can still serve public pages before Discord credentials are supplied;
    // AuthController blocks the actual login flow until real values are configured.
    options.ClientId = string.IsNullOrWhiteSpace(discord.ClientId) ? "discord-not-configured" : discord.ClientId;
    options.ClientSecret = string.IsNullOrWhiteSpace(discord.ClientSecret) ? "discord-not-configured" : discord.ClientSecret;
    options.CallbackPath = "/signin-discord";
    options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
    options.TokenEndpoint = "https://discord.com/api/oauth2/token";
    options.UserInformationEndpoint = "https://discord.com/api/users/@me";
    options.Scope.Add("identify");
    options.SaveTokens = false;
    options.Events = new OAuthEvents
    {
        OnCreatingTicket = async context =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
            request.Headers.Authorization = new("Bearer", context.AccessToken);
            using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
            var root = payload.RootElement;
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, root.GetProperty("id").GetString()!));
            identity.AddClaim(new Claim(ClaimTypes.Name, root.GetProperty("username").GetString()!));
            if (root.TryGetProperty("avatar", out var avatar) && avatar.ValueKind == JsonValueKind.String)
                identity.AddClaim(new Claim("urn:discord:avatar", avatar.GetString()!));
        },
        OnTicketReceived = async context =>
        {
            var discordId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = context.Principal?.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(discordId) || string.IsNullOrWhiteSpace(username))
            {
                context.Fail("Discord did not return a valid user identity.");
                return;
            }
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var avatar = context.Principal?.FindFirstValue("urn:discord:avatar");
            var user = await db.UpsertDiscordUserAsync(discordId, username, avatar, context.HttpContext.RequestAborted);
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            identity.AddClaim(new Claim("owlctf:user_id", user.Id.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.Role, user.IsAdmin ? "Admin" : "Player"));
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue("owlctf:user_id") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("submit", context => RateLimitPartition.GetSlidingWindowLimiter(
        context.User.FindFirstValue("owlctf:user_id") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("instances", context => RateLimitPartition.GetSlidingWindowLimiter(
        context.User.FindFirstValue("owlctf:user_id") ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
});

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
}
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
    context.Items["CspNonce"] = nonce;
    context.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; script-src 'self' 'nonce-{nonce}' https://cdn.jsdelivr.net; connect-src 'self'; img-src 'self' https://cdn.discordapp.com https://cdn.jsdelivr.net data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self' https://discord.com";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/health")
        && !context.Request.Path.StartsWithSegments("/hubs")
        && !context.Request.Path.StartsWithSegments("/error"),
    branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=604800" });
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TeamAccessGuardMiddleware>();
app.UseAuthorization();
app.MapHub<ActivityHub>("/hubs/activity");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDb db, CancellationToken ct) => await db.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).AllowAnonymous();
app.Run();

public partial class Program;
