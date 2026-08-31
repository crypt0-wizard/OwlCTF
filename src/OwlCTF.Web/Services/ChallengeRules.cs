namespace OwlCTF.Services;

public sealed record ChallengeCategory(string Key, string Name, string IconClass);

public static class ChallengeCategoryCatalog
{
    public const string DefaultKey = "reverse-engineering";

    public static IReadOnlyList<ChallengeCategory> All { get; } =
    [
        new(DefaultKey, "Reverse Engineering", "fa-solid fa-microchip"),
        new("web", "Web Exploitation", "fa-solid fa-globe"),
        new("cryptography", "Cryptography", "fa-solid fa-key"),
        new("pwn", "Binary Exploitation (Pwn)", "fa-solid fa-bug"),
        new("forensics", "Forensics", "fa-solid fa-magnifying-glass"),
        new("osint", "OSINT", "fa-solid fa-binoculars"),
        new("steganography", "Steganography", "fa-solid fa-image"),
        new("mobile", "Mobile", "fa-solid fa-mobile-screen-button"),
        new("hardware", "Hardware / IoT", "fa-solid fa-memory"),
        new("blockchain", "Blockchain / Web3", "fa-solid fa-link"),
        new("programming", "Programming", "fa-solid fa-code"),
        new("miscellaneous", "Miscellaneous", "fa-solid fa-puzzle-piece")
    ];

    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key) && All.Any(category => category.Key.Equals(key, StringComparison.Ordinal));

    public static ChallengeCategory Get(string? key) =>
        All.FirstOrDefault(category => category.Key.Equals(key, StringComparison.Ordinal)) ?? All[0];
}

public static class ChallengeTagPolicy
{
    public const int MaximumTags = 8;
    public const int MaximumTagLength = 24;

    public static bool TryNormalize(string? value, out IReadOnlyList<string> tags, out string? error)
    {
        tags = Array.Empty<string>();
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = part.ToLowerInvariant();
            if (tag.Length > MaximumTagLength)
            {
                error = $"Each tag must be {MaximumTagLength} characters or fewer.";
                return false;
            }
            if (!IsValid(tag))
            {
                error = "Use lowercase letters, numbers and single hyphens in tags.";
                return false;
            }
            if (seen.Add(tag)) normalized.Add(tag);
        }

        if (normalized.Count > MaximumTags)
        {
            error = $"Add no more than {MaximumTags} tags.";
            return false;
        }
        tags = normalized;
        return true;
    }

    public static IReadOnlyList<string> FromStored(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool IsValid(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-') return false;
        var previousWasHyphen = false;
        foreach (var character in value)
        {
            if (character == '-')
            {
                if (previousWasHyphen) return false;
                previousWasHyphen = true;
                continue;
            }
            if (!char.IsAsciiLetterOrDigit(character)) return false;
            previousWasHyphen = false;
        }
        return true;
    }
}

public sealed record ScoreAwardPlan(int ValueAwarded, int NextCurrentValue);
public sealed record TeamScoreEligibility(bool IsBanned, bool IsHidden, bool IsSuspended, bool IsDisbanded);

public sealed class DynamicChallengeScoring
{
    public int Calculate(int initial, int minimum, int decay, long solveCount)
    {
        if (initial < 1) throw new ArgumentOutOfRangeException(nameof(initial));
        if (minimum < 1 || minimum > initial) throw new ArgumentOutOfRangeException(nameof(minimum));
        if (decay <= 0) return initial;
        var solves = Math.Max(0, solveCount);
        var raw = ((minimum - initial) / (double)((long)decay * decay) * solves * solves) + initial;
        return Math.Max(minimum, (int)Math.Ceiling(raw));
    }

    public ScoreAwardPlan Plan(int currentValue, int initial, int minimum, int decay, long eligibleSolveCountAfterAward) =>
        new(currentValue, Calculate(initial, minimum, decay, eligibleSolveCountAfterAward));

    public bool CountsForDecay(TeamScoreEligibility team) =>
        !team.IsBanned && !team.IsHidden && !team.IsSuspended && !team.IsDisbanded;
}
