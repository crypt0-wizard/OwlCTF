using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class ChallengeCatalogTests
{
    [Fact]
    public void CategoriesHaveUniqueKeysAndTrustedIcons()
    {
        Assert.Equal(12, ChallengeCategoryCatalog.All.Count);
        Assert.Equal(
            ChallengeCategoryCatalog.All.Count,
            ChallengeCategoryCatalog.All.Select(category => category.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ChallengeCategoryCatalog.All, category => Assert.StartsWith("fa-solid fa-", category.IconClass));
        Assert.Equal(
            ChallengeCategoryCatalog.All.Count,
            ChallengeCategoryCatalog.All.Select(category => category.IconClass).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("reverse-engineering")]
    [InlineData("web")]
    [InlineData("cryptography")]
    [InlineData("pwn")]
    [InlineData("forensics")]
    [InlineData("osint")]
    [InlineData("steganography")]
    [InlineData("mobile")]
    [InlineData("hardware")]
    [InlineData("blockchain")]
    [InlineData("programming")]
    [InlineData("miscellaneous")]
    public void StandardCategoriesAreAvailable(string key) =>
        Assert.True(ChallengeCategoryCatalog.IsValid(key));

    [Fact]
    public void OnlyPredefinedCategoriesAreValid()
    {
        Assert.True(ChallengeCategoryCatalog.IsValid(ChallengeCategoryCatalog.DefaultKey));
        Assert.False(ChallengeCategoryCatalog.IsValid("<script>"));
        Assert.False(ChallengeCategoryCatalog.IsValid(null));
    }

    [Fact]
    public void UnknownCategoriesFallBackToReverseEngineering()
    {
        Assert.Equal(ChallengeCategoryCatalog.DefaultKey, ChallengeCategoryCatalog.Get("unknown").Key);
        Assert.Equal(ChallengeCategoryCatalog.DefaultKey, ChallengeCategoryCatalog.Get(null).Key);
    }

    [Fact]
    public void CustomCategoriesUseTheSharedGenericIcon()
    {
        var categories = ChallengeCategoryCatalog.All.Append(
            new ChallengeCategory("cloud-security", "Cloud Security", ChallengeCategoryCatalog.CustomIconClass, false)).ToArray();

        var category = ChallengeCategoryCatalog.Resolve("cloud-security", categories);
        Assert.Equal("Cloud Security", category.Name);
        Assert.Equal("fa-solid fa-shapes", category.IconClass);
        Assert.False(category.IsBuiltIn);
    }

    [Theory]
    [InlineData("cloud-security", true)]
    [InlineData("web3", true)]
    [InlineData("Cloud", false)]
    [InlineData("two--hyphens", false)]
    [InlineData("<script>", false)]
    [InlineData("", false)]
    public void CustomCategoryKeysAreSafe(string key, bool expected) =>
        Assert.Equal(expected, ChallengeCategoryPolicy.IsValidKey(key));

    [Theory]
    [InlineData("Cloud Security", true)]
    [InlineData("X", false)]
    [InlineData("Line\nBreak", false)]
    public void CustomCategoryNamesRejectUnsafeDisplayText(string name, bool expected) =>
        Assert.Equal(expected, ChallengeCategoryPolicy.IsValidName(name));

    [Theory]
    [InlineData("Cloud Security", "cloud-security")]
    [InlineData("AI / ML", "ai-ml")]
    [InlineData("  Web3 and Smart Contracts  ", "web3-and-smart-contracts")]
    public void CustomCategoryKeysAreGeneratedFromReadableNames(string name, string expected) =>
        Assert.Equal(expected, ChallengeCategoryPolicy.CreateKey(name));

    [Fact]
    public void ChallengeEditorAcceptsExistingOrTypedCategoriesInline()
    {
        var root = FindRepositoryRoot();
        var editor = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Admin", "ChallengeForm.cshtml"));
        var management = File.ReadAllText(Path.Combine(root, "src", "OwlCTF.Web", "Views", "Admin", "Challenges.cshtml"));

        Assert.Contains("<select asp-for=\"CategoryKey\"", editor, StringComparison.Ordinal);
        Assert.Contains("value=\"__custom__\"", editor, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"CustomCategoryName\"", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"challenge-categories\"", management, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsAreNormalizedDeduplicatedAndOrdered()
    {
        Assert.True(ChallengeTagPolicy.TryNormalize(" Beginner, linux, BEGINNER, web-101 ", out var tags, out var error));
        Assert.Null(error);
        Assert.Equal(["beginner", "linux", "web-101"], tags);
    }

    [Theory]
    [InlineData("not allowed")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("two--hyphens")]
    [InlineData("<script>")]
    public void InvalidTagsAreRejected(string value)
    {
        Assert.False(ChallengeTagPolicy.TryNormalize(value, out var tags, out var error));
        Assert.Empty(tags);
        Assert.NotNull(error);
    }

    [Fact]
    public void TagsAreOptionalAndLimited()
    {
        Assert.True(ChallengeTagPolicy.TryNormalize(null, out var empty, out _));
        Assert.Empty(empty);
        Assert.False(ChallengeTagPolicy.TryNormalize("one,two,three,four,five,six,seven,eight,nine", out _, out _));
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
