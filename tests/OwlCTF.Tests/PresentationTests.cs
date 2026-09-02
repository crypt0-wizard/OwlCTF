using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class PresentationTests
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

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=https://evil.example></iframe>")]
    [InlineData("[click](data:text/html,boom)")]
    public void MarkdownRejectsAdditionalUnsafeMarkup(string markdown)
    {
        var html = new MarkdownService().Render(markdown);

        Assert.DoesNotContain("<img ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:text", html, StringComparison.OrdinalIgnoreCase);
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
        var input = new SettingsInput { PlatformName = "OwlCTF", AboutDescription = null, InstructionsDescription = null, ContactDescription = null, SponsorsDescription = null };
        var results = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), results, true));
        var nullability = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.AboutDescription))!).WriteState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.InstructionsDescription))!).WriteState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.ContactDescription))!).WriteState);
        Assert.Equal(NullabilityState.Nullable, nullability.Create(typeof(SettingsInput).GetProperty(nameof(SettingsInput.SponsorsDescription))!).WriteState);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(8146944, "7.8 MiB")]
    [InlineData(1073741824, "1 GiB")]
    public void FileSizesUseTheMostReadableUnit(long bytes, string expected) =>
        Assert.Equal(expected, FileSizeDisplay.Format(bytes));

    [Fact]
    public void NegativeFileSizesAreRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FileSizeDisplay.Format(-1));

    [Fact]
    public void UtcTimesAreSerializedWithAnExplicitUtcOffset()
    {
        var unspecified = new DateTime(2026, 9, 2, 12, 30, 0, DateTimeKind.Unspecified);

        Assert.EndsWith("Z", TimeDisplay.UtcIso(unspecified), StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformCopyDoesNotPlaceACommaBesideAnd()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "OwlCTF.Web");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".cshtml", ".js" };
        var pattern = new Regex(@",\s+and\b|\band\s*,", RegexOptions.CultureInvariant);
        var violations = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, number: index + 1)).Where(item => pattern.IsMatch(item.line)))
            .Select(item => $"{Path.GetRelativePath(sourceRoot, item.path)}:{item.number}: {item.line.Trim()}")
            .ToArray();

        Assert.True(violations.Length == 0, $"Found commas beside 'and':{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OwlCTF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the OwlCTF repository root.");
    }
}
