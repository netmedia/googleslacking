using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GoogleSlacking.Google;

/// <summary>Google Chat API v1 with user authentication. Every call takes the user's access token.</summary>
public sealed class ChatApiClient(HttpClient http)
{
    public const string BaseUrl = "https://chat.googleapis.com/v1/";

    public async IAsyncEnumerable<ChatSpace> ListSpacesAsync(string token, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? page = null;
        do
        {
            var uri = $"{BaseUrl}spaces?pageSize=1000" + (page is null ? "" : $"&pageToken={GoogleHttp.Q(page)}");
            var res = await GoogleHttp.SendAsync<ListSpacesResponse>(http, HttpMethod.Get, uri, token, null, ct);
            foreach (var s in res?.Spaces ?? []) yield return s;
            page = res?.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    public Task<ChatSpace?> GetSpaceAsync(string token, string spaceName, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<ChatSpace>(http, HttpMethod.Get, BaseUrl + spaceName, token, null, ct);

    /// <summary>Newest-first page of messages. Use <paramref name="createdBefore"/> for "load older".</summary>
    public Task<ListMessagesResponse?> ListMessagesAsync(
        string token, string spaceName, int pageSize, DateTimeOffset? createdBefore = null,
        string? pageToken = null, CancellationToken ct = default)
    {
        var uri = $"{BaseUrl}{spaceName}/messages?pageSize={pageSize}&orderBy={GoogleHttp.Q("createTime desc")}";
        if (createdBefore is { } before)
            uri += "&filter=" + GoogleHttp.Q($"createTime < \"{before.UtcDateTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}\"");
        if (pageToken is not null)
            uri += "&pageToken=" + GoogleHttp.Q(pageToken);
        return GoogleHttp.SendAsync<ListMessagesResponse>(http, HttpMethod.Get, uri, token, null, ct);
    }

    public Task<ChatMessage?> GetMessageAsync(string token, string messageName, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<ChatMessage>(http, HttpMethod.Get, BaseUrl + messageName, token, null, ct);

    /// <summary>
    /// Sends a message as the user. <paramref name="clientMessageId"/> must start with "client-" and makes
    /// the call idempotent (safe to retry from an optimistic UI).
    /// </summary>
    public Task<ChatMessage?> CreateMessageAsync(
        string token, string spaceName, string text, string? threadName, string clientMessageId,
        IReadOnlyList<JsonElement>? attachments = null, CancellationToken ct = default)
    {
        var uri = $"{BaseUrl}{spaceName}/messages?messageId={GoogleHttp.Q(clientMessageId)}";
        if (threadName is not null)
            uri += "&messageReplyOption=REPLY_MESSAGE_FALLBACK_TO_NEW_THREAD";
        var body = new
        {
            text,
            thread = threadName is null ? null : new { name = threadName },
            attachment = attachments is { Count: > 0 } ? attachments : null,
        };
        return GoogleHttp.SendAsync<ChatMessage>(http, HttpMethod.Post, uri, token, body, ct);
    }

    /// <summary>Edits the text of the user's own message.</summary>
    public Task<ChatMessage?> UpdateMessageTextAsync(string token, string messageName, string text, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<ChatMessage>(http, HttpMethod.Patch, $"{BaseUrl}{messageName}?updateMask=text", token, new { text }, ct);

    public Task DeleteMessageAsync(string token, string messageName, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<object>(http, HttpMethod.Delete, BaseUrl + messageName, token, null, ct);

    public Task<ChatReaction?> CreateReactionAsync(string token, string messageName, string unicode, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<ChatReaction>(http, HttpMethod.Post, $"{BaseUrl}{messageName}/reactions", token,
            new { emoji = new { unicode } }, ct);

    /// <summary>Finds the caller's reaction with the given emoji (needed to delete it).</summary>
    public async Task<IReadOnlyList<ChatReaction>> ListReactionsAsync(
        string token, string messageName, string? unicode = null, string? userName = null, CancellationToken ct = default)
    {
        var filters = new List<string>();
        if (unicode is not null) filters.Add($"emoji.unicode = \"{unicode}\"");
        if (userName is not null) filters.Add($"user.name = \"{userName}\"");
        var uri = $"{BaseUrl}{messageName}/reactions?pageSize=200";
        if (filters.Count > 0) uri += "&filter=" + GoogleHttp.Q(string.Join(" AND ", filters));
        var res = await GoogleHttp.SendAsync<ListReactionsResponse>(http, HttpMethod.Get, uri, token, null, ct);
        return res?.Reactions ?? [];
    }

    public Task DeleteReactionAsync(string token, string reactionName, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<object>(http, HttpMethod.Delete, BaseUrl + reactionName, token, null, ct);

    /// <summary>
    /// Uploads a file (max 200 MB) to a space. Returns the raw upload response, which is passed
    /// unchanged as an element of <c>attachment</c> when creating the message.
    /// </summary>
    public async Task<JsonElement> UploadAttachmentAsync(
        string token, string spaceName, string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        using var form = new MultipartContent("related");
        var meta = new StringContent(JsonSerializer.Serialize(new { filename = fileName }), System.Text.Encoding.UTF8, "application/json");
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);
        form.Add(meta);
        form.Add(file);

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://chat.googleapis.com/upload/v1/{spaceName}/attachments:upload?uploadType=multipart") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new GoogleApiException(res.StatusCode, text, req.RequestUri!.ToString());
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Downloads attachment bytes. The caller owns (and must dispose) the response.</summary>
    public async Task<HttpResponseMessage> DownloadMediaAsync(string token, string resourceName, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}media/{resourceName}?alt=media");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            res.Dispose();
            throw new GoogleApiException(res.StatusCode, text, req.RequestUri!.ToString());
        }
        return res;
    }

    /// <summary>Creates a named space, group chat or DM with members (returns the existing DM if there is one).</summary>
    public Task<ChatSpace?> SetupSpaceAsync(
        string token, string spaceType, string? displayName, IEnumerable<string> memberUserNames, CancellationToken ct = default)
    {
        var body = new
        {
            space = new { spaceType, displayName = spaceType == "SPACE" ? displayName : null },
            memberships = memberUserNames.Select(u => new { member = new { name = u, type = "HUMAN" } }).ToArray(),
        };
        return GoogleHttp.SendAsync<ChatSpace>(http, HttpMethod.Post, $"{BaseUrl}spaces:setup", token, body, ct);
    }

    public async Task<ChatSpace?> FindDirectMessageAsync(string token, string userName, CancellationToken ct = default)
    {
        try
        {
            return await GoogleHttp.SendAsync<ChatSpace>(http, HttpMethod.Get,
                $"{BaseUrl}spaces:findDirectMessage?name={GoogleHttp.Q(userName)}", token, null, ct);
        }
        catch (GoogleApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<ChatMembership?> AddMemberAsync(string token, string spaceName, string userName, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<ChatMembership>(http, HttpMethod.Post, $"{BaseUrl}{spaceName}/members", token,
            new { member = new { name = userName, type = "HUMAN" } }, ct);

    public async IAsyncEnumerable<ChatMembership> ListMembersAsync(
        string token, string spaceName, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? page = null;
        do
        {
            var uri = $"{BaseUrl}{spaceName}/members?pageSize=1000" + (page is null ? "" : $"&pageToken={GoogleHttp.Q(page)}");
            var res = await GoogleHttp.SendAsync<ListMembershipsResponse>(http, HttpMethod.Get, uri, token, null, ct);
            foreach (var m in res?.Memberships ?? []) yield return m;
            page = res?.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
    }

    public Task<SpaceReadState?> GetReadStateAsync(string token, string spaceName, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<SpaceReadState>(http, HttpMethod.Get,
            $"{BaseUrl}users/me/{spaceName}/spaceReadState", token, null, ct);

    public Task<SpaceReadState?> UpdateReadStateAsync(
        string token, string spaceName, DateTimeOffset lastReadTime, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<SpaceReadState>(http, HttpMethod.Patch,
            $"{BaseUrl}users/me/{spaceName}/spaceReadState?updateMask=lastReadTime", token,
            new { lastReadTime = lastReadTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") }, ct);
}
