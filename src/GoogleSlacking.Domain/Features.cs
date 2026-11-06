namespace GoogleSlacking.Domain;

/// <summary>Per-space notification preference (Slack: "All new messages / Mentions / Nothing").</summary>
public enum NotifyLevel
{
    /// <summary>DMs and group chats: All. Named spaces: Mentions.</summary>
    Default = 0,
    All = 1,
    Mentions = 2,
    Nothing = 3,
}

/// <summary>Per-user settings: notifications, Do Not Disturb, status.</summary>
public class UserSettings : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }

    /// <summary>Comma-separated keywords that notify like a mention (diacritics/case-insensitive, whole word).</summary>
    public string? Keywords { get; set; }

    /// <summary>IANA time zone used for the DND schedule.</summary>
    public string TimeZone { get; set; } = "Europe/Zagreb";

    public bool DndScheduleEnabled { get; set; }
    public TimeOnly? DndStart { get; set; }
    public TimeOnly? DndEnd { get; set; }

    /// <summary>"Pause notifications" until this time.</summary>
    public DateTimeOffset? SnoozeUntil { get; set; }

    public bool NotifyThreadReplies { get; set; } = true;

    /// <summary>Only send push when not active in a client (Slack behaviour). False = always push.</summary>
    public bool PushOnlyWhenInactive { get; set; } = true;

    public string? StatusEmoji { get; set; }
    public string? StatusText { get; set; }
    public DateTimeOffset? StatusExpiresAt { get; set; }

    /// <summary>Manually set "away".</summary>
    public bool ForceAway { get; set; }

    /// <summary>
    /// Opt-in, reciprocal (like WhatsApp): others see when I've read their messages, and only then do I see theirs.
    /// </summary>
    public bool ShareReadReceipts { get; set; }

    /// <summary>Don't broadcast "… is typing" to others (Slack-style: shown by default).</summary>
    public bool HideTyping { get; set; }
}

/// <summary>A browser/device Web Push subscription (RFC 8030/8291).</summary>
public class WebPushSubscription : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int FailureCount { get; set; }
}

public enum ActivityKind
{
    Mention = 0,
    DirectMessage = 1,
    ThreadReply = 2,
    Keyword = 3,
    Reaction = 4,
    Reminder = 5,
    AllMessages = 6,
}

/// <summary>An entry in the user's Activity feed (Slack "Activity").</summary>
public class ActivityItem : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }
    public ActivityKind Kind { get; set; }
    public Guid SpaceId { get; set; }
    public Guid? MessageId { get; set; }
    public string? ActorGoogleUserName { get; set; }
    public string? Preview { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Individual reactions (who reacted with what), so the UI can highlight "mine".</summary>
public class MessageReaction : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }

    /// <summary>"spaces/X/messages/Y/reactions/Z"</summary>
    public required string GoogleName { get; set; }
    public required string UserGoogleName { get; set; }

    /// <summary>Unicode emoji, or "custom:{uid}" for custom emoji.</summary>
    public required string Emoji { get; set; }
}

/// <summary>User-defined sidebar section (Slack "custom sections").</summary>
public class UserSection : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsCollapsed { get; set; }
}

public enum ScheduledMessageStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Cancelled = 3,
}

public class ScheduledMessage : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }
    public Guid SpaceId { get; set; }
    public string? ThreadName { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset SendAt { get; set; }
    public ScheduledMessageStatus Status { get; set; }
    public string? Error { get; set; }
    public string? SentMessageName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Slack "Later": a saved message with an optional reminder.</summary>
public class SavedItem : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }
    public Guid MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemindAt { get; set; }
    public DateTimeOffset? RemindedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Composer draft, synced across devices. ThreadKey is "" for the main timeline.</summary>
public class Draft : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }
    public Guid SpaceId { get; set; }
    public string ThreadKey { get; set; } = "";
    public required string Text { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Pinned message in a space. Shared by all app users in that space (not visible in official Google Chat).</summary>
public class PinnedMessage : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SpaceId { get; set; }
    public Guid MessageId { get; set; }
    public Guid PinnedByUserId { get; set; }
    public DateTimeOffset PinnedAt { get; set; }
}

/// <summary>Link bookmarked in a space header (Slack "bookmarks bar").</summary>
public class SpaceBookmark : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SpaceId { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int SortOrder { get; set; }
}
