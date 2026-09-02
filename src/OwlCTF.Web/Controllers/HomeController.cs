using OwlCTF.Models;
using OwlCTF.Services;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OwlCTF.Controllers;

public sealed class HomeController(PlatformService platform, MarkdownService markdown, SponsorLogoStorage sponsorLogos) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        return View(new HomeViewModel(
            settings,
            CtfState.From(settings, DateTime.UtcNow),
            markdown.Render(settings.AboutDescription),
            markdown.Render(settings.InstructionsDescription),
            markdown.Render(settings.ContactDescription),
            sponsorLogos.List()));
    }

    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Route("/team/blocked")]
    public IActionResult TeamBlocked() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Route("/error/{statusCode:int?}")]
    public IActionResult Error(int? statusCode)
    {
        var code = statusCode is >= 400 and <= 599
            ? statusCode.Value
            : StatusCodes.Status500InternalServerError;
        Response.StatusCode = code;

        var (title, message) = code switch
        {
            StatusCodes.Status400BadRequest => ("That request did not look right", "Check what you entered and try again."),
            StatusCodes.Status401Unauthorized => ("Sign in to continue", "You need to sign in before opening this page."),
            StatusCodes.Status403Forbidden => ("You cannot open this page", "Your account does not have access to this area."),
            StatusCodes.Status404NotFound => ("Page not found", "The address may be wrong or the page may have moved."),
            StatusCodes.Status408RequestTimeout => ("The request took too long", "Try again when your connection is stable."),
            StatusCodes.Status429TooManyRequests => ("Too many requests", "Give it a moment then try again."),
            StatusCodes.Status503ServiceUnavailable => ("Temporarily unavailable", "The platform is not ready right now. Please try again shortly."),
            _ => ("Something went wrong", "The server could not finish that request. Please try again shortly.")
        };

        var requestId = code >= 500 ? Activity.Current?.Id ?? HttpContext.TraceIdentifier : null;
        return View(new ErrorPageViewModel(code, title, message, requestId));
    }
}
