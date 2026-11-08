using System.Text.RegularExpressions;

namespace VRCGroupTools.Services;

public static class ModerationUserSearch
{
    private static readonly Regex Uuid = new("\\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\z", RegexOptions.CultureInvariant);
    // Preserve the original input and exact prefixed identifier. Only an explicitly bare UUID gains usr_.
    public static string? UserId(string input)
    {
        var trimmed = input.Trim();
        if (trimmed != input && (trimmed.StartsWith("usr_", StringComparison.OrdinalIgnoreCase) || Uuid.IsMatch(trimmed)))
            throw new ArgumentException("Invalid user identifier: preserve the exact ID without surrounding whitespace.");
        if (input.StartsWith("usr_", StringComparison.Ordinal))
        {
            if (!Uuid.IsMatch(input[4..])) throw new ArgumentException("Invalid User ID. Use usr_<UUID> or a bare dashed UUID.");
            return input;
        }
        if (Uuid.IsMatch(input)) return "usr_" + input;
        if (Regex.IsMatch(input, @"\A(?:usr|wrld|grp|avtr|file|gmem|grol)_", RegexOptions.IgnoreCase) || Regex.IsMatch(input, @"\A[0-9a-zA-Z]{8}-[0-9a-zA-Z-]{15,}\z"))
            throw new ArgumentException("Invalid user UUID; identifiers are not repaired or searched by name.");
        return null;
    }
    public static async Task<List<UserSearchResult>> SearchAsync(string input, Func<string, Task<UserDetails?>> lookup, Func<string, Task<List<UserSearchResult>>> names)
    {
        var id = UserId(input);
        if (id == null) return await names(input);
        var user = await lookup(id);
        if (user == null) return new();
        if (!string.Equals(user.UserId, id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Lookup returned a different user identity");
        return new() { new() { UserId = id, DisplayName = user.DisplayName, ProfilePicUrl = user.ProfilePicUrl, Tags = user.Tags, IsAgeVerified = user.IsAgeVerified } };
    }
}
