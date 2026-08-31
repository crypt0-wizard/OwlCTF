using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.DataProtection;
using OwlCTF.Models;
using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class TeamRulesTests
{
    [Theory]
    [InlineData("[T-SHP] VzDX", "[T-SHP] VzDX")]
    [InlineData("  Null   Script  ", "Null Script")]
    [InlineData("Hackers & Friends!", "Hackers & Friends!")]
    [InlineData("0xC0FFEE_CTF", "0xC0FFEE_CTF")]
    public void TeamNamesAreNormalized(string input, string expected)
    {
        Assert.True(TeamNamePolicy.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("＜script＞")]
    [InlineData("Team/name")]
    [InlineData("Team\nName")]
    [InlineData("Safe\u202EName")]
    [InlineData("---")]
    public void UnsafeOrMeaninglessTeamNamesAreRejected(string input) =>
        Assert.False(TeamNamePolicy.TryNormalize(input, out _));

    [Fact]
    public void TeamNamesCannotExceedTheMaximumLength() =>
        Assert.False(TeamNamePolicy.TryNormalize(new string('a', TeamNamePolicy.MaxLength + 1), out _));

    [Fact]
    public void AllowedHtmlCharactersAreEncodedWhenDisplayed()
    {
        Assert.True(TeamNamePolicy.TryNormalize("Red & Blue", out var normalized));
        Assert.Equal("Red &amp; Blue", HtmlEncoder.Default.Encode(normalized));
    }

    [Fact]
    public void TeamCreationRequiresAnExplicitBracket()
    {
        var input = new TeamInput { Name = "Valid Team", CountryCode = "PK", BracketKey = "" };
        var results = Validate(input);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(TeamInput.BracketKey)));
    }

    [Fact]
    public void BracketCatalogContainsTheSupportedBrackets() =>
        Assert.Equal(["Open", "High School", "College"], TeamBracketCatalog.All.Select(bracket => bracket.Name));

    [Theory]
    [InlineData("open")]
    [InlineData("high-school")]
    [InlineData("college")]
    public void SupportedBracketsAreValid(string key) =>
        Assert.True(TeamBracketCatalog.IsValid(key));

    [Fact]
    public void UnknownBracketsAreRejectedAndFallBackToOpen()
    {
        Assert.False(TeamBracketCatalog.IsValid("professional"));
        Assert.Equal(TeamBracketCatalog.DefaultKey, TeamBracketCatalog.Get("professional").Key);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void SupportedTeamMemberLimitsAreValid(int limit) =>
        Assert.True(TeamCapacityPolicy.IsValidLimit(limit));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void UnsupportedTeamMemberLimitsAreRejected(int limit) =>
        Assert.False(TeamCapacityPolicy.IsValidLimit(limit));

    [Fact]
    public void TeamsHaveRoomUntilTheyReachTheMemberLimit()
    {
        Assert.True(TeamCapacityPolicy.HasRoom(4, 5));
        Assert.False(TeamCapacityPolicy.HasRoom(5, 5));
        Assert.False(TeamCapacityPolicy.HasRoom(6, 5));
    }

    [Fact]
    public void SoloTeamLimitAllowsTheCaptainOnly()
    {
        Assert.True(TeamCapacityPolicy.HasRoom(0, 1));
        Assert.False(TeamCapacityPolicy.HasRoom(1, 1));
    }

    [Fact]
    public void TeamCapacityInputEnforcesTheSupportedRange()
    {
        var input = new TeamCapacityInput { MaxTeamMembers = TeamCapacityPolicy.MaximumMembers + 1 };

        Assert.Contains(Validate(input), result => result.MemberNames.Contains(nameof(TeamCapacityInput.MaxTeamMembers)));
    }

    [Fact]
    public void JoinCodesAreEncryptedAndCanBeRecovered()
    {
        var protector = new JoinCodeProtector(new EphemeralDataProtectionProvider());
        var encrypted = protector.Protect("ABCDEF0123456789");

        Assert.DoesNotContain("ABCDEF0123456789", encrypted);
        Assert.Equal("ABCDEF0123456789", protector.Unprotect(encrypted));
    }

    [Fact]
    public void TamperedJoinCodesAreRejected()
    {
        var protector = new JoinCodeProtector(new EphemeralDataProtectionProvider());
        var encrypted = protector.Protect("ABCDEF0123456789");

        Assert.Null(protector.Unprotect(encrypted + "tampered"));
    }

    [Fact]
    public void CountryCatalogRejectsArbitraryInput()
    {
        Assert.True(CountryCatalog.IsValid("PK"));
        Assert.False(CountryCatalog.IsValid("<script>"));
    }

    [Fact]
    public void TeamStatusCannotExceedFiftyCharacters()
    {
        var input = new TeamSettingsInput
        {
            CountryCode = "PK",
            BracketKey = TeamBracketCatalog.DefaultKey,
            Status = new string('x', 51)
        };

        Assert.NotEmpty(Validate(input));
    }

    [Fact]
    public void SuspensionReasonCannotExceedFiveHundredCharacters()
    {
        var input = new TeamSuspensionInput { Suspended = true, Reason = new string('x', 501) };

        Assert.NotEmpty(Validate(input));
    }

    private static List<ValidationResult> Validate(object input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, true);
        return results;
    }
}
