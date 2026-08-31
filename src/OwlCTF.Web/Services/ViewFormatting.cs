namespace OwlCTF.Services;

public static class DiscordAvatar
{
    public static string Url(string? discordId, string? avatarHash, int size = 128)
    {
        var validId = !string.IsNullOrWhiteSpace(discordId) && discordId.All(char.IsAsciiDigit);
        var defaultAvatarIndex = validId && ulong.TryParse(discordId, out var snowflake) ? (snowflake >> 22) % 6 : 0;
        var validHash = !string.IsNullOrWhiteSpace(avatarHash) && avatarHash.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
        var safeSize = size is 32 or 64 or 128 or 256 ? size : 128;
        return validId && validHash
            ? $"https://cdn.discordapp.com/avatars/{discordId}/{avatarHash}.png?size={safeSize}"
            : $"https://cdn.discordapp.com/embed/avatars/{defaultAvatarIndex}.png";
    }
}

public static class TimeDisplay
{
    public static string UtcIso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O");
}
