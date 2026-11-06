using GoogleSlacking.Core.Rules;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Infrastructure.Search;

public sealed record SearchResultDto(MessageDto Message, string SpaceName);

/// <summary>
/// Message search over the local cache (only spaces the user is a member of).
/// LIKE-based so it works on LocalDB; switch to SQL Server Full-Text (CONTAINS) for large tenants.
/// </summary>
public sealed class SearchService(AppDbContext db, ChatQueries queries)
{
    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(Guid userId, string query, Guid? spaceId, int limit, CancellationToken ct)
    {
        var q = SearchQuery.Parse(query);
        if (q.IsEmpty && spaceId is null) return [];

        var mySpaces = db.SpaceMembers.Where(m => m.AppUserId == userId).Select(m => m.SpaceId);
        var msgs = db.Messages.Where(m => mySpaces.Contains(m.SpaceId) && m.DeleteTime == null);

        if (spaceId is { } sid) msgs = msgs.Where(m => m.SpaceId == sid);
        foreach (var term in q.Terms)
            msgs = msgs.Where(m => m.Text != null && EF.Functions.Like(m.Text, "%" + Escape(term) + "%", "\\"));

        if (q.In is { } inName)
        {
            var spaceIds = db.Spaces.Where(s => s.DisplayName != null && EF.Functions.Like(s.DisplayName, "%" + Escape(inName) + "%", "\\"))
                .Select(s => s.Id);
            msgs = msgs.Where(m => spaceIds.Contains(m.SpaceId));
        }
        if (q.From is { } from)
        {
            var userNames = db.People
                .Where(p => (p.DisplayName != null && EF.Functions.Like(p.DisplayName, "%" + Escape(from) + "%", "\\"))
                            || (p.Email != null && EF.Functions.Like(p.Email, Escape(from) + "%", "\\")))
                .Select(p => p.GoogleUserName);
            msgs = msgs.Where(m => userNames.Contains(m.SenderGoogleUserName));
        }
        if (q.After is { } after)
        {
            var t = new DateTimeOffset(after.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            msgs = msgs.Where(m => m.CreateTime > t);
        }
        if (q.Before is { } before)
        {
            var t = new DateTimeOffset(before.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            msgs = msgs.Where(m => m.CreateTime < t);
        }
        if (q.HasLink) msgs = msgs.Where(m => m.Text != null && EF.Functions.Like(m.Text, "%http%"));
        if (q.HasFile) msgs = msgs.Where(m => m.AttachmentsJson != null);
        if (q.HasReaction) msgs = msgs.Where(m => m.ReactionsJson != null);
        if (q.IsThread) msgs = msgs.Where(m => m.IsThreadReply);
        if (q.IsPinned) msgs = msgs.Where(m => db.Pins.Any(p => p.MessageId == m.Id));
        if (q.IsSaved) msgs = msgs.Where(m => db.SavedItems.Any(s => s.MessageId == m.Id && s.AppUserId == userId));

        var page = await msgs.OrderByDescending(m => m.CreateTime).Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct);
        var dtos = await queries.ToDtosAsync(page, ct);

        var spaces = (await queries.GetSpacesAsync(userId, ct)).ToDictionary(s => s.Id, s => s.DisplayName);
        return dtos.Select(d => new SearchResultDto(d, spaces.GetValueOrDefault(d.SpaceId) ?? "")).ToList();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
