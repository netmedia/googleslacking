using System.Security.Cryptography;
using System.Text;
using GoogleSlacking.Core;
using GoogleSlacking.Core.Rules;
using GoogleSlacking.Core.Unfurl;
using GoogleSlacking.Core.WebPush;
using GoogleSlacking.Domain;

namespace GoogleSlacking.Tests;

public class WebPushTests
{
    // RFC 8291, Appendix A.
    [Fact]
    public void Encryption_matches_rfc8291_test_vector()
    {
        var asPriv = Base64Url.Decode("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var asPub = Base64Url.Decode("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var uaPub = Base64Url.Decode("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var auth = Base64Url.Decode("BTBZMqHH6r4Tts7J_aSIgg");
        var salt = Base64Url.Decode("DGv6ra1nlYgDCS1FRnbzlw");
        using var asKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, D = asPriv, Q = new ECPoint { X = asPub[1..33], Y = asPub[33..65] },
        });

        var body = WebPushEncryption.Encrypt("When I grow up, I want to be a watermelon"u8, uaPub, auth, asKey, salt);

        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            Base64Url.Encode(body));
    }

    [Fact]
    public void Encrypt_decrypt_roundtrip()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ua.ExportParameters(false);
        byte[] uaPub = [0x04, .. p.Q.X!, .. p.Q.Y!];
        var auth = RandomNumberGenerator.GetBytes(16);
        var body = WebPushEncryption.Encrypt("""{"title":"Bok"}"""u8, uaPub, auth);
        Assert.Equal("""{"title":"Bok"}""", Encoding.UTF8.GetString(WebPushEncryption.Decrypt(body, ua, auth)));
    }

    [Fact]
    public void Vapid_keys_have_expected_sizes()
    {
        var k = VapidKeys.Generate();
        Assert.Equal(65, Base64Url.Decode(k.PublicKey).Length);
        Assert.Equal(32, Base64Url.Decode(k.PrivateKey).Length);
    }
}

public class NotificationRulesTests
{
    private static NotificationInput I(SpaceKind k, NotifyLevel l = NotifyLevel.Default, bool muted = false, bool me = false,
        bool all = false, bool kw = false, bool reply = false, bool part = false) => new(k, l, muted, me, all, kw, reply, part, true);

    [Theory]
    [InlineData(SpaceKind.DirectMessage, ActivityKind.DirectMessage)]
    [InlineData(SpaceKind.GroupChat, ActivityKind.DirectMessage)]
    public void Dms_notify_by_default(SpaceKind kind, ActivityKind expected) =>
        Assert.Equal(expected, NotificationRules.Decide(I(kind)));

    [Fact] public void Spaces_notify_only_on_mention_by_default() => Assert.Null(NotificationRules.Decide(I(SpaceKind.Space)));
    [Fact] public void Mention_notifies() => Assert.Equal(ActivityKind.Mention, NotificationRules.Decide(I(SpaceKind.Space, me: true)));
    [Fact] public void Direct_mention_beats_mute() => Assert.Equal(ActivityKind.Mention, NotificationRules.Decide(I(SpaceKind.Space, muted: true, me: true)));
    [Fact] public void Mute_blocks_at_all() => Assert.Null(NotificationRules.Decide(I(SpaceKind.Space, muted: true, all: true)));
    [Fact] public void Nothing_blocks_mentions() => Assert.Null(NotificationRules.Decide(I(SpaceKind.Space, NotifyLevel.Nothing, me: true)));
    [Fact] public void Thread_participant_notified() => Assert.Equal(ActivityKind.ThreadReply, NotificationRules.Decide(I(SpaceKind.Space, reply: true, part: true)));
    [Fact] public void All_level_in_space() => Assert.Equal(ActivityKind.AllMessages, NotificationRules.Decide(I(SpaceKind.Space, NotifyLevel.All)));

    [Fact]
    public void Keywords_are_whole_word_case_and_diacritics_insensitive()
    {
        var kws = NotificationRules.ParseKeywords("ćevapi, deploy");
        Assert.True(NotificationRules.MatchesKeyword("Idemo na Cevapi?", kws));
        Assert.True(NotificationRules.MatchesKeyword("DEPLOY je gotov", kws));
        Assert.False(NotificationRules.MatchesKeyword("redeployed", kws));
    }

    [Fact]
    public void Dnd_schedule_over_midnight_in_user_time_zone()
    {
        var s = new UserSettings { DndScheduleEnabled = true, DndStart = new TimeOnly(22, 0), DndEnd = new TimeOnly(8, 0), TimeZone = "Europe/Zagreb" };
        Assert.True(NotificationRules.IsDoNotDisturb(s, DateTimeOffset.Parse("2026-11-04T22:30:00Z")));  // 23:30 local
        Assert.False(NotificationRules.IsDoNotDisturb(s, DateTimeOffset.Parse("2026-11-04T11:00:00Z"))); // 12:00 local
    }

    [Fact]
    public void Snooze_is_dnd() =>
        Assert.True(NotificationRules.IsDoNotDisturb(new UserSettings { SnoozeUntil = DateTimeOffset.UtcNow.AddMinutes(5) }, DateTimeOffset.UtcNow));
}

public class SearchQueryTests
{
    [Fact]
    public void Parses_terms_and_filters()
    {
        var q = SearchQuery.Parse("deploy \"staging server\" in:#dev-team from:@marko after:1.10.2026 has:link is:thread");
        Assert.Equal(new[] { "deploy", "staging server" }, q.Terms);
        Assert.Equal("dev-team", q.In);
        Assert.Equal("marko", q.From);
        Assert.Equal(new DateOnly(2026, 10, 1), q.After);
        Assert.True(q.HasLink && q.IsThread);
    }

    [Fact]
    public void On_expands_to_range()
    {
        var q = SearchQuery.Parse("on:2026-11-04");
        Assert.Equal(new DateOnly(2026, 11, 3), q.After);
        Assert.Equal(new DateOnly(2026, 11, 5), q.Before);
    }
}

public class MentionsAndUnfurlTests
{
    [Fact]
    public void Mentions_from_annotations()
    {
        var ann = System.Text.Json.JsonDocument.Parse(
            """[{"type":"USER_MENTION","userMention":{"user":{"name":"users/42"}}},{"type":"RICH_LINK"}]""").RootElement;
        var m = Mentions.FromAnnotations(ann);
        Assert.Equal(new[] { "users/42" }, m);
        Assert.True(Mentions.Contains(Mentions.ToStorage(m), "users/42"));
        Assert.False(Mentions.Contains(Mentions.ToStorage(m), "users/4"));
    }

    [Fact]
    public void OpenGraph_parsing()
    {
        var p = HtmlMeta.Parse(
            """<title>Fallback</title><meta property="og:title" content="Netmedia &amp; Co"><meta content="Opis" name="description"><meta property='og:image' content='/a.png'>""",
            new Uri("https://netmedia.agency/blog/x"));
        Assert.Equal("Netmedia & Co", p!.Title);
        Assert.Equal("Opis", p.Description);
        Assert.Equal("https://netmedia.agency/a.png", p.ImageUrl);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("192.168.0.10", false)]
    [InlineData("169.254.169.254", false)] // cloud metadata
    [InlineData("::1", false)]
    [InlineData("142.250.180.14", true)]
    public void Ssrf_guard(string ip, bool isPublic) =>
        Assert.Equal(isPublic, LinkPreviewService.IsPublic(System.Net.IPAddress.Parse(ip)));
}
