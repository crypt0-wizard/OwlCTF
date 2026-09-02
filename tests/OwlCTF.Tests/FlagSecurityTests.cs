using OwlCTF.Options;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class FlagSecurityTests
{
    [Fact]
    public void CorrectFlagIsAccepted()
    {
        var hasher = CreateHasher();

        Assert.True(hasher.Verify("CTF{correct}", hasher.Hash("CTF{correct}")));
    }

    [Fact]
    public void IncorrectFlagIsRejected()
    {
        var hasher = CreateHasher();

        Assert.False(hasher.Verify("CTF{wrong}", hasher.Hash("CTF{correct}")));
    }

    [Fact]
    public void SurroundingWhitespaceIsIgnored()
    {
        var hasher = CreateHasher();

        Assert.True(hasher.Verify("  CTF{correct}\r\n", hasher.Hash("CTF{correct}")));
    }

    [Fact]
    public void DifferentPeppersProduceDifferentHashes() =>
        Assert.NotEqual(
            CreateHasher("pepper-one-that-is-long-enough-12345").Hash("same"),
            CreateHasher("pepper-two-that-is-long-enough-12345").Hash("same"));

    [Fact]
    public void MalformedStoredHashIsRejected() =>
        Assert.False(CreateHasher().Verify("anything", "not-a-hash"));

    [Fact]
    public void RegexFlagsSupportVariableValuesAndRequireAFullMatch()
    {
        var matcher = new RegexFlagMatcher();
        const string pattern = @"CTF\{user-[a-f0-9]{8}\}";

        Assert.True(matcher.Verify(" CTF{user-deadbeef} ", pattern));
        Assert.False(matcher.Verify("prefix-CTF{user-deadbeef}", pattern));
        Assert.False(matcher.Verify("CTF{USER-deadbeef}", pattern));
    }

    [Theory]
    [InlineData("")]
    [InlineData("(")]
    [InlineData("[unterminated")]
    public void InvalidRegexFlagsAreRejected(string pattern)
    {
        var matcher = new RegexFlagMatcher();

        Assert.False(matcher.TryValidate(pattern, out var error));
        Assert.NotNull(error);
        Assert.False(matcher.Verify("CTF{anything}", pattern));
    }

    [Fact]
    public void OversizedRegexFlagsAreRejected()
    {
        var matcher = new RegexFlagMatcher();
        var pattern = new string('a', RegexFlagMatcher.MaximumPatternLength + 1);

        Assert.False(matcher.TryValidate(pattern, out _));
    }

    [Fact]
    public void RegexMatchingFailsClosedForOversizedAndPathologicalInput()
    {
        var matcher = new RegexFlagMatcher();

        Assert.False(matcher.Verify(new string('a', 501), "a+"));
        Assert.False(matcher.Verify(new string('a', 499) + "!", "(a+)+"));
    }

    [Theory]
    [InlineData("ctf", "CTF")]
    [InlineData("  MyEvent42  ", "MYEVENT42")]
    [InlineData("A!", "CTF")]
    [InlineData("ReallyLongCompetitionName", "REALLYLONGCOMPET")]
    public void FlagPrefixIsNormalizedToASafeValue(string input, string expected) =>
        Assert.Equal(expected, FlagPrefixPolicy.Normalize(input));

    private static FlagHasher CreateHasher(string pepper = "this-is-a-test-pepper-with-32-characters") =>
        new(Microsoft.Extensions.Options.Options.Create(new SecurityOptions { FlagPepper = pepper }));
}
