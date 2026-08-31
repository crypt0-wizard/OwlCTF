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
