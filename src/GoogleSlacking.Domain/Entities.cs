namespace GoogleSlacking.Domain;

/// <summary>Marker for entities that belong to a single Workspace customer (tenant).</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>
/// A Google Workspace organisation that has trusted the app (hosted mode),
/// or the single organisation of a self-hosted deployment.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Primary Workspace domain, matched against the OIDC <c>hd</c> claim (e.g. "netmedia.hr").</summary>
    public required string Domain { get; set; }

    /// <summary>Workspace customer ID (e.g. "C01abc23d"), filled in when known.</summary>
    public string? GoogleCustomerId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public class AppUser : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Chat resource name, "users/{sub}".</summary>
    public required string GoogleUserName { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>Refresh token protected with ASP.NET Data Protection. Never store it in plain text.</summary>
    public string? ProtectedRefreshToken { get; set; }
    public string? GrantedScopes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? InitialSyncCompletedAt { get; set; }
}

/// <summary>Cached directory info for any Chat user we've seen (senders, DM partners).</summary>
public class Person : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string GoogleUserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum SpaceKind
{
    Space = 0,
    GroupChat = 1,
    DirectMessage = 2,
}

public class Space : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>"spaces/AAAA..."</summary>
    public required string GoogleName { get; set; }
    public string? DisplayName { get; set; }
    public SpaceKind Kind { get; set; }
    public bool IsThreaded { get; set; }
    public DateTimeOffset? LastActiveTime { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>True once we've paged back to the first message, so "load older" stops calling Google.</summary>
    public bool HistoryComplete { get; set; }

    public List<SpaceMember> Members { get; set; } = [];
}

/// <summary>An app user's membership in a space plus their per-space client state.</summary>
public class SpaceMember : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }

    /// <summary>"users/{id}" — may be a user who never logged into this app.</summary>
    public required string GoogleUserName { get; set; }

    /// <summary>Set when the member is also an app user.</summary>
    public Guid? AppUserId { get; set; }

    public DateTimeOffset? LastReadTime { get; set; }
    public bool IsMuted { get; set; }
    public bool IsStarred { get; set; }
}

public class Message : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SpaceId { get; set; }

    /// <summary>"spaces/{space}/messages/{message}"</summary>
    public required string GoogleName { get; set; }

    /// <summary>"spaces/{space}/threads/{thread}"</summary>
    public string? ThreadName { get; set; }
    public bool IsThreadReply { get; set; }

    public required string SenderGoogleUserName { get; set; }
    public string? Text { get; set; }
    public string? FormattedText { get; set; }

    /// <summary>Reaction summaries / attachments / annotations as received from Google (JSON).</summary>
    public string? ReactionsJson { get; set; }
    public string? AttachmentsJson { get; set; }

    public DateTimeOffset CreateTime { get; set; }
    public DateTimeOffset? LastUpdateTime { get; set; }
    public DateTimeOffset? DeleteTime { get; set; }
    public string? ClientAssignedMessageId { get; set; }
}

public enum SubscriptionState
{
    Active = 0,
    Suspended = 1,
    Expired = 2,
    Deleted = 3,
}

/// <summary>A Workspace Events API subscription created on behalf of an app user.</summary>
public class EventSubscription : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AppUserId { get; set; }

    /// <summary>"subscriptions/{id}"</summary>
    public required string GoogleName { get; set; }
    public required string TargetResource { get; set; }
    public SubscriptionState State { get; set; }
    public DateTimeOffset? ExpireTime { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
