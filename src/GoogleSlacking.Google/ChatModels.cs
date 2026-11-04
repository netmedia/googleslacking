using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoogleSlacking.Google;

// Subset of the Google Chat API v1 resources. Unknown fields are ignored;
// raw JSON for reactions/attachments is kept as JsonElement so we can pass it through.

public sealed record ChatSpace
{
    public string Name { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? SpaceType { get; init; }              // SPACE | GROUP_CHAT | DIRECT_MESSAGE
    public string? SpaceThreadingState { get; init; }    // THREADED_MESSAGES | GROUPED_MESSAGES | UNTHREADED_MESSAGES
    public DateTimeOffset? LastActiveTime { get; init; }
    public bool? SingleUserBotDm { get; init; }
}

public sealed record ChatUser
{
    public string Name { get; init; } = "";               // users/{id}
    public string? DisplayName { get; init; }             // NOT populated with user auth -> resolve via People API
    public string? Type { get; init; }                    // HUMAN | BOT
}

public sealed record ChatThread
{
    public string? Name { get; init; }
}

public sealed record ChatMessage
{
    public string Name { get; init; } = "";
    public ChatUser? Sender { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public DateTimeOffset? LastUpdateTime { get; init; }
    public DateTimeOffset? DeleteTime { get; init; }
    public string? Text { get; init; }
    public string? FormattedText { get; init; }
    public ChatThread? Thread { get; init; }
    public bool? ThreadReply { get; init; }
    public string? ClientAssignedMessageId { get; init; }
    public JsonElement? EmojiReactionSummaries { get; init; }
    public JsonElement? Attachment { get; init; }

    /// <summary>"spaces/X/messages/Y" -> "spaces/X"</summary>
    [JsonIgnore]
    public string SpaceName => SpaceNameOf(Name);

    public static string SpaceNameOf(string resourceName)
    {
        var parts = resourceName.Split('/');
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : resourceName;
    }
}

public sealed record ChatMembership
{
    public string Name { get; init; } = "";
    public string? State { get; init; }                   // JOINED | INVITED | NOT_A_MEMBER
    public string? Role { get; init; }
    public ChatUser? Member { get; init; }
}

public sealed record SpaceReadState
{
    public string? Name { get; init; }
    public DateTimeOffset? LastReadTime { get; init; }
}

public sealed record ListSpacesResponse(List<ChatSpace>? Spaces, string? NextPageToken);
public sealed record ListMessagesResponse(List<ChatMessage>? Messages, string? NextPageToken);
public sealed record ListMembershipsResponse(List<ChatMembership>? Memberships, string? NextPageToken);
