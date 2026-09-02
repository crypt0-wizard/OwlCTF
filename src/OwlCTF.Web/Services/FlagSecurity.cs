using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OwlCTF.Options;

namespace OwlCTF.Services;

public sealed class FlagHasher(IOptions<SecurityOptions> options)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.FlagPepper);
    public string Hash(string flag)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(Normalize(flag))));
    }
    public bool Verify(string submitted, string expectedHash)
    {
        if (expectedHash.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(submitted)), Convert.FromHexString(expectedHash)); }
        catch (FormatException) { return false; }
    }
    internal static string Normalize(string value) => value.Trim();
}

public sealed class RegexFlagMatcher
{
    public const int MaximumPatternLength = 500;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    public bool TryValidate(string? pattern, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Enter a regular expression.";
            return false;
        }
        if (pattern.Length > MaximumPatternLength)
        {
            error = $"The regular expression must be {MaximumPatternLength} characters or fewer.";
            return false;
        }
        try
        {
            _ = Build(pattern);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = "Invalid regular expression: " + ex.Message;
            return false;
        }
    }

    public bool Verify(string submitted, string? pattern)
    {
        if (submitted.Length > 500 || !TryValidate(pattern, out _)) return false;
        try { return Build(pattern!).IsMatch(FlagHasher.Normalize(submitted)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static Regex Build(string pattern) =>
        new("\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant, MatchTimeout);
}

public static class FlagPrefixPolicy
{
    public const string Default = "CTF";

    public static string Normalize(string? value)
    {
        var normalized = new string((value ?? "").Trim().Where(char.IsLetterOrDigit).Take(16).ToArray()).ToUpperInvariant();
        return normalized.Length >= 2 ? normalized : Default;
    }
}
