using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Google;

public sealed record PubSubMessage(
    string? Data,                                  // base64
    Dictionary<string, string>? Attributes,
    string? MessageId,
    DateTimeOffset? PublishTime);

public sealed record ReceivedMessage(string AckId, PubSubMessage Message);

internal sealed record PullResponse(List<ReceivedMessage>? ReceivedMessages);

/// <summary>Minimal Pub/Sub REST client (synchronous pull + ack). Good enough for one backend instance.</summary>
public sealed class PubSubClient(HttpClient http, ServiceAccountTokenProvider tokens, IOptions<GoogleOptions> options)
{
    public const string BaseUrl = "https://pubsub.googleapis.com/v1/";

    public async Task<IReadOnlyList<ReceivedMessage>> PullAsync(int maxMessages, CancellationToken ct)
    {
        var token = await tokens.GetAccessTokenAsync(GoogleScopes.PubSub, ct);
        var res = await GoogleHttp.SendAsync<PullResponse>(http, HttpMethod.Post,
            $"{BaseUrl}{options.Value.SubscriptionResourceName}:pull", token, new { maxMessages }, ct);
        return res?.ReceivedMessages ?? [];
    }

    public async Task AcknowledgeAsync(IEnumerable<string> ackIds, CancellationToken ct)
    {
        var ids = ackIds.ToArray();
        if (ids.Length == 0) return;
        var token = await tokens.GetAccessTokenAsync(GoogleScopes.PubSub, ct);
        await GoogleHttp.SendAsync<object>(http, HttpMethod.Post,
            $"{BaseUrl}{options.Value.SubscriptionResourceName}:acknowledge", token, new { ackIds = ids }, ct);
    }
}
