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
    bool IsStarred,
    bool IsMuted);

public sealed record MessageDto(
    Guid Id,
    Guid SpaceId,
    string GoogleName,
    string? ThreadName,
    bool IsThreadReply,
    PersonDto Sender,
    string? Text,
    string? FormattedText,
    JsonElement? Reactions,
    JsonElement? Attachments,
    DateTimeOffset CreateTime,
    DateTimeOffset? LastUpdateTime,
    bool IsDeleted,
    string? ClientAssignedMessageId);

/// <summary>Pushes changes to connected clients. Implemented with SignalR in the API.</summary>
public interface IRealtimeNotifier
{
    Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct);
    Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct);
    Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct);
}

public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct) => Task.CompletedTask;
    public Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct) => Task.CompletedTask;
    public Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct) => Task.CompletedTask;
}

internal static class JsonHelpers
{
    public static string? ToJson(JsonElement? e) =>
        e is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } v ? v.GetRawText() : null;

    public static JsonElement? FromJson(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
