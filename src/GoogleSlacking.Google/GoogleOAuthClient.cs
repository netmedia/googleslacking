using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Google;

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("scope")] string? Scope);

/// <summary>Exchanges a user's refresh token for a short-lived access token.</summary>
public sealed class GoogleOAuthClient(HttpClient http, IOptions<GoogleOptions> options)
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var o = options.Value;
        using var res = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
        }), ct);

        if (!res.IsSuccessStatusCode)
            throw new GoogleApiException(res.StatusCode, await res.Content.ReadAsStringAsync(ct), TokenEndpoint);

        return (await res.Content.ReadFromJsonAsync<TokenResponse>(ct))!;
    }
}
