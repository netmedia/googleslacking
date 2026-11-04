using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoogleSlacking.Google;

public sealed class GoogleApiException(HttpStatusCode status, string body, string requestUri)
    : Exception($"Google API {(int)status} for {requestUri}: {Truncate(body)}")
{
    public HttpStatusCode Status { get; } = status;
    public string Body { get; } = body;

    private static string Truncate(string s) => s.Length > 500 ? s[..500] + "…" : s;
}

internal static class GoogleHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaxAttempts = 4;

    /// <summary>Sends a request with bearer auth; retries 429/5xx with exponential backoff (honours Retry-After).</summary>
    public static async Task<T?> SendAsync<T>(
        HttpClient http, HttpMethod method, string uri, string accessToken, object? body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (body is not null)
                req.Content = JsonContent.Create(body, body.GetType(), options: Json);

            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                if (res.StatusCode == HttpStatusCode.NoContent || res.Content.Headers.ContentLength == 0)
                    return default;
                return await res.Content.ReadFromJsonAsync<T>(Json, ct);
            }

            var transient = res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500;
            if (transient && attempt < MaxAttempts)
            {
                var delay = res.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 250));
                await Task.Delay(delay, ct);
                continue;
            }

            var text = await res.Content.ReadAsStringAsync(ct);
            throw new GoogleApiException(res.StatusCode, text, uri);
        }
    }

    public static string Q(string value) => Uri.EscapeDataString(value);
}
