using System.Runtime.CompilerServices;

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
        string token, string spaceName, string text, string? threadName, string clientMessageId, CancellationToken ct = default)
    {
        var uri = $"{BaseUrl}{spaceName}/messages?messageId={GoogleHttp.Q(clientMessageId)}";
        if (threadName is not null)
            uri += "&messageReplyOption=REPLY_MESSAGE_FALLBACK_TO_NEW_THREAD";
        var body = new { text, thread = threadName is null ? null : new { name = threadName } };
        return GoogleHttp.SendAsync<ChatMessage>(http, HttpMethod.Post, uri, token, body, ct);
    }

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
