namespace OwlCTF.Services;

public sealed record ChallengeCategory(string Key, string Name, string IconClass, bool IsBuiltIn = true);

public static class ChallengeCategoryCatalog
{
    public const string DefaultKey = "reverse-engineering";
    public const string CustomIconClass = "fa-solid fa-shapes";

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

    public static ChallengeCategory Resolve(string? key, IReadOnlyList<ChallengeCategory> categories) =>
        categories.FirstOrDefault(category => category.Key.Equals(key, StringComparison.Ordinal)) ?? Get(key);
}

public static class ChallengeCategoryPolicy
{
    public const int MaximumKeyLength = 40;
    public const int MaximumNameLength = 60;

    public static bool IsValidKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumKeyLength || value[0] == '-' || value[^1] == '-') return false;
        var previousWasHyphen = false;
        foreach (var character in value)
        {
            if (character == '-')
            {
                if (previousWasHyphen) return false;
                previousWasHyphen = true;
                continue;
            }
            if (!char.IsAsciiLetterOrDigit(character) || char.IsAsciiLetterUpper(character)) return false;
            previousWasHyphen = false;
        }
        return true;
    }

    public static bool IsValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 2 and <= MaximumNameLength
        && !value.Any(char.IsControl);

    public static string CreateKey(string name)
    {
        var key = new List<char>(Math.Min(name.Length, MaximumKeyLength));
        var separatorPending = false;
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (separatorPending && key.Count > 0 && key.Count < MaximumKeyLength) key.Add('-');
                if (key.Count >= MaximumKeyLength) break;
                key.Add(character);
                separatorPending = false;
            }
            else separatorPending = key.Count > 0;
        }
        return new string([.. key]).TrimEnd('-');
    }
}

public sealed class ChallengeCategoryService(OwlCTF.Data.AppDb db)
{
    public async Task<IReadOnlyList<ChallengeCategory>> GetAllAsync(CancellationToken ct)
    {
        var custom = await db.GetCustomChallengeCategoriesAsync(ct);
        return [.. ChallengeCategoryCatalog.All, .. custom.Select(category => new ChallengeCategory(category.Key, category.Name, ChallengeCategoryCatalog.CustomIconClass, false))];
    }

    public async Task<bool> IsValidAsync(string? key, CancellationToken ct) =>
        ChallengeCategoryCatalog.IsValid(key)
        || (!string.IsNullOrWhiteSpace(key) && await db.CustomChallengeCategoryExistsAsync(key, ct));

    public async Task<ChallengeCategory?> FindAsync(string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return (await GetAllAsync(ct)).FirstOrDefault(category =>
            category.Key.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)
            || category.Name.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ChallengeCategory?> ResolveOrCreateAsync(string? value, CancellationToken ct)
    {
        var name = value?.Trim();
        var existing = await FindAsync(name, ct);
        if (existing is not null) return existing;
        if (!ChallengeCategoryPolicy.IsValidName(name)) return null;
        var baseKey = ChallengeCategoryPolicy.CreateKey(name!);
        if (!ChallengeCategoryPolicy.IsValidKey(baseKey)) return null;

        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var suffix = attempt == 1 ? "" : "-" + attempt;
            var stemLength = Math.Min(baseKey.Length, ChallengeCategoryPolicy.MaximumKeyLength - suffix.Length);
            var key = baseKey[..stemLength].TrimEnd('-') + suffix;
            var categories = await GetAllAsync(ct);
            var sameName = categories.FirstOrDefault(category => category.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sameName is not null) return sameName;
            if (categories.Any(category => category.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
            if (await db.TryAddCustomChallengeCategoryAsync(key, name!, ct))
                return new ChallengeCategory(key, name!, ChallengeCategoryCatalog.CustomIconClass, false);
        }
        return null;
    }
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
