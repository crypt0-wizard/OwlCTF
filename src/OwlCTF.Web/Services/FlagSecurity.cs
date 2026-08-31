using System.Security.Cryptography;
using System.Text;
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

public static class FlagPrefixPolicy
{
    public const string Default = "CTF";

    public static string Normalize(string? value)
    {
        var normalized = new string((value ?? "").Trim().Where(char.IsLetterOrDigit).Take(16).ToArray()).ToUpperInvariant();
        return normalized.Length >= 2 ? normalized : Default;
    }
}
