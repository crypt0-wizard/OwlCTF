using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OwlCTF.Options;
using Microsoft.Extensions.Options;

namespace OwlCTF.Controllers;

[Route("auth")]
public sealed class AuthController(IOptions<DiscordOptions> discord) : Controller
{
    [HttpGet("login"), AllowAnonymous, EnableRateLimiting("login")]
    public IActionResult Login(string? returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(discord.Value.ClientId) || string.IsNullOrWhiteSpace(discord.Value.ClientSecret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Discord login is not configured. Set Discord:ClientId and Discord:ClientSecret, then restart the application.");
        if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/";
        return Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, "Discord");
    }

    [HttpGet("cancelled"), AllowAnonymous]
    public IActionResult Cancelled()
    {
        TempData["Error"] = "Discord sign-in was cancelled. Nothing changed.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("discord-failed"), AllowAnonymous]
    public IActionResult DiscordFailed()
    {
        TempData["Error"] = "Discord sign-in could not be completed. Please try again.";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
    [HttpGet("denied")]
    public IActionResult Denied() => Redirect("/error/403");
}
