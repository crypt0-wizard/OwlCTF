using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OwlCTF.Data;
using OwlCTF.Options;

namespace OwlCTF.Services;

public sealed record TeamAccessDecision(bool Blocked, bool Suspended, string? Reason);
public sealed class TeamAccessGuardMiddleware(RequestDelegate next)
{
    public static string CacheKey(Guid userId) => "team-access:" + userId.ToString("N");

    public async Task InvokeAsync(HttpContext context, IDbContextFactory<InstanceDbContext> factory, IMemoryCache cache, IOptions<DynamicInstanceOptions> configured)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || context.User.IsInRole("Admin")
            || context.Request.Path.StartsWithSegments("/auth")
            || context.Request.Path.StartsWithSegments("/error")
            || context.Request.Path.StartsWithSegments("/team/blocked"))
        {
            await next(context);
            return;
        }
        if (!Guid.TryParse(context.User.FindFirstValue("owlctf:user_id"), out var userId)) { await next(context); return; }
        var decision = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
            var teamId = await db.TeamMemberships.Where(x => x.UserId == userId).Select(x => (Guid?)x.TeamId).SingleOrDefaultAsync(context.RequestAborted);
            if (teamId is null) return new TeamAccessDecision(false, false, null);
            return await db.TeamSecurityStates.Where(x => x.Id == teamId).Select(x => new TeamAccessDecision(
                x.IsSuspended || x.IsBanned || (configured.Value.BlockFlaggedTeams && x.IsFlagged),
                x.IsSuspended,
                x.IsSuspended ? x.SuspensionReason : x.SecurityReason)).SingleAsync(context.RequestAborted);
        }) ?? new(false, false, null);
        if (!decision.Blocked) { await next(context); return; }
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = decision.Suspended ? "team_suspended" : "team_blocked",
                message = decision.Reason ?? (decision.Suspended ? "Your team is suspended." : "Your team is blocked from platform access.")
            }, context.RequestAborted);
        }
        else context.Response.Redirect("/team/blocked");
    }
}
