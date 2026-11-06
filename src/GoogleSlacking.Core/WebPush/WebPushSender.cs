using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Core.WebPush;

public sealed record WebPushTarget(string Endpoint, string P256dh, string Auth);

public enum WebPushResult
{
    Delivered,
    /// <summary>404/410: the subscription is gone; delete it.</summary>
    Gone,
    Failed,
}

/// <summary>Sends encrypted Web Push messages with VAPID (RFC 8292) authentication.</summary>
public sealed class WebPushSender(HttpClient http, IOptions<WebPushOptions> options, TimeProvider clock)
{
    private static readonly ConcurrentDictionary<string, (string Jwt, DateTimeOffset Expires)> JwtCache = new();

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<WebPushResult> SendAsync(WebPushTarget target, object payload, TimeSpan ttl, string? topic = null,
        CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.IsConfigured) return WebPushResult.Failed;

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var body = WebPushEncryption.Encrypt(json, Base64Url.Decode(target.P256dh), Base64Url.Decode(target.Auth));

        using var req = new HttpRequestMessage(HttpMethod.Post, target.Endpoint);
        req.Content = new ByteArrayContent(body);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        req.Content.Headers.ContentEncoding.Add("aes128gcm");
        req.Headers.TryAddWithoutValidation("TTL", ((int)ttl.TotalSeconds).ToString());
        req.Headers.TryAddWithoutValidation("Urgency", "high");
        if (topic is not null) req.Headers.TryAddWithoutValidation("Topic", topic); // replaces an undelivered push with the same topic
        req.Headers.TryAddWithoutValidation("Authorization", $"vapid t={GetJwt(target.Endpoint, o)}, k={o.VapidPublicKey}");

        using var res = await http.SendAsync(req, ct);
        return res.StatusCode switch
        {
            HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Accepted => WebPushResult.Delivered,
            HttpStatusCode.NotFound or HttpStatusCode.Gone => WebPushResult.Gone,
            _ => WebPushResult.Failed,
        };
    }

    private string GetJwt(string endpoint, WebPushOptions o)
    {
        var uri = new Uri(endpoint);
        var audience = $"{uri.Scheme}://{uri.Authority}";
        var now = clock.GetUtcNow();
        if (JwtCache.TryGetValue(audience, out var c) && c.Expires > now.AddMinutes(10)) return c.Jwt;

        var exp = now.AddHours(12);
        var header = Base64Url.Encode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = exp.ToUnixTimeSeconds(), sub = o.Subject }));
        var input = $"{header}.{claims}";
        using var signer = new VapidKeys(o.VapidPublicKey!, o.VapidPrivateKey!).CreateSigner();
        var sig = signer.SignData(Encoding.ASCII.GetBytes(input), System.Security.Cryptography.HashAlgorithmName.SHA256); // IEEE P1363 (r||s)
        var jwt = $"{input}.{Base64Url.Encode(sig)}";
        JwtCache[audience] = (jwt, exp);
        return jwt;
    }
}
