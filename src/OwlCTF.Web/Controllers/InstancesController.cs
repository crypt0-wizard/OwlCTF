using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Controllers;

[ApiController, Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme), EnableRateLimiting("instances"), Route("api/instances")]
public sealed class InstancesController(AppDb appDb, InstanceLifecycleService instances, PlatformService platform) : ControllerBase
{
    [HttpPost("{challengeId:guid}/start")] public Task<IActionResult> Start(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.StartAsync, requireLiveEvent: true, requireVisibleChallenge: true, allowSuspendedTeam: false, requireUnsolvedChallenge: true, ct);
    [HttpPost("{challengeId:guid}/stop")] public Task<IActionResult> Stop(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.StopAsync, requireLiveEvent: false, requireVisibleChallenge: false, allowSuspendedTeam: true, requireUnsolvedChallenge: false, ct);
    [HttpPost("{challengeId:guid}/renew")] public Task<IActionResult> Renew(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.RenewAsync, requireLiveEvent: true, requireVisibleChallenge: true, allowSuspendedTeam: false, requireUnsolvedChallenge: true, ct);
    [HttpGet("{challengeId:guid}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] public Task<IActionResult> Get(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.GetAsync, requireLiveEvent: false, requireVisibleChallenge: false, allowSuspendedTeam: true, requireUnsolvedChallenge: false, ct);

    private async Task<IActionResult> Execute(
        Guid challengeId,
        Func<Guid, Guid, CancellationToken, Task<InstanceView>> action,
        bool requireLiveEvent,
        bool requireVisibleChallenge,
        bool allowSuspendedTeam,
        bool requireUnsolvedChallenge,
        CancellationToken ct)
    {
        var team = await appDb.GetTeamForUserAsync(User.UserId(), ct);
        if (team is null) return Conflict(new { error = "team_required", message = "Create or join a team first." });
        if (team.IsSuspended && !allowSuspendedTeam) return StatusCode(403, new { error = "team_suspended", message = "Your team is suspended." });
        if (requireVisibleChallenge)
        {
            var challenge = await appDb.GetChallengeAsync(challengeId, team.Id, includeHidden: false, ct);
            if (challenge is null)
                return NotFound(new { error = "challenge_not_found", message = "This challenge is not available." });
            if (requireUnsolvedChallenge && challenge.IsSolved)
                return Conflict(new { error = "challenge_already_solved", message = "Your team has already solved this challenge." });
        }
        if (requireLiveEvent)
        {
            var state = CtfState.From(await platform.GetAsync(ct), DateTime.UtcNow);
            if (state.Phase != CtfPhase.Live)
                return Conflict(new { error = "event_not_live", message = "Challenge instances can only be started or renewed while the CTF is live." });
        }
        try { return Ok(await action(team.Id, challengeId, ct)); }
        catch (InstanceOperationException ex) { return StatusCode(ex.StatusCode, new { error = "instance_operation_failed", message = ex.Message }); }
    }
}
