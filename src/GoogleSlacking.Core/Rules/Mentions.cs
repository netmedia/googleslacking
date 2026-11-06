using System.Text.Json;

namespace GoogleSlacking.Core.Rules;

public static class Mentions
{
    public const string All = "users/all";

    /// <summary>User resource names mentioned in Chat message annotations (USER_MENTION).</summary>
    public static IReadOnlyList<string> FromAnnotations(JsonElement? annotations)
    {
        if (annotations is not { ValueKind: JsonValueKind.Array } arr) return [];
        var result = new List<string>();
        foreach (var a in arr.EnumerateArray())
        {
            if (!a.TryGetProperty("type", out var t) || t.GetString() != "USER_MENTION") continue;
            if (a.TryGetProperty("userMention", out var um) && um.TryGetProperty("user", out var u)
                && u.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                result.Add(name);
        }
        return result.Distinct().ToList();
    }

    /// <summary>Space-delimited form stored in Message.MentionedUserNames, e.g. " users/1 users/all ".</summary>
    public static string? ToStorage(IReadOnlyList<string> names) =>
        names.Count == 0 ? null : " " + string.Join(' ', names) + " ";

    public static bool Contains(string? storage, string userName) =>
        storage is not null && storage.Contains(" " + userName + " ", StringComparison.Ordinal);
}
