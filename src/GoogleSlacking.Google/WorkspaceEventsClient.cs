using System.Text.Json;

namespace GoogleSlacking.Google;

/// <summary>Google Workspace Events API v1 — subscriptions that push Chat events to Pub/Sub.</summary>
public sealed class WorkspaceEventsClient(HttpClient http)
{
    public const string BaseUrl = "https://workspaceevents.googleapis.com/v1/";

    /// <summary>Creates a subscription and waits (briefly) for the long-running operation to finish.</summary>
    public async Task<WorkspaceSubscription?> CreateSubscriptionAsync(
        string token, string targetResource, IEnumerable<string> eventTypes, string pubSubTopic,
        bool includeResource, CancellationToken ct = default)
    {
        var body = new
        {
            targetResource,
            eventTypes = eventTypes.ToArray(),
            notificationEndpoint = new { pubsubTopic = pubSubTopic },
            payloadOptions = new { includeResource },
        };
        var op = await GoogleHttp.SendAsync<LongRunningOperation>(http, HttpMethod.Post, BaseUrl + "subscriptions", token, body, ct);

        for (var i = 0; op is { Done: false, Name: not null } && i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1 + i), ct);
            op = await GoogleHttp.SendAsync<LongRunningOperation>(http, HttpMethod.Get, BaseUrl + op.Name, token, null, ct);
        }

        if (op?.Error is { } err)
            throw new InvalidOperationException($"Subscription create failed: {err}");

        return op?.Response is { } resp ? resp.Deserialize<WorkspaceSubscription>(GoogleHttp.Json) : null;
    }

    /// <summary>Lists the caller's subscriptions for a target. The API requires at least one event type in the filter.</summary>
    public async Task<IReadOnlyList<WorkspaceSubscription>> ListSubscriptionsAsync(
        string token, string targetResource, string eventType, CancellationToken ct = default)
    {
        var filter = $"event_types:\"{eventType}\" AND target_resource=\"{targetResource}\"";
        var res = await GoogleHttp.SendAsync<ListSubscriptionsResponse>(http, HttpMethod.Get,
            $"{BaseUrl}subscriptions?filter={GoogleHttp.Q(filter)}", token, null, ct);
        return res?.Subscriptions ?? [];
    }

    public Task<WorkspaceSubscription?> GetSubscriptionAsync(string token, string name, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<WorkspaceSubscription>(http, HttpMethod.Get, BaseUrl + name, token, null, ct);

    /// <summary>Extends the subscription to the maximum allowed TTL ("0s" = maximum).</summary>
    public Task<LongRunningOperation?> RenewAsync(string token, string name, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<LongRunningOperation>(http, HttpMethod.Patch,
            $"{BaseUrl}{name}?updateMask=ttl", token, new { ttl = "0s" }, ct);

    public Task<LongRunningOperation?> ReactivateAsync(string token, string name, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<LongRunningOperation>(http, HttpMethod.Post, $"{BaseUrl}{name}:reactivate", token, new { }, ct);

    public Task<LongRunningOperation?> DeleteAsync(string token, string name, CancellationToken ct = default) =>
        GoogleHttp.SendAsync<LongRunningOperation>(http, HttpMethod.Delete, BaseUrl + name, token, null, ct);
}

public sealed record ListSubscriptionsResponse(List<WorkspaceSubscription>? Subscriptions, string? NextPageToken);
