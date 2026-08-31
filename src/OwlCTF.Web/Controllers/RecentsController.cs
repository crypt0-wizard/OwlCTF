using OwlCTF.Data;
using Microsoft.AspNetCore.Mvc;

namespace OwlCTF.Controllers;

public sealed class RecentsController(AppDb db) : Controller
{
    [HttpGet("recents")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await db.GetRecentSolvesAsync(100, ct));
}
