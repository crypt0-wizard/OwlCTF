using OwlCTF.Data;
using OwlCTF.Models;
using Microsoft.AspNetCore.Mvc;

namespace OwlCTF.Controllers;

public sealed class DirectoryController(AppDb db) : Controller
{
    [HttpGet("teams/{id:guid}")]
    public async Task<IActionResult> Team(Guid id, CancellationToken ct)
    {
        var team = await db.GetPublicTeamAsync(id, ct);
        if (team is null) return NotFound();
        var membersTask = db.GetPublicTeamMembersAsync(id, ct);
        var solvesTask = db.GetPublicTeamSolvesAsync(id, ct);
        await Task.WhenAll(membersTask, solvesTask);
        return View(new PublicTeamViewModel(team, await membersTask, await solvesTask));
    }

    [HttpGet("members/{id:guid}")]
    public async Task<IActionResult> Member(Guid id, CancellationToken ct)
    {
        var member = await db.GetPublicMemberAsync(id, ct);
        if (member is null) return NotFound();
        return View(new PublicMemberViewModel(member, await db.GetPublicMemberSolvesAsync(id, member.TeamId, ct)));
    }
}
