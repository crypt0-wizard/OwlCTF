using System.Text;

namespace OwlCTF.Services;

public sealed record TeamBracket(string Key, string Name);

public static class TeamBracketCatalog
{
    public const string DefaultKey = "open";

    public static IReadOnlyList<TeamBracket> All { get; } =
    [
        new(DefaultKey, "Open"),
        new("high-school", "High School"),
        new("college", "College")
    ];

    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key) && All.Any(bracket => bracket.Key.Equals(key, StringComparison.Ordinal));

    public static TeamBracket Get(string? key) =>
        All.FirstOrDefault(bracket => bracket.Key.Equals(key, StringComparison.Ordinal)) ?? All[0];
}

public static class TeamNamePolicy
{
    public const int MaxLength = 40;
    private const string AllowedPunctuation = "._-[]()#+'&@!";

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            return false;

        var compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        if (compatibilityNormalized.EnumerateRunes().Any(rune =>
                Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator
                    or System.Globalization.UnicodeCategory.ParagraphSeparator))
            return false;

        normalized = string.Join(' ', compatibilityNormalized
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is < 2 or > MaxLength)
            return false;

        var hasLetterOrDigit = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                hasLetterOrDigit = true;
                continue;
            }
            if (rune.Value == ' ' || (rune.IsAscii && AllowedPunctuation.Contains((char)rune.Value)))
                continue;
            return false;
        }
        return hasLetterOrDigit;
    }
}

public static class TeamCapacityPolicy
{
    public const int MinimumMembers = 1;
    public const int MaximumMembers = 100;
    public const int DefaultMaxMembers = 5;

    public static bool IsValidLimit(int limit) => limit is >= MinimumMembers and <= MaximumMembers;

    public static bool HasRoom(long currentMemberCount, int limit) =>
        currentMemberCount >= 0 && IsValidLimit(limit) && currentMemberCount < limit;
}
