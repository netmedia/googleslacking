using System.Text;
using GoogleSlacking.Google;

namespace GoogleSlacking.Tests;

public class WorkspaceEventParserTests
{
    private static PubSubMessage Msg(string type, string json, string subject = "//chat.googleapis.com/spaces/AAA") => new(
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
        new Dictionary<string, string>
        {
            ["ce-type"] = type,
            ["ce-id"] = "evt-1",
            ["ce-source"] = "//workspaceevents.googleapis.com/subscriptions/sub-123",
            ["ce-subject"] = subject,
            ["ce-time"] = "2026-11-04T10:00:00Z",
        },
        "m1",
        DateTimeOffset.Parse("2026-11-04T10:00:01Z"));

    [Fact]
    public void Parses_cloud_event_attributes()
    {
        var e = WorkspaceEventParser.Parse(Msg(ChatEventTypes.MessageCreated, """{"message":{"name":"spaces/AAA/messages/1"}}"""))!;
        Assert.Equal(ChatEventTypes.MessageCreated, e.Type);
        Assert.Equal("subscriptions/sub-123", e.SubscriptionName);
        Assert.Equal("evt-1", e.Id);
    }

    [Fact]
    public void Extracts_single_message()
    {
        var e = WorkspaceEventParser.Parse(Msg(ChatEventTypes.MessageCreated,
            """{"message":{"name":"spaces/AAA/messages/1","text":"bok","createTime":"2026-11-04T10:00:00.123456Z","sender":{"name":"users/42"}}}"""))!;
        var m = Assert.Single(WorkspaceEventParser.ExtractMessages(e));
        Assert.Equal("bok", m.Text);
        Assert.Equal("spaces/AAA", m.SpaceName);
        Assert.Equal("users/42", m.Sender?.Name);
    }

    [Fact]
    public void Extracts_batch_messages()
    {
        var e = WorkspaceEventParser.Parse(Msg(ChatEventTypes.MessageBatchCreated,
            """{"messages":[{"message":{"name":"spaces/AAA/messages/1"}},{"message":{"name":"spaces/AAA/messages/2"}}]}"""))!;
        Assert.Equal(2, WorkspaceEventParser.ExtractMessages(e).Count);
    }

    [Fact]
    public void Reaction_resolves_parent_message()
    {
        var e = WorkspaceEventParser.Parse(Msg(ChatEventTypes.ReactionCreated,
            """{"reaction":{"name":"spaces/AAA/messages/1/reactions/xyz"}}"""))!;
        Assert.Equal("spaces/AAA/messages/1", WorkspaceEventParser.ExtractReactionMessageName(e));
    }

    [Fact]
    public void Subject_gives_space_name()
    {
        var e = WorkspaceEventParser.Parse(Msg(ChatEventTypes.MembershipCreated, "{}", "//chat.googleapis.com/spaces/BBB/members/7"))!;
        Assert.Equal("spaces/BBB", WorkspaceEventParser.SubjectSpaceName(e));
    }

    [Fact]
    public void Non_cloud_event_returns_null() =>
        Assert.Null(WorkspaceEventParser.Parse(new PubSubMessage(null, new(), "x", null)));
}
