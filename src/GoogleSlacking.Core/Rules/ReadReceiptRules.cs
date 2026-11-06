namespace GoogleSlacking.Core.Rules;

/// <summary>Reciprocal, opt-in read receipts (WhatsApp model). Pure functions, unit-tested.</summary>
public static class ReadReceiptRules
{
    /// <summary>A viewer may see read receipts only if the company allows it and they share their own.</summary>
    public static bool CanView(bool tenantEnabled, bool viewerShares) => tenantEnabled && viewerShares;

    /// <summary>A member's read state is visible only if they share it too (and aren't the viewer).</summary>
    public static bool IsVisible(bool tenantEnabled, bool memberShares, bool isViewer) => tenantEnabled && memberShares && !isViewer;

    /// <summary>"Read" = the member's last-read time is at or after the message time.</summary>
    public static bool HasRead(DateTimeOffset? lastReadTime, DateTimeOffset messageTime) =>
        lastReadTime is { } t && t >= messageTime;
}
