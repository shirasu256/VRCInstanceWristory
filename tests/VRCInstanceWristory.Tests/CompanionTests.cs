using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Logging;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 同じ部屋にいた人の名前（2026-09-26のユーザー指定→実装メモ5.46）。
///
/// 人数のために追っている <c>OnPlayerJoined</c> / <c>OnPlayerLeft</c> から、その訪問で一緒にいた人を残す。
/// 自分（<c>User Authenticated</c> の本人）は含めない。デスクトップのウィンドウで行を選ぶと一覧が出る。
/// 名前はこの検証のために作ったもので、実在の利用者ではない。
/// </summary>
public class CompanionTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 26, 21, 0, 0);

    private static VisitTracker Tracker(ManualClock clock)
        => new("s", 0, new LogTimeConverter(EngineHarness.Tokyo), clock);

    [Fact]
    public void ログインしている本人の行を読む()
    {
        var events = LogText.Events(LogText.UserAuthenticated(SessionStart, "じぶん")).ToList();

        var ev = Assert.Single(events);
        Assert.Equal(LogEventKind.UserAuthenticated, ev.Kind);
        Assert.Equal(LogText.UserId("じぶん"), PlayerRef.Parse(ev.Payload).Key);
        Assert.Equal("じぶん", PlayerRef.Parse(ev.Payload).Name);
    }

    [Theory]
    [InlineData("みかん (usr_1)", "みかん")]
    [InlineData("名前 (かっこ) 入り (usr_2)", "名前 (かっこ) 入り")]
    [InlineData("書式が違う", "書式が違う")]
    public void 表示名を取り出す(string payload, string expected)
        => Assert.Equal(expected, PlayerRef.Parse(payload).Name);

    [Fact]
    public void 一緒にいた人を残し_自分は含めず_先に出た人に印を付ける()
    {
        using var dir = new TempLogDirectory();
        var t = SessionStart.AddMinutes(1);

        dir.WriteSession(
            SessionStart,
            LogText.UserAuthenticated(SessionStart, "じぶん")
            + LogText.Entering(t, "A") + LogText.Joining(t, Loc.GroupPublic("111")) + LogText.Joined(t)
            + LogText.PlayerJoined(t, "じぶん")
            + LogText.PlayerJoined(t, "ことり")
            + LogText.PlayerJoined(t, "Aoi_VR")
            + LogText.FinishedEntering(t)
            + LogText.PlayerJoined(t.AddMinutes(2), "もちもち")
            + LogText.PlayerLeft(t.AddMinutes(3), "Aoi_VR")

            // こちらが離れたあとに並ぶ OnPlayerLeft は、先に出たことにはしない。
            + LogText.DestinationSet(t.AddMinutes(5), Loc.Public("222"))
            + LogText.LeftRoomWith(t.AddMinutes(5), 0)
            + LogText.PlayerLeft(t.AddMinutes(5), "ことり")
            + LogText.PlayerLeft(t.AddMinutes(5), "もちもち"));

        using var harness = new EngineHarness(dir.Path, t.AddMinutes(6), Process(SessionStart));
        harness.Engine.Initialize();

        var visit = Assert.Single(harness.Snapshot().History);
        Assert.Equal(["ことり", "Aoi_VR", "もちもち"], visit.Companions.Select(c => c.Name));

        Assert.Null(visit.Companions[0].LeftAtUtc);
        Assert.Equal(harness.Utc(t.AddMinutes(3)), visit.Companions[1].LeftAtUtc);
        Assert.Null(visit.Companions[2].LeftAtUtc);
        Assert.Equal(harness.Utc(t.AddMinutes(2)), visit.Companions[2].FirstSeenUtc);
    }

    [Fact]
    public void 一度出て戻ってきた人は1人として数え_いることに戻す()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 26, 13, 0, 0, DateTimeKind.Utc));
        var tracker = Tracker(clock);
        var t = SessionStart.AddMinutes(1);
        VisitCompanion? last = null;

        foreach (var ev in LogText.Events(
                     LogText.Entering(t, "A") + LogText.Joining(t, Loc.GroupPublic("111")) + LogText.Joined(t)
                     + LogText.PlayerJoined(t, "ことり")
                     + LogText.PlayerLeft(t.AddMinutes(1), "ことり")
                     + LogText.PlayerJoined(t.AddMinutes(2), "ことり")))
        {
            tracker.Apply(ev);
            last = tracker.LastCompanion ?? last;
        }

        new LogTimeConverter(EngineHarness.Tokyo).TryToUtc(t, out var first);

        Assert.NotNull(last);
        Assert.Null(last!.Value.Companion.LeftAtUtc);
        Assert.Equal(first, last.Value.Companion.FirstSeenUtc);
    }

    [Fact]
    public void 対象外のインスタンスで一緒にいた人は残さない()
    {
        var clock = new ManualClock(new DateTime(2026, 9, 26, 13, 0, 0, DateTimeKind.Utc));
        var tracker = Tracker(clock);
        var t = SessionStart.AddMinutes(1);

        foreach (var ev in LogText.Events(LogText.Visit(t, Loc.Friends("999"), "F") + LogText.PlayerJoined(t, "ことり")))
        {
            tracker.Apply(ev);
            Assert.Null(tracker.LastCompanion);
        }
    }

    [Fact]
    public void 本人が分からないログでは自分も一覧に入るが_落ちはしない()
    {
        // 途中から読んだログでは User Authenticated がない。自分を見分けられないだけで、ほかは同じ。
        using var dir = new TempLogDirectory();
        var t = SessionStart.AddMinutes(1);

        dir.WriteSession(
            SessionStart,
            LogText.Entering(t, "A") + LogText.Joining(t, Loc.GroupPublic("111")) + LogText.Joined(t)
            + LogText.PlayerJoined(t, "じぶん")
            + LogText.PlayerJoined(t, "ことり")
            + LogText.FinishedEntering(t));

        using var harness = new EngineHarness(dir.Path, t.AddMinutes(1), Process(SessionStart));
        harness.Engine.Initialize();

        Assert.Equal(2, Assert.Single(harness.Snapshot().History).Companions.Count);
    }

    [Fact]
    public void 行の詳しい情報には一緒にいた人の写しを入れる()
    {
        var record = new VisitRecord
        {
            EventId = "e",
            SourceSessionId = "s",
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = "k",
            WorldId = Loc.WorldA,
            InstanceId = "111",
            AccessType = Core.Locations.AccessType.Public,
            VisitedAtUtc = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc),
        };

        record.Companions.Add(new Companion("usr_a", "ことり", record.VisitedAtUtc, null));

        var detail = Assert.Single(RowDetails.Build([record], null));

        // 表示側へ渡したあとで記録が増えても、渡した写しは変わらない（別のスレッドから読むため）。
        record.Companions.Add(new Companion("usr_b", "Aoi_VR", record.VisitedAtUtc, null));
        Assert.Single(detail.Companions);
        Assert.NotEqual(detail, Assert.Single(RowDetails.Build([record], null)));
    }

    [Fact]
    public void 出入りのたびに世代が進み_ウィンドウへ渡し直す()
    {
        using var dir = new TempLogDirectory();
        var t = SessionStart.AddMinutes(1);
        var file = dir.WriteSession(
            SessionStart,
            LogText.UserAuthenticated(SessionStart, "じぶん")
            + LogText.Visit(t, Loc.GroupPublic("111"), "A"));

        using var harness = new EngineHarness(dir.Path, t.AddMinutes(1), Process(SessionStart));
        harness.Engine.Initialize();
        var before = harness.Snapshot().Generation;

        TempLogDirectory.Append(file, LogText.PlayerJoined(t.AddMinutes(1), "ことり"));
        harness.SetNow(t.AddMinutes(2));
        harness.Engine.Update();

        var after = harness.Snapshot();
        Assert.True(after.Generation > before);
        Assert.Equal("ことり", Assert.Single(Assert.Single(after.History).Companions).Name);
    }
}
