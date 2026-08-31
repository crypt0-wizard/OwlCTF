using OwlCTF.Data;
using OwlCTF.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OwlCTF.Controllers;

[Authorize, Route("profile")]
public sealed class ProfileController(AppDb db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var profile = await db.GetProfileAsync(User.UserId(), ct);
        return profile is null ? NotFound() : View(profile);
    }
}
