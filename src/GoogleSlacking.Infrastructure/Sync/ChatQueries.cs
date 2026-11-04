using GoogleSlacking.Domain;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Infrastructure.Sync;

/// <summary>Read side used by the API. Everything here is served from the local cache — no Google calls.</summary>
public sealed class ChatQueries(AppDbContext db)
{
    public async Task<IReadOnlyList<SpaceDto>> GetSpacesAsync(Guid userId, CancellationToken ct)
    {
        var me = await db.Users.Where(u => u.Id == userId).Select(u => u.GoogleUserName).SingleAsync(ct);

        var memberships = await db.SpaceMembers
            .Where(m => m.AppUserId == userId && m.Space!.DeletedAt == null)
            .Select(m => new { m.SpaceId, m.LastReadTime, m.IsStarred, m.IsMuted, Space = m.Space! })
            .ToListAsync(ct);

        var unread = await (
                from m in db.Messages
                join sm in db.SpaceMembers on m.SpaceId equals sm.SpaceId
                where sm.AppUserId == userId
                      && m.DeleteTime == null
                      && m.SenderGoogleUserName != me
                      && (sm.LastReadTime == null || m.CreateTime > sm.LastReadTime)
                group m by m.SpaceId into g
                select new { SpaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SpaceId, x => x.Count, ct);

        // Names for DMs / group chats come from the other members.
        var spaceIds = memberships.Where(m => m.Space.Kind != SpaceKind.Space).Select(m => m.SpaceId).ToList();
        var others = await (
                from sm in db.SpaceMembers
                where spaceIds.Contains(sm.SpaceId) && sm.GoogleUserName != me
                join p in db.People on sm.GoogleUserName equals p.GoogleUserName into pj
                from p in pj.DefaultIfEmpty()
                select new { sm.SpaceId, Name = p != null ? p.DisplayName ?? p.Email : null })
            .ToListAsync(ct);
        var namesBySpace = others.GroupBy(o => o.SpaceId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Name ?? "Unknown").OrderBy(n => n).ToList());

        return memberships
            .Select(m => new SpaceDto(
                m.SpaceId,
                m.Space.GoogleName,
                DisplayNameFor(m.Space, namesBySpace.GetValueOrDefault(m.SpaceId)),
                m.Space.Kind,
                m.Space.IsThreaded,
                m.Space.LastActiveTime,
                m.LastReadTime,
                unread.GetValueOrDefault(m.SpaceId),
                m.IsStarred,
                m.IsMuted))
            .OrderByDescending(s => s.LastActiveTime)
            .ToList();
    }

    private static string DisplayNameFor(Space s, List<string>? otherNames) => s.Kind switch
    {
        SpaceKind.Space => s.DisplayName ?? s.GoogleName,
        _ when !string.IsNullOrWhiteSpace(s.DisplayName) => s.DisplayName!,
        _ when otherNames is { Count: > 0 } => otherNames.Count <= 3
            ? string.Join(", ", otherNames)
            : $"{string.Join(", ", otherNames.Take(3))} +{otherNames.Count - 3}",
        _ => "Direct message",
    };

    /// <summary>Newest-first page; the client reverses for display.</summary>
    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(
        Guid spaceId, DateTimeOffset? before, int limit, string? threadName, CancellationToken ct)
    {
        var q = db.Messages.Where(m => m.SpaceId == spaceId);
        if (before is { } b) q = q.Where(m => m.CreateTime < b);
        if (threadName is not null) q = q.Where(m => m.ThreadName == threadName);
        var page = await q.OrderByDescending(m => m.CreateTime).Take(limit).ToListAsync(ct);
        return await ToDtosAsync(db, page, ct);
    }

    public async Task<MessageDto?> GetMessageByNameAsync(string googleName, CancellationToken ct)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.GoogleName == googleName, ct);
        return m is null ? null : (await ToDtosAsync(db, [m], ct))[0];
    }

    public Task<bool> IsMemberAsync(Guid userId, Guid spaceId, CancellationToken ct) =>
        db.SpaceMembers.AnyAsync(m => m.AppUserId == userId && m.SpaceId == spaceId, ct);

    internal static async Task<IReadOnlyList<MessageDto>> ToDtosAsync(AppDbContext db, IReadOnlyCollection<Message> msgs, CancellationToken ct)
    {
        var senders = msgs.Select(m => m.SenderGoogleUserName).Distinct().ToList();
        var people = await db.People.Where(p => senders.Contains(p.GoogleUserName))
            .ToDictionaryAsync(p => p.GoogleUserName, ct);

        return msgs.Select(m =>
        {
            people.TryGetValue(m.SenderGoogleUserName, out var p);
            var deleted = m.DeleteTime is not null;
            return new MessageDto(
                m.Id, m.SpaceId, m.GoogleName, m.ThreadName, m.IsThreadReply,
                new PersonDto(m.SenderGoogleUserName, p?.DisplayName ?? p?.Email, p?.AvatarUrl, p?.Email),
                deleted ? null : m.Text,
                deleted ? null : m.FormattedText,
                deleted ? null : JsonHelpers.FromJson(m.ReactionsJson),
                deleted ? null : JsonHelpers.FromJson(m.AttachmentsJson),
                m.CreateTime, m.LastUpdateTime, deleted, m.ClientAssignedMessageId);
        }).ToList();
    }
}
