using OwlCTF.Data;
using OwlCTF.Extensions;
using OwlCTF.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace OwlCTF.Controllers;

[ApiController, Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme), IgnoreAntiforgeryToken, EnableRateLimiting("instances"), Route("api/instances")]
public sealed class InstancesController(AppDb appDb, InstanceLifecycleService instances) : ControllerBase
{
    [HttpPost("{challengeId:guid}/start")] public Task<IActionResult> Start(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.StartAsync, ct);
    [HttpPost("{challengeId:guid}/stop")] public Task<IActionResult> Stop(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.StopAsync, ct);
    [HttpPost("{challengeId:guid}/renew")] public Task<IActionResult> Renew(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.RenewAsync, ct);
    [HttpGet("{challengeId:guid}")] public Task<IActionResult> Get(Guid challengeId, CancellationToken ct) => Execute(challengeId, instances.GetAsync, ct);
    private async Task<IActionResult> Execute(Guid challengeId, Func<Guid, Guid, CancellationToken, Task<InstanceView>> action, CancellationToken ct)
    {
        var team = await appDb.GetTeamForUserAsync(User.UserId(), ct);
        if (team is null) return Conflict(new { error = "team_required", message = "Create or join a team first." });
        if (team.IsSuspended) return StatusCode(403, new { error = "team_suspended", message = "Your team is suspended." });
        try { return Ok(await action(team.Id, challengeId, ct)); }
        catch (InstanceOperationException ex) { return StatusCode(ex.StatusCode, new { error = "instance_operation_failed", message = ex.Message }); }
    }
}
