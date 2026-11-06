using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GoogleSlacking.Core.Unfurl;

public sealed record LinkPreview(string Url, string? Title, string? Description, string? ImageUrl, string? SiteName);

/// <summary>
/// Fetches OpenGraph/meta tags for link previews. Protected against SSRF: every TCP connection
/// (including redirects) is checked against private/loopback/link-local ranges at connect time.
/// </summary>
public sealed partial class LinkPreviewService(IHttpClientFactory factory, IMemoryCache cache, ILogger<LinkPreviewService> log)
{
    public const string HttpClientName = "unfurl";
    private const int MaxBytes = 512 * 1024;

    public async Task<LinkPreview?> GetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return null;

        var key = "unfurl:" + uri.AbsoluteUri;
        if (cache.TryGetValue(key, out LinkPreview? cached)) return cached;

        LinkPreview? preview = null;
        try
        {
            var http = factory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("User-Agent", "GoogleSlackingBot/1.0 (link preview)");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.IsSuccessStatusCode && res.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            {
                var html = await ReadLimitedAsync(res, ct);
                preview = HtmlMeta.Parse(html, res.RequestMessage?.RequestUri ?? uri);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or InvalidOperationException)
        {
            log.LogDebug(ex, "Unfurl failed for {Url}", url);
        }

        cache.Set(key, preview, preview is null ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(24));
        return preview;
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage res, CancellationToken ct)
    {
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes];
        var total = 0;
        int read;
        while (total < MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxBytes - total), ct)) > 0)
            total += read;
        var charset = res.Content.Headers.ContentType?.CharSet;
        Encoding enc;
        try { enc = charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { enc = Encoding.UTF8; }
        return enc.GetString(buffer, 0, total);
    }

    /// <summary>HttpClient handler that refuses to connect to non-public addresses.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(4),
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            var allowed = addresses.Where(IsPublic).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException($"Blocked non-public address for {ctx.DnsEndPoint.Host}");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 0 || b[0] == 127
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                     || b[0] >= 224);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC || ip.Equals(IPAddress.IPv6None));
        }
        return false;
    }
}

public static partial class HtmlMeta
{
    [GeneratedRegex("""<meta\s+[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("""(?<name>[a-zA-Z:_-]+)\s*=\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)')""")]
    private static partial Regex Attr();

    [GeneratedRegex("""<title[^>]*>(?<t>[\s\S]*?)</title>""", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    public static LinkPreview? Parse(string html, Uri baseUri)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in MetaTag().Matches(html))
        {
            string? key = null, content = null;
            foreach (Match a in Attr().Matches(m.Value))
            {
                var n = a.Groups["name"].Value.ToLowerInvariant();
                if (n is "property" or "name") key = a.Groups["v"].Value;
                else if (n == "content") content = a.Groups["v"].Value;
            }
            if (key is not null && content is not null) meta.TryAdd(key, WebUtility.HtmlDecode(content).Trim());
        }

        var title = Get("og:title") ?? Get("twitter:title")
                    ?? (TitleTag().Match(html) is { Success: true } t ? WebUtility.HtmlDecode(t.Groups["t"].Value).Trim() : null);
        var description = Get("og:description") ?? Get("twitter:description") ?? Get("description");
        var image = Get("og:image") ?? Get("twitter:image");
        if (image is not null && Uri.TryCreate(baseUri, image, out var abs) && abs.Scheme is "http" or "https")
            image = abs.AbsoluteUri;
        else image = null;

        if (string.IsNullOrWhiteSpace(title) && description is null) return null;
        return new LinkPreview(baseUri.AbsoluteUri, Trunc(title, 200), Trunc(description, 400), image, Get("og:site_name") ?? baseUri.Host);

        string? Get(string k) => meta.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n] + "…";
}
