using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OwlCTF.Controllers;
using OwlCTF.Models;

namespace OwlCTF.Tests;

public sealed class ErrorPageTests
{
    [Theory]
    [InlineData(400, "That request did not look right")]
    [InlineData(401, "Sign in to continue")]
    [InlineData(403, "You cannot open this page")]
    [InlineData(404, "Page not found")]
    [InlineData(429, "Too many requests")]
    [InlineData(503, "Temporarily unavailable")]
    public void KnownStatusGetsFriendlyCopyAndKeepsItsStatusCode(int statusCode, string title)
    {
        var controller = CreateController();

        var result = Assert.IsType<ViewResult>(controller.Error(statusCode));
        var model = Assert.IsType<ErrorPageViewModel>(result.Model);

        Assert.Equal(statusCode, controller.Response.StatusCode);
        Assert.Equal(statusCode, model.StatusCode);
        Assert.Equal(title, model.Title);
        Assert.False(string.IsNullOrWhiteSpace(model.Message));
    }

    [Fact]
    public void MissingStatusFallsBackToSafeServerError()
    {
        var controller = CreateController();

        var result = Assert.IsType<ViewResult>(controller.Error(null));
        var model = Assert.IsType<ErrorPageViewModel>(result.Model);

        Assert.Equal(StatusCodes.Status500InternalServerError, controller.Response.StatusCode);
        Assert.Equal("Something went wrong", model.Title);
        Assert.False(string.IsNullOrWhiteSpace(model.RequestId));
    }

    private static HomeController CreateController() => new(null!, null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
}
