using System.Globalization;
using System.Text;

namespace GoogleSlacking.Core.Rules;

/// <summary>
/// Slack-style search syntax:
///   free text, "exact phrase", in:#space, from:@person, before:2026-11-01, after:1.10.2026,
///   on:2026-11-04, has:link, has:file, has:reaction, is:thread, is:pinned, is:saved
/// </summary>
public sealed record SearchQuery(
    IReadOnlyList<string> Terms,
    string? In,
    string? From,
    DateOnly? Before,
    DateOnly? After,
    bool HasLink,
    bool HasFile,
    bool HasReaction,
    bool IsThread,
    bool IsPinned,
    bool IsSaved)
{
    public bool IsEmpty => Terms.Count == 0 && In is null && From is null && Before is null && After is null
                           && !HasLink && !HasFile && !HasReaction && !IsThread && !IsPinned && !IsSaved;

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "d.M.yyyy", "d.M.yyyy.", "dd.MM.yyyy", "dd.MM.yyyy."];

    public static SearchQuery Parse(string? q)
    {
        var terms = new List<string>();
        string? @in = null, from = null;
        DateOnly? before = null, after = null;
        bool link = false, file = false, reaction = false, thread = false, pinned = false, saved = false;

        foreach (var token in Tokenize(q ?? ""))
        {
            var colon = token.IndexOf(':');
            var key = colon > 0 ? token[..colon].ToLowerInvariant() : null;
            var val = colon > 0 ? token[(colon + 1)..] : token;

            switch (key)
            {
                case "in" when val.Length > 0: @in = val.TrimStart('#'); break;
                case "from" when val.Length > 0: from = val.TrimStart('@'); break;
                case "before" when TryDate(val, out var d): before = d; break;
                case "after" when TryDate(val, out var d): after = d; break;
                case "on" when TryDate(val, out var d): after = d.AddDays(-1); before = d.AddDays(1); break;
                case "has":
                    switch (val.ToLowerInvariant())
                    {
                        case "link": link = true; break;
                        case "file": file = true; break;
                        case "reaction": reaction = true; break;
                        default: terms.Add(token); break;
                    }
                    break;
                case "is":
                    switch (val.ToLowerInvariant())
                    {
                        case "thread": thread = true; break;
                        case "pinned": pinned = true; break;
                        case "saved": saved = true; break;
                        default: terms.Add(token); break;
                    }
                    break;
                default:
                    if (token.Length > 0) terms.Add(token);
                    break;
            }
        }
        return new SearchQuery(terms, @in, from, before, after, link, file, reaction, thread, pinned, saved);
    }

    private static bool TryDate(string s, out DateOnly d) =>
        DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    private static IEnumerable<string> Tokenize(string q)
    {
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var c in q)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
