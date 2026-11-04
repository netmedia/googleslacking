using System.Text;
using System.Text.Json;

namespace GoogleSlacking.Google;

/// <summary>
/// Turns a Pub/Sub message into a <see cref="WorkspaceEvent"/>. Workspace Events uses CloudEvents
/// binary content mode: metadata in "ce-*" attributes, the resource JSON in the message data.
/// </summary>
public static class WorkspaceEventParser
{
    public static WorkspaceEvent? Parse(PubSubMessage message)
    {
        var a = message.Attributes;
        if (a is null || !a.TryGetValue("ce-type", out var type)) return null;

        JsonElement? data = null;
        if (!string.IsNullOrEmpty(message.Data))
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(message.Data));
            if (!string.IsNullOrWhiteSpace(json))
                data = JsonDocument.Parse(json).RootElement.Clone();
        }

        return new WorkspaceEvent(
            a.GetValueOrDefault("ce-id") ?? message.MessageId ?? Guid.NewGuid().ToString(),
            type,
            a.GetValueOrDefault("ce-source"),
            a.GetValueOrDefault("ce-subject"),
            DateTimeOffset.TryParse(a.GetValueOrDefault("ce-time"), out var t) ? t : message.PublishTime,
            data);
    }

    /// <summary>Extracts messages from single ("message") or batch ("messages":[{"message":…}]) payloads.</summary>
    public static IReadOnlyList<ChatMessage> ExtractMessages(WorkspaceEvent e)
    {
        if (e.Data is not { ValueKind: JsonValueKind.Object } d) return [];

        if (d.TryGetProperty("message", out var single))
            return [single.Deserialize<ChatMessage>(GoogleHttp.Json)!];

        if (d.TryGetProperty("messages", out var many) && many.ValueKind == JsonValueKind.Array)
            return many.EnumerateArray()
                .Select(x => x.TryGetProperty("message", out var m) ? m : x)
                .Select(m => m.Deserialize<ChatMessage>(GoogleHttp.Json)!)
                .ToList();

        return [];
    }

    /// <summary>For reaction events: the parent message name ("spaces/X/messages/Y/reactions/Z" -> "spaces/X/messages/Y").</summary>
    public static string? ExtractReactionMessageName(WorkspaceEvent e)
    {
        if (e.Data is not { ValueKind: JsonValueKind.Object } d) return null;
        if (!d.TryGetProperty("reaction", out var r) || !r.TryGetProperty("name", out var n)) return null;
        var name = n.GetString();
        var i = name?.IndexOf("/reactions/", StringComparison.Ordinal) ?? -1;
        return i > 0 ? name![..i] : null;
    }

    /// <summary>Space name for space/membership events, taken from the CloudEvent subject.</summary>
    public static string? SubjectSpaceName(WorkspaceEvent e)
    {
        const string prefix = "//chat.googleapis.com/";
        return e.Subject is { } s && s.StartsWith(prefix, StringComparison.Ordinal)
            ? ChatMessage.SpaceNameOf(s[prefix.Length..])
            : null;
    }
}
