using System.Text.Json;

namespace GoogleSlacking.Google;

public static class ChatEventTypes
{
    public const string MessageCreated = "google.workspace.chat.message.v1.created";
    public const string MessageUpdated = "google.workspace.chat.message.v1.updated";
    public const string MessageDeleted = "google.workspace.chat.message.v1.deleted";
    public const string MessageBatchCreated = "google.workspace.chat.message.v1.batchCreated";
    public const string MessageBatchUpdated = "google.workspace.chat.message.v1.batchUpdated";
    public const string MessageBatchDeleted = "google.workspace.chat.message.v1.batchDeleted";
    public const string ReactionCreated = "google.workspace.chat.reaction.v1.created";
    public const string ReactionDeleted = "google.workspace.chat.reaction.v1.deleted";
    public const string MembershipCreated = "google.workspace.chat.membership.v1.created";
    public const string MembershipDeleted = "google.workspace.chat.membership.v1.deleted";
    public const string SpaceUpdated = "google.workspace.chat.space.v1.updated";
    public const string SpaceDeleted = "google.workspace.chat.space.v1.deleted";

    public const string MembershipUpdated = "google.workspace.chat.membership.v1.updated";
    public const string SpaceReadStateUpdated = "google.workspace.chat.spaceReadState.v1.updated";
    public const string ThreadReadStateUpdated = "google.workspace.chat.threadReadState.v1.updated";

    // Subscription lifecycle events (always delivered, no need to subscribe).
    public const string SubscriptionExpirationReminder = "google.workspace.events.subscription.v1.expirationReminder";
    public const string SubscriptionSuspended = "google.workspace.events.subscription.v1.suspended";
    public const string SubscriptionExpired = "google.workspace.events.subscription.v1.expired";

    /// <summary>Event types requested for the per-user "all my spaces" subscription.</summary>
    public static readonly string[] AllSpacesSubscription =
    [
        MessageCreated, MessageUpdated, MessageDeleted,
        ReactionCreated, ReactionDeleted,
        MembershipCreated, MembershipDeleted,
        SpaceUpdated, SpaceDeleted,
    ];

    /// <summary>Target resource meaning "every space the authorizing user is a member of".</summary>
    public const string AllSpacesTarget = "//chat.googleapis.com/spaces/-";

    /// <summary>Per-user subscription: read state from other clients (official Google Chat apps).</summary>
    public static readonly string[] UserSubscription = [SpaceReadStateUpdated];

    /// <summary>"users/123" -> "//cloudidentity.googleapis.com/users/123"</summary>
    public static string UserTarget(string googleUserName) => "//cloudidentity.googleapis.com/" + googleUserName;
}

public sealed record WorkspaceSubscription
{
    public string? Name { get; init; }
    public string? TargetResource { get; init; }
    public List<string>? EventTypes { get; init; }
    public string? State { get; init; }                   // ACTIVE | SUSPENDED | DELETED
    public string? SuspensionReason { get; init; }
    public DateTimeOffset? ExpireTime { get; init; }
}

public sealed record LongRunningOperation
{
    public string? Name { get; init; }
    public bool Done { get; init; }
    public JsonElement? Response { get; init; }
    public JsonElement? Error { get; init; }
}

/// <summary>A Workspace event as received through Pub/Sub (CloudEvents binary mode).</summary>
public sealed record WorkspaceEvent(
    string Id,
    string Type,
    string? Source,     // "//workspaceevents.googleapis.com/subscriptions/{id}"
    string? Subject,    // target resource, e.g. "//chat.googleapis.com/spaces/X"
    DateTimeOffset? Time,
    JsonElement? Data)
{
    /// <summary>"subscriptions/{id}" extracted from <see cref="Source"/>.</summary>
    public string? SubscriptionName
    {
        get
        {
            if (Source is null) return null;
            var i = Source.IndexOf("subscriptions/", StringComparison.Ordinal);
            return i >= 0 ? Source[i..] : null;
        }
    }
}
