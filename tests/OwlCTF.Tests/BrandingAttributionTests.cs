namespace OwlCTF.Tests;

public sealed class BrandingAttributionTests
{
    [Fact]
    public void SharedLayoutKeepsVisibleOwlCtfCredit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var layoutPath = Path.Combine(
            repositoryRoot,
            "src",
            "OwlCTF.Web",
            "Views",
            "Shared",
            "_Layout.cshtml");
        var layout = File.ReadAllText(layoutPath);

        Assert.Contains("Powered by", layout, StringComparison.Ordinal);
        Assert.Contains(
            "https://github.com/crypt0-wizard/OwlCTF",
            layout,
            StringComparison.Ordinal);
        Assert.Contains(">OwlCTF</a>", layout, StringComparison.Ordinal);
        Assert.Contains(@"rel=""icon""", layout, StringComparison.Ordinal);
        Assert.Contains("site-brand-wordmark", layout, StringComparison.Ordinal);
        Assert.Contains("site-brand-wordmark-accent", layout, StringComparison.Ordinal);
        Assert.Contains(">Owl</span><span class=\"site-brand-wordmark-accent\">CTF</span>", layout, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "OwlCTF.Web",
            "wwwroot",
            "images",
            "navbar-logo.png")));
        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "OwlCTF.Web",
            "wwwroot",
            "images",
            "favicon.png")));

        var notice = File.ReadAllText(Path.Combine(repositoryRoot, "NOTICE"));
        Assert.Contains("Powered by", notice, StringComparison.Ordinal);
        Assert.Contains("https://github.com/crypt0-wizard/OwlCTF", notice, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "TRADEMARKS.md")));
    }

    [Fact]
    public void PlatformManagementDoesNotOfferTheDefaultFaviconOverACustomUpload()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "OwlCTF.Web",
            "Views",
            "Admin",
            "Index.cshtml"));

        Assert.Contains("var hasCustomFavicon", view, StringComparison.Ordinal);
        Assert.Contains("!hasCustomFavicon && Model.Settings.FaviconPath != BrandingStorage.DefaultFaviconPath", view, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OwlCTF.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OwlCTF repository root.");
    }
}
