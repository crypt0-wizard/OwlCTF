using System.Text.RegularExpressions;

namespace OwlCTF.Tests;

public sealed class ContentCopyTests
{
    [Fact]
    public void PlatformCopyDoesNotPlaceACommaBesideAnd()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "OwlCTF.Web");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".cshtml", ".js" };
        var pattern = new Regex(@",\s+and\b|\band\s*,", RegexOptions.CultureInvariant);

        var violations = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, line, number: index + 1))
                .Where(item => pattern.IsMatch(item.line)))
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
