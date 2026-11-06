using System.Text.Json;
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
        var meToken = " " + me + " ";

        var memberships = await db.SpaceMembers
            .Where(m => m.AppUserId == userId && m.Space!.DeletedAt == null)
            .Select(m => new { Member = m, Space = m.Space! })
            .ToListAsync(ct);

        var unread = await (
                from m in db.Messages
                join sm in db.SpaceMembers on m.SpaceId equals sm.SpaceId
                where sm.AppUserId == userId
                      && m.DeleteTime == null
                      && m.SenderGoogleUserName != me
                      && (sm.LastReadTime == null || m.CreateTime > sm.LastReadTime)
                group m by m.SpaceId into g
                select new
                {
                    SpaceId = g.Key,
                    Count = g.Count(),
                    Mentions = g.Count(x => x.MentionedUserNames != null
                                            && (x.MentionedUserNames.Contains(meToken) || x.MentionedUserNames.Contains(" users/all "))),
                })
            .ToDictionaryAsync(x => x.SpaceId, ct);

        // Names for DMs / group chats come from the other members.
        var spaceIds = memberships.Where(m => m.Space.Kind != SpaceKind.Space).Select(m => m.Space.Id).ToList();
        var others = await (
                from sm in db.SpaceMembers
                where spaceIds.Contains(sm.SpaceId) && sm.GoogleUserName != me
                join p in db.People on sm.GoogleUserName equals p.GoogleUserName into pj
                from p in pj.DefaultIfEmpty()
                select new { sm.SpaceId, sm.GoogleUserName, Name = p != null ? p.DisplayName ?? p.Email : null })
            .ToListAsync(ct);
        var othersBySpace = others.GroupBy(o => o.SpaceId).ToDictionary(g => g.Key, g => g.ToList());

        return memberships
            .Select(x =>
            {
                var m = x.Member;
                var s = x.Space;
                othersBySpace.TryGetValue(s.Id, out var o);
                unread.TryGetValue(s.Id, out var u);
                return new SpaceDto(
                    s.Id, s.GoogleName,
                    DisplayNameFor(s, o?.Select(z => z.Name ?? "Nepoznato").OrderBy(n => n).ToList()),
                    s.Kind, s.IsThreaded, s.LastActiveTime, m.LastReadTime,
                    u?.Count ?? 0, u?.Mentions ?? 0,
                    m.IsStarred, m.IsMuted, m.NotifyLevel, m.SectionId, m.SortOrder,
                    s.Kind == SpaceKind.DirectMessage && o is { Count: 1 } ? o[0].GoogleUserName : null);
            })
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
        _ => "Direktna poruka",
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

    /// <summary>Messages around a point in time (jump to search result / link). Newest-first.</summary>
    public async Task<IReadOnlyList<MessageDto>> GetMessagesAroundAsync(Guid spaceId, DateTimeOffset around, int each, CancellationToken ct)
    {
        var older = await db.Messages.Where(m => m.SpaceId == spaceId && m.CreateTime <= around)
            .OrderByDescending(m => m.CreateTime).Take(each).ToListAsync(ct);
        var newer = await db.Messages.Where(m => m.SpaceId == spaceId && m.CreateTime > around)
            .OrderBy(m => m.CreateTime).Take(each).ToListAsync(ct);
        return await ToDtosAsync(db, newer.Concat(older).OrderByDescending(m => m.CreateTime).ToList(), ct);
    }

    public async Task<MessageDto?> GetMessageByNameAsync(string googleName, CancellationToken ct)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.GoogleName == googleName, ct);
        return m is null ? null : (await ToDtosAsync(db, [m], ct))[0];
    }

    public async Task<MessageDto?> GetMessageAsync(Guid id, CancellationToken ct)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : (await ToDtosAsync(db, [m], ct))[0];
    }

    public Task<bool> IsMemberAsync(Guid userId, Guid spaceId, CancellationToken ct) =>
        db.SpaceMembers.AnyAsync(m => m.AppUserId == userId && m.SpaceId == spaceId, ct);

    public Task<IReadOnlyList<MessageDto>> ToDtosAsync(IReadOnlyCollection<Message> msgs, CancellationToken ct) =>
        ToDtosAsync(db, msgs, ct);

    internal static async Task<IReadOnlyList<MessageDto>> ToDtosAsync(AppDbContext db, IReadOnlyCollection<Message> msgs, CancellationToken ct)
    {
        if (msgs.Count == 0) return [];
        var ids = msgs.Select(m => m.Id).ToList();
        var senders = msgs.Select(m => m.SenderGoogleUserName).Distinct().ToList();
        var people = await db.People.Where(p => senders.Contains(p.GoogleUserName))
            .ToDictionaryAsync(p => p.GoogleUserName, ct);
        var reactions = (await db.Reactions.Where(r => ids.Contains(r.MessageId)).ToListAsync(ct))
            .ToLookup(r => r.MessageId);
        var pinned = (await db.Pins.Where(p => ids.Contains(p.MessageId)).Select(p => p.MessageId).ToListAsync(ct)).ToHashSet();

        return msgs.Select(m =>
        {
            people.TryGetValue(m.SenderGoogleUserName, out var p);
            var deleted = m.DeleteTime is not null;
            return new MessageDto(
                m.Id, m.SpaceId, m.GoogleName, m.ThreadName, m.IsThreadReply,
                ToPerson(m.SenderGoogleUserName, p),
                deleted ? null : m.Text,
                deleted ? null : m.FormattedText,
                deleted ? [] : BuildReactions(m.ReactionsJson, reactions[m.Id]),
                deleted ? null : JsonHelpers.FromJson(m.AttachmentsJson),
                deleted ? null : JsonHelpers.FromJson(m.AnnotationsJson),
                m.CreateTime, m.LastUpdateTime, deleted, pinned.Contains(m.Id), m.ClientAssignedMessageId);
        }).ToList();
    }

    public static PersonDto ToPerson(string userName, Person? p) =>
        new(userName, p?.DisplayName ?? p?.Email, p?.AvatarUrl, p?.Email);

    /// <summary>Merges Google's reaction summaries (counts) with individual reactions we know about (who).</summary>
    internal static IReadOnlyList<ReactionDto> BuildReactions(string? summariesJson, IEnumerable<MessageReaction> known)
    {
        var users = known.GroupBy(r => r.Emoji)
            .ToDictionary(g => g.Key, g => g.Select(r => r.UserGoogleName).Distinct().ToList());
        var result = new List<ReactionDto>();

        if (summariesJson is not null)
        {
            using var doc = JsonDocument.Parse(summariesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in doc.RootElement.EnumerateArray())
                {
                    if (!s.TryGetProperty("emoji", out var e)) continue;
                    var key = e.TryGetProperty("unicode", out var u) ? u.GetString()
                        : e.TryGetProperty("customEmoji", out var ce) && ce.TryGetProperty("uid", out var uid) ? "custom:" + uid.GetString()
                        : null;
                    if (key is null) continue;
                    var count = s.TryGetProperty("reactionCount", out var c) && c.TryGetInt32(out var n) ? n : 0;
                    var who = users.Remove(key, out var list) ? list : [];
                    count = Math.Max(count, who.Count);
                    if (count > 0) result.Add(new ReactionDto(key, count, who));
                }
            }
        }

        // Reactions we know about but the (possibly stale) summary doesn't have yet.
        foreach (var (key, who) in users)
            result.Add(new ReactionDto(key, who.Count, who));
        return result;
    }
}
