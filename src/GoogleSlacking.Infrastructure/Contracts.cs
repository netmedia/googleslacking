using System.Text.Json;
using GoogleSlacking.Domain;

namespace GoogleSlacking.Infrastructure;

// DTOs shared by the REST API and the SignalR hub. Keep them flat and client-friendly.

public sealed record PersonDto(string Id, string? DisplayName, string? AvatarUrl, string? Email);

public sealed record SpaceDto(
    Guid Id,
    string GoogleName,
    string DisplayName,
    SpaceKind Kind,
    bool IsThreaded,
    DateTimeOffset? LastActiveTime,
    DateTimeOffset? LastReadTime,
    int UnreadCount,
    int MentionCount,
    bool IsStarred,
    bool IsMuted,
    NotifyLevel NotifyLevel,
    Guid? SectionId,
    int SortOrder,
    // For 1:1 DMs: the other person ("users/…"), for presence/status.
    string? DmUserName);

/// <summary>Reaction summary. Users is what we know (from events/our own actions) and may be shorter than Count.</summary>
public sealed record ReactionDto(string Emoji, int Count, IReadOnlyList<string> Users);

public sealed record MessageDto(
    Guid Id,
    Guid SpaceId,
    string GoogleName,
    string? ThreadName,
    bool IsThreadReply,
    PersonDto Sender,
    string? Text,
    string? FormattedText,
    IReadOnlyList<ReactionDto> Reactions,
    JsonElement? Attachments,
    JsonElement? Annotations,
    DateTimeOffset CreateTime,
    DateTimeOffset? LastUpdateTime,
    bool IsDeleted,
    bool IsPinned,
    string? ClientAssignedMessageId);

public sealed record NotificationPayload(
    string Title,
    string Body,
    Guid SpaceId,
    string? ThreadName,
    string MessageName,
    ActivityKind Kind,
    string? Icon,
    // Notifications with the same tag replace each other (dedupes SignalR + Web Push).
    string Tag);

public sealed record ActivityDto(
    Guid Id,
    ActivityKind Kind,
    Guid SpaceId,
    string SpaceName,
    MessageDto? Message,
    PersonDto? Actor,
    string? Preview,
    DateTimeOffset CreatedAt,
    bool IsRead);

public sealed record SavedItemDto(
    Guid Id,
    MessageDto Message,
    string SpaceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RemindAt,
    DateTimeOffset? CompletedAt);

public sealed record ScheduledMessageDto(
    Guid Id,
    Guid SpaceId,
    string SpaceName,
    string? ThreadName,
    string Text,
    DateTimeOffset SendAt,
    ScheduledMessageStatus Status,
    string? Error);

public sealed record DraftDto(Guid SpaceId, string? ThreadName, string Text, DateTimeOffset UpdatedAt);

public sealed record SettingsDto(
    string? Keywords,
    string TimeZone,
    bool DndScheduleEnabled,
    TimeOnly? DndStart,
    TimeOnly? DndEnd,
    DateTimeOffset? SnoozeUntil,
    bool NotifyThreadReplies,
    bool PushOnlyWhenInactive,
    string? StatusEmoji,
    string? StatusText,
    DateTimeOffset? StatusExpiresAt,
    bool ForceAway,
    bool ShareReadReceipts,
    // Read-only: false when the company switched read receipts off.
    bool ReadReceiptsAvailable,
    bool HideTyping);

/// <summary>"active" | "away" | "offline" (only app users are known).</summary>
public sealed record PresenceDto(string UserName, string State, string? StatusEmoji, string? StatusText);

/// <summary>Pushes changes to connected clients. Implemented with SignalR in the API.</summary>
public interface IRealtimeNotifier
{
    Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct);
    Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct);
    Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct);

    /// <summary>Event to all of one user's connections, e.g. "notification", "activityChanged", "savedChanged".</summary>
    Task ToUserAsync(Guid appUserId, string eventName, object? payload, CancellationToken ct);

    /// <summary>Event to everyone in a space, e.g. "pinsChanged", "bookmarksChanged".</summary>
    Task ToSpaceAsync(Guid spaceId, string eventName, object? payload, CancellationToken ct);
}

public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct) => Task.CompletedTask;
    public Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct) => Task.CompletedTask;
    public Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct) => Task.CompletedTask;
    public Task ToUserAsync(Guid appUserId, string eventName, object? payload, CancellationToken ct) => Task.CompletedTask;
    public Task ToSpaceAsync(Guid spaceId, string eventName, object? payload, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Who is connected/focused right now. Implemented by the SignalR hub's connection tracker.</summary>
public interface IPresence
{
    /// <summary>True when the user has a focused client that reported activity recently (suppresses push).</summary>
    bool IsActive(Guid appUserId);

    /// <summary>The space the user is currently looking at in a focused client, if any.</summary>
    Guid? FocusedSpace(Guid appUserId);

    /// <summary>"active" | "away" for every connected user of the tenant.</summary>
    IReadOnlyDictionary<Guid, string> Snapshot(Guid tenantId);
}

public sealed class NullPresence : IPresence
{
    public bool IsActive(Guid appUserId) => false;
    public Guid? FocusedSpace(Guid appUserId) => null;
    public IReadOnlyDictionary<Guid, string> Snapshot(Guid tenantId) => new Dictionary<Guid, string>();
}

internal static class JsonHelpers
{
    public static string? ToJson(JsonElement? e) =>
        e is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } v ? v.GetRawText() : null;

    public static JsonElement? FromJson(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
