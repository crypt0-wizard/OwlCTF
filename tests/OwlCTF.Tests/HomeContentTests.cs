using System.ComponentModel.DataAnnotations;
using System.Reflection;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class HomeContentTests
{
    [Fact]
    public void MarkdownIsRenderedAndUnsafeHtmlIsRemoved()
    {
        var html = new MarkdownService().Render("## Hello\n\n**Bold** <script>alert(1)</script> [bad](javascript:alert(1))");

        Assert.Contains("<h2", html);
        Assert.Contains("<strong>Bold</strong>", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeLocalMarkdownImagesArePreserved()
    {
        var html = new MarkdownService().Render("![Sponsor logo](/uploads/content/content-test.png)");

        Assert.Contains("<img", html);
        Assert.Contains("/uploads/content/content-test.png", html);
        Assert.Contains("Sponsor logo", html);
    }

    [Fact]
    public void OptionalHomePageSectionsCanBeEmpty()
    {
        var input = new SettingsInput
        {
            PlatformName = "OwlCTF",
            AboutDescription = null,
            InstructionsDescription = null,
            ContactDescription = null,
            SponsorsDescription = null
        };
        var results = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), results, true));

        var nullability = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.AboutDescription))!).WriteState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.InstructionsDescription))!).WriteState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.ContactDescription))!).WriteState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.SponsorsDescription))!).WriteState);
    }
}
