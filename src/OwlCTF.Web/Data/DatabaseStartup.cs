using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OwlCTF.Options;

namespace OwlCTF.Data;

public sealed class DatabaseInitializer(AppDb db, IHostEnvironment environment, IOptions<SecurityOptions> security, IOptions<DiscordOptions> discord) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsProduction())
        {
            if (security.Value.FlagPepper.Length < 32 || security.Value.FlagPepper.StartsWith("CHANGE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Security:FlagPepper must be a secret of at least 32 characters.");
            if (string.IsNullOrWhiteSpace(discord.Value.ClientId) || string.IsNullOrWhiteSpace(discord.Value.ClientSecret))
                throw new InvalidOperationException("Discord OAuth credentials are required.");
        }
        await db.InitializeAsync(cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class EfMigrationInitializer(IDbContextFactory<InstanceDbContext> factory, IOptions<DatabaseOptions> options, ILogger<EfMigrationInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyEfMigrationsOnStartup)
        {
            logger.LogInformation("Automatic EF Core migrations are disabled; the database must be upgraded before startup.");
            return;
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
        {
            logger.LogDebug("The EF Core schema is already up to date.");
            return;
        }
        logger.LogInformation("Applying {MigrationCount} pending EF Core migration(s).", pending.Length);
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
