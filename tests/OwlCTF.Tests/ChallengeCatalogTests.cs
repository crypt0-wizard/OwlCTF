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
}
