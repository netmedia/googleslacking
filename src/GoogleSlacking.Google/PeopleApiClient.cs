using System.Text.Json;

namespace GoogleSlacking.Google;

public sealed record PersonInfo(string GoogleUserName, string? DisplayName, string? Email, string? AvatarUrl);

/// <summary>
/// Resolves names/avatars for Chat users. The Chat API does not return sender display names
/// with user auth, so we look them up in the domain directory (directory.readonly scope).
/// </summary>
public sealed class PeopleApiClient(HttpClient http)
{
    public const string BaseUrl = "https://people.googleapis.com/v1/";

    /// <param name="googleUserNames">"users/{id}" values; up to 50 per call.</param>
    public async Task<IReadOnlyList<PersonInfo>> GetBatchAsync(
        string token, IReadOnlyCollection<string> googleUserNames, CancellationToken ct = default)
    {
        if (googleUserNames.Count == 0) return [];
        var query = string.Join("&", googleUserNames.Take(50)
            .Select(u => "resourceNames=" + GoogleHttp.Q("people/" + u["users/".Length..])));
        var uri = $"{BaseUrl}people:batchGet?{query}&personFields=names,emailAddresses,photos&sources=READ_SOURCE_TYPE_PROFILE&sources=READ_SOURCE_TYPE_DOMAIN_CONTACT";

        var doc = await GoogleHttp.SendAsync<JsonElement>(http, HttpMethod.Get, uri, token, null, ct);
        var result = new List<PersonInfo>();
        if (doc.ValueKind != JsonValueKind.Object || !doc.TryGetProperty("responses", out var responses))
            return result;

        foreach (var r in responses.EnumerateArray())
        {
            if (!r.TryGetProperty("person", out var p) || !p.TryGetProperty("resourceName", out var rn)) continue;
            var id = rn.GetString()!["people/".Length..];
            result.Add(new PersonInfo(
                "users/" + id,
                First(p, "names", "displayName"),
                First(p, "emailAddresses", "value"),
                First(p, "photos", "url")));
        }
        return result;
    }

    private static string? First(JsonElement person, string array, string field) =>
        person.TryGetProperty(array, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
        && arr[0].TryGetProperty(field, out var v) ? v.GetString() : null;
}
