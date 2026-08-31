using System.Globalization;

namespace OwlCTF.Services;

public sealed record CountryOption(string Code, string Name);

public static class CountryCatalog
{
    public static IReadOnlyList<CountryOption> All { get; } = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new RegionInfo(c.Name); } catch { return null; } })
        .Where(r => r is not null && r.TwoLetterISORegionName.Length == 2)
        .GroupBy(r => r!.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
        .Select(g => new CountryOption(g.Key.ToUpperInvariant(), g.First()!.EnglishName))
        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static bool IsValid(string? code) => !string.IsNullOrWhiteSpace(code) && All.Any(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static string Flag(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 2 || !code.All(char.IsLetter)) return "";
        var upper = code.ToUpperInvariant();
        return char.ConvertFromUtf32(0x1F1E6 + upper[0] - 'A') + char.ConvertFromUtf32(0x1F1E6 + upper[1] - 'A');
    }
}
