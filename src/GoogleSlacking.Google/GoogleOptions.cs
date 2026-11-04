namespace GoogleSlacking.Google;

public sealed class GoogleOptions
{
    public const string Section = "Google";

    /// <summary>OAuth client (Web application) from the GCP project that hosts the Chat app.</summary>
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>GCP project ID that owns the Pub/Sub topic + subscription.</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>Topic id the Workspace Events API publishes to (e.g. "chat-events").</summary>
    public string PubSubTopic { get; set; } = "chat-events";

    /// <summary>Pull subscription id on that topic (e.g. "chat-events-backend").</summary>
    public string PubSubSubscription { get; set; } = "chat-events-backend";

    /// <summary>Path to a service-account JSON key with roles/pubsub.subscriber. Alternatively use <see cref="ServiceAccountKeyJson"/>.</summary>
    public string? ServiceAccountKeyPath { get; set; }
    public string? ServiceAccountKeyJson { get; set; }

    /// <summary>How many recent messages per space to backfill on first login.</summary>
    public int InitialBackfillPerSpace { get; set; } = 50;

    public string TopicResourceName => $"projects/{ProjectId}/topics/{PubSubTopic}";
    public string SubscriptionResourceName => $"projects/{ProjectId}/subscriptions/{PubSubSubscription}";
}

public static class GoogleScopes
{
    public const string ChatSpacesReadonly = "https://www.googleapis.com/auth/chat.spaces.readonly";
    public const string ChatMessages = "https://www.googleapis.com/auth/chat.messages";          // restricted
    public const string ChatMessagesReactions = "https://www.googleapis.com/auth/chat.messages.reactions";
    public const string ChatMembershipsReadonly = "https://www.googleapis.com/auth/chat.memberships.readonly";
    public const string ChatUsersReadState = "https://www.googleapis.com/auth/chat.users.readstate";
    public const string DirectoryReadonly = "https://www.googleapis.com/auth/directory.readonly";
    public const string PubSub = "https://www.googleapis.com/auth/pubsub";

    /// <summary>Scopes requested at user sign-in (in addition to openid/email/profile).</summary>
    public static readonly string[] UserScopes =
    [
        ChatSpacesReadonly,
        ChatMessages,
        ChatMessagesReactions,
        ChatMembershipsReadonly,
        ChatUsersReadState,
        DirectoryReadonly,
    ];
}
