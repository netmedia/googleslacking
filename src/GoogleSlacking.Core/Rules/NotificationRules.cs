using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GoogleSlacking.Domain;

namespace GoogleSlacking.Core.Rules;

public sealed record NotificationInput(
    SpaceKind SpaceKind,
    NotifyLevel Level,
    bool IsMuted,
    bool MentionedDirectly,
    bool MentionedAll,
    bool KeywordMatched,
    bool IsThreadReply,
    bool ParticipatesInThread,
    bool NotifyThreadReplies);

/// <summary>Slack-like notification decisions. Pure functions, unit-tested.</summary>
public static class NotificationRules
{
    /// <summary>Why (if at all) a user should be notified about a new message.</summary>
    public static ActivityKind? Decide(NotificationInput i)
    {
        var level = i.Level == NotifyLevel.Default
            ? (i.SpaceKind == SpaceKind.Space ? NotifyLevel.Mentions : NotifyLevel.All)
            : i.Level;

        if (level == NotifyLevel.Nothing) return null;
        if (i.MentionedDirectly) return ActivityKind.Mention;      // direct @you beats mute
        if (i.IsMuted) return null;
        if (i.MentionedAll) return ActivityKind.Mention;
        if (i.KeywordMatched) return ActivityKind.Keyword;
        if (i.IsThreadReply && i.ParticipatesInThread && i.NotifyThreadReplies) return ActivityKind.ThreadReply;
        if (level == NotifyLevel.All)
            return i.SpaceKind == SpaceKind.Space ? ActivityKind.AllMessages : ActivityKind.DirectMessage;
        return null;
    }

    /// <summary>True while notifications are paused (snooze) or inside the user's DND schedule.</summary>
    public static bool IsDoNotDisturb(UserSettings? s, DateTimeOffset now)
    {
        if (s is null) return false;
        if (s.SnoozeUntil is { } until && until > now) return true;
        if (!s.DndScheduleEnabled || s.DndStart is not { } start || s.DndEnd is not { } end) return false;

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { tz = TimeZoneInfo.Utc; }

        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        return start <= end
            ? local >= start && local < end            // e.g. 13:00–14:00
            : local >= start || local < end;           // over midnight, e.g. 22:00–08:00
    }

    public static IReadOnlyList<string> ParseKeywords(string? keywords) =>
        string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(k => k.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Whole-word, case- and diacritics-insensitive match ("cevapi" matches "Ćevapi").</summary>
    public static bool MatchesKeyword(string? text, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords.Count == 0) return false;
        var hay = Fold(text);
        foreach (var k in keywords)
        {
            var needle = Regex.Escape(Fold(k));
            if (Regex.IsMatch(hay, $@"(?<![\p{{L}}\p{{N}}_]){needle}(?![\p{{L}}\p{{N}}_])")) return true;
        }
        return false;
    }

    /// <summary>Lower-case and strip diacritics (đ → d as well).</summary>
    public static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
