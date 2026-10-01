using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様3.1〜3.4節（60分窓・回数）と受入項目 T07・T08・T16・T26。</summary>
public class HistoryAndCounterTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);

    private static VisitRecord Record(
        string id,
        DateTime visitedAtUtc,
        string? locationKey = null,
        string? eventId = null,
        int sessionOrder = 0,
        long offset = 0)
        => new()
        {
            EventId = eventId ?? $"s0001@{offset}",
            SourceSessionId = "s0001",
            SuccessByteOffset = offset,
            SessionOrder = sessionOrder,
            LocationKey = locationKey ?? $"wrld_x:{id}",
            WorldId = "wrld_x",
            InstanceId = id,
            AccessType = AccessType.Public,
            VisitedAtUtc = visitedAtUtc,
        };

    [Fact]
    public void T08_区切りより前だけを捨てそれ以外は古くても残す()
    {
        var history = new HistoryStore();
        history.Add(Record("a", Now - TimeSpan.FromHours(9), offset: 1));
        history.Add(Record("b", Now - TimeSpan.FromMinutes(90), offset: 2));
        history.Add(Record("c", Now, offset: 3));

        // 区切りがなければ9時間前の行も表示する（対象に滞在している間は消さない）。
        Assert.Equal(
            ["a", "b", "c"],
            history.GetVisible(Now, DateTime.MinValue).Select(r => r.InstanceId));

        // 退出から60分の期限（ここでは30分前）に達したら、そこより前をまとめて捨てる。
        var cutoff = Now - TimeSpan.FromMinutes(30);

        Assert.Equal(2, history.PruneBefore(cutoff));
        Assert.Equal(["c"], history.GetVisible(Now, cutoff).Select(r => r.InstanceId));
    }

    [Fact]
    public void T08_消去では回数を減らさない()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, true, EpochReason.PerLaunch));

        var first = Record("a", Now - TimeSpan.FromMinutes(90), offset: 1);
        counter.Apply(first, null);

        var history = new HistoryStore();
        history.Add(first);
        history.PruneBefore(Now);

        Assert.Empty(history.GetVisible(Now, Now));
        Assert.Equal(1, counter.Counts["wrld_x:a"]);

        // T16: 期限切れ後の再訪は1へ戻らず、継続する次の数字になる。
        var second = Record("a", Now, offset: 2);
        counter.Apply(second, null);

        Assert.Equal(2, second.VisitOrdinal);
    }

    [Fact]
    public void T07_同じイベントの再適用では増えない()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, true, EpochReason.PerLaunch));

        var history = new HistoryStore();
        var record = Record("a", Now, offset: 10);

        Assert.True(history.Add(record));
        Assert.True(counter.Apply(record, null));
        Assert.Equal(1, record.VisitOrdinal);

        // 同じ成功行をもう一度読んだ場合。
        var again = Record("a", Now, offset: 10);
        Assert.False(history.Add(again));
        Assert.False(counter.Apply(again, null));
        Assert.Equal(1, again.VisitOrdinal);
        Assert.Equal(1, counter.Counts["wrld_x:a"]);
    }

    [Fact]
    public void T07_別イベントで同じlocationへ再訪すると1行1回増える()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, true, EpochReason.PerLaunch));
        var history = new HistoryStore();

        var first = Record("a", Now - TimeSpan.FromMinutes(5), offset: 10);
        var second = Record("a", Now, offset: 20);

        history.Add(first);
        counter.Apply(first, null);
        history.Add(second);
        counter.Apply(second, null);

        Assert.Equal(2, history.GetVisible(Now, DateTime.MinValue).Count);
        Assert.Equal(1, first.VisitOrdinal);
        Assert.Equal(2, second.VisitOrdinal);
    }

    [Fact]
    public void T26_基準が不明なら回数を不明にする()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, false, EpochReason.FirstLaunch));

        var record = Record("a", Now, offset: 1);
        counter.Apply(record, null);

        Assert.Null(record.VisitOrdinal);
        Assert.False(counter.BaselineKnown);
        Assert.Empty(counter.Counts);
    }

    [Fact]
    public void 確定した新しい期間では0開始へ戻る()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, false, EpochReason.FirstLaunch));

        var unknown = Record("a", Now, offset: 1);
        counter.Apply(unknown, null);
        Assert.Null(unknown.VisitOrdinal);

        counter.ApplyResolution(new EpochResolution("s0002", Now.AddHours(3), true, EpochReason.PerLaunch));

        var known = Record("a", Now.AddHours(3), offset: 2);
        counter.Apply(known, null);

        Assert.Equal(1, known.VisitOrdinal);
    }

    [Fact]
    public void 適用位置より前の成功は再加算しない()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, true, EpochReason.PerLaunch));

        var old = Record("a", Now - TimeSpan.FromMinutes(90), offset: 100);
        var applied = counter.Apply(old, appliedOffsetForSource: 500);

        Assert.False(applied);
        Assert.Null(old.VisitOrdinal);
        Assert.Empty(counter.Counts);
    }

    [Fact]
    public void 並びは時刻とセッション順とファイル位置で安定する()
    {
        var history = new HistoryStore();
        var at = Now - TimeSpan.FromMinutes(1);

        history.Add(Record("c", at, eventId: "s2@5", sessionOrder: 1, offset: 5));
        history.Add(Record("b", at, eventId: "s1@9", sessionOrder: 0, offset: 9));
        history.Add(Record("a", at, eventId: "s1@1", sessionOrder: 0, offset: 1));

        Assert.Equal(["a", "b", "c"], history.GetVisible(Now, DateTime.MinValue).Select(r => r.InstanceId));
    }

    [Fact]
    public void 保存済みイベントの回数は期間が変わっても保持する()
    {
        var counter = new VisitCounter();
        counter.ApplyResolution(new EpochResolution("s0001", Now, true, EpochReason.PerLaunch));

        var record = Record("a", Now, offset: 1);
        counter.Apply(record, null);
        Assert.Equal(1, record.VisitOrdinal);

        counter.ApplyResolution(new EpochResolution("s0002", Now.AddHours(2), true, EpochReason.PerLaunch));

        var replayed = Record("a", Now, offset: 1);
        counter.Apply(replayed, null);

        Assert.Equal(1, replayed.VisitOrdinal);
        Assert.Empty(counter.Counts); // 新しい期間の回数は0から
    }
}
