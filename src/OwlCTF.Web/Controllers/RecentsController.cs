using Microsoft.AspNetCore.Mvc;
using OwlCTF.Data;

namespace OwlCTF.Controllers;

public sealed class RecentsController(AppDb db) : Controller
{
    [HttpGet("recents")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await db.GetRecentSolvesAsync(100, ct));
}
