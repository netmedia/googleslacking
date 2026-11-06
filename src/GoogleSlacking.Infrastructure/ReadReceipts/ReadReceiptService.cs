using GoogleSlacking.Core.Rules;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Infrastructure.ReadReceipts;

public sealed record ReaderDto(string UserName, DateTimeOffset LastReadTime);

/// <param name="Enabled">False when the company disabled it or the viewer doesn't share (reciprocity).</param>
/// <param name="Readers">Members who share their read state (excluding the viewer).</param>
/// <param name="UnknownCount">Members we can't know about: not app users, or not sharing.</param>
public sealed record ReadReceiptsDto(bool Enabled, IReadOnlyList<ReaderDto> Readers, int UnknownCount);

/// <summary>
/// Read receipts for app users. Google doesn't expose other people's read state, but we know it for everyone
/// who uses this app: from our own "mark read" calls and from per-user readState events (official clients too).
/// </summary>
public sealed class ReadReceiptService(AppDbContext db, IRealtimeNotifier realtime)
{
    public async Task<ReadReceiptsDto> GetAsync(Guid viewerId, Guid spaceId, CancellationToken ct)
    {
        var tenantEnabled = await TenantEnabledAsync(ct);
        var viewerShares = await db.UserSettings.AnyAsync(s => s.AppUserId == viewerId && s.ShareReadReceipts, ct);
        if (!ReadReceiptRules.CanView(tenantEnabled, viewerShares)) return new ReadReceiptsDto(false, [], 0);

        var members = await (from m in db.SpaceMembers
                             where m.SpaceId == spaceId && m.AppUserId != viewerId
                             join s in db.UserSettings on m.AppUserId equals s.AppUserId into sj
                             from s in sj.DefaultIfEmpty()
                             select new { m.GoogleUserName, m.LastReadTime, Shares = m.AppUserId != null && s != null && s.ShareReadReceipts })
            .ToListAsync(ct);

        var readers = members
            .Where(m => ReadReceiptRules.IsVisible(tenantEnabled, m.Shares, isViewer: false) && m.LastReadTime is not null)
            .Select(m => new ReaderDto(m.GoogleUserName, m.LastReadTime!.Value))
            .ToList();
        return new ReadReceiptsDto(true, readers, members.Count - readers.Count);
    }

    /// <summary>Call after a user's LastReadTime changed in a space: tells that space's clients to refresh receipts.</summary>
    public async Task OnReadAsync(Guid userId, IReadOnlyCollection<Guid> spaceIds, CancellationToken ct)
    {
        if (spaceIds.Count == 0) return;
        if (!await TenantEnabledAsync(ct)) return;
        if (!await db.UserSettings.AnyAsync(s => s.AppUserId == userId && s.ShareReadReceipts, ct)) return;
        foreach (var id in spaceIds)
            await realtime.ToSpaceAsync(id, "readReceiptsChanged", null, ct);
    }

    public Task<bool> TenantEnabledAsync(CancellationToken ct) =>
        db.CurrentTenantId is { } tid
            ? db.Tenants.Where(t => t.Id == tid).Select(t => t.ReadReceiptsEnabled).FirstOrDefaultAsync(ct)
            : Task.FromResult(false);
}
