using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Google;

/// <summary>
/// OAuth 2.0 JWT-bearer flow for a service account (used only for Pub/Sub pull).
/// Caches the token until shortly before expiry.
/// </summary>
public sealed class ServiceAccountTokenProvider(HttpClient http, IOptions<GoogleOptions> options, TimeProvider clock)
{
    internal sealed record Key(
        [property: JsonPropertyName("client_email")] string ClientEmail,
        [property: JsonPropertyName("private_key")] string PrivateKey,
        [property: JsonPropertyName("token_uri")] string? TokenUri);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;
    private Key? _key;

    public async Task<string> GetAccessTokenAsync(string scope, CancellationToken ct = default)
    {
        if (_token is not null && clock.GetUtcNow() < _expires) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.GetUtcNow() < _expires) return _token;

            _key ??= LoadKey();
            var tokenUri = _key.TokenUri ?? GoogleOAuthClient.TokenEndpoint;
            var assertion = CreateAssertion(_key, scope, tokenUri, clock.GetUtcNow());

            using var res = await http.PostAsync(tokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }), ct);
            if (!res.IsSuccessStatusCode)
                throw new GoogleApiException(res.StatusCode, await res.Content.ReadAsStringAsync(ct), tokenUri);

            var token = (await res.Content.ReadFromJsonAsync<TokenResponse>(ct))!;
            _token = token.AccessToken;
            _expires = clock.GetUtcNow().AddSeconds(token.ExpiresIn - 120);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private Key LoadKey()
    {
        var o = options.Value;
        var json = o.ServiceAccountKeyJson
            ?? (o.ServiceAccountKeyPath is { Length: > 0 } p ? File.ReadAllText(p)
                : throw new InvalidOperationException("Google:ServiceAccountKeyPath or Google:ServiceAccountKeyJson must be configured."));
        return JsonSerializer.Deserialize<Key>(json)!;
    }

    internal static string CreateAssertion(Key key, string scope, string audience, DateTimeOffset now)
    {
        var header = new { alg = "RS256", typ = "JWT" };
        var iat = now.ToUnixTimeSeconds();
        var payload = new { iss = key.ClientEmail, scope, aud = audience, iat, exp = iat + 3600 };

        var signingInput = $"{B64(JsonSerializer.SerializeToUtf8Bytes(header))}.{B64(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKey);
        var sig = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{B64(sig)}";
    }

    private static string B64(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
