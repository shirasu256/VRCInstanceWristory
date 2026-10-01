using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// インスタンスに付ける目印（2026-09-22のユーザー指定→実装メモ5.32）。
///
/// 目印は1回の訪問ではなく<b>インスタンス</b>（ワールドID + インスタンス番号）に付く。
/// 同じインスタンスの行はすべて同じ印になり、同じ印をもう一度選ぶと外れる。
/// </summary>
public class InstanceMarkTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 22, 1, 0, 0);

    [Fact]
    public void 同じ印をもう一度選ぶと外れる()
    {
        var marks = new MarkStore();

        Assert.True(marks.Toggle("w:111", InstanceMark.Heart));
        Assert.Equal(InstanceMark.Heart, marks.Get("w:111"));

        // 別の印を選べば置き換わる。
        Assert.True(marks.Toggle("w:111", InstanceMark.Check));
        Assert.Equal(InstanceMark.Check, marks.Get("w:111"));

        // 同じ印をもう一度選ぶと外れる。
        Assert.True(marks.Toggle("w:111", InstanceMark.Check));
        Assert.Equal(InstanceMark.None, marks.Get("w:111"));
        Assert.Empty(marks.Entries());
    }

    [Fact]
    public void 目印はインスタンスごとに別々()
    {
        var marks = new MarkStore();

        marks.Toggle("w:111", InstanceMark.Heart);
        marks.Toggle("w:222", InstanceMark.Warning);

        Assert.Equal(InstanceMark.Heart, marks.Get("w:111"));
        Assert.Equal(InstanceMark.Warning, marks.Get("w:222"));

        // 同じ番号でもワールドが違えば別のインスタンス。
        Assert.Equal(InstanceMark.None, marks.Get("x:111"));
    }

    /// <summary>付箋は行と一緒に剥がれる。ログのリセットで、履歴から消えたインスタンスの目印は残さない（→5.54）。</summary>
    [Fact]
    public void 履歴から消えたインスタンスの目印は捨てる()
    {
        var marks = new MarkStore();

        marks.Toggle("w:111", InstanceMark.Heart);
        marks.Toggle("w:222", InstanceMark.Check);

        Assert.True(marks.RemoveStale(DateTime.MinValue, ["w:222"]));
        Assert.Equal(InstanceMark.None, marks.Get("w:111"));
        Assert.Equal(InstanceMark.Check, marks.Get("w:222"));

        // 捨てるものがなければ false。
        Assert.False(marks.RemoveStale(DateTime.MinValue, ["w:222"]));
    }

    [Fact]
    public void 目印は同じインスタンスの行すべてに出る()
    {
        var now = new DateTime(2026, 9, 22, 3, 0, 0, DateTimeKind.Utc);
        var records = new List<VisitRecord>
        {
            Record("a", "wrld_a", "111", now.AddMinutes(-30)),
            Record("b", "wrld_a", "222", now.AddMinutes(-20)),
            Record("c", "wrld_a", "111", now.AddMinutes(-10)),
        };

        var marks = new Dictionary<string, InstanceMark>(StringComparer.Ordinal)
        {
            ["wrld_a:111"] = InstanceMark.Heart,
        };

        var rows = RowFormatter.Build(records, "c", LogTimeConverter.Local, now, marks);

        Assert.Equal(InstanceMark.Heart, rows[0].Mark);
        Assert.Equal(InstanceMark.None, rows[1].Mark);
        Assert.Equal(InstanceMark.Heart, rows[2].Mark);
    }

    /// <summary>
    /// 目印が変われば行の内容が変わったことになる。
    /// 描画側は内容が同じなら描き直さないので、ここが等しいと目印が反映されない。
    /// </summary>
    [Fact]
    public void 目印の違う行は別の内容として扱う()
    {
        var row = new DisplayRow("a", "111", "01:00 - 01:10", "（1回目）", "世界", "Public", false);

        Assert.NotEqual(row, row with { Mark = InstanceMark.Heart });
        Assert.Equal(row with { Mark = InstanceMark.Heart }, row with { Mark = InstanceMark.Heart });
    }

    /// <summary>
    /// エンジン越しの付け外し。行はeventIdで指すが、印はそのインスタンスに付くので、
    /// 同じインスタンスの別の訪問にも出る。
    /// </summary>
    [Fact]
    public void エンジンは目印をインスタンスへ付け行を描き直させる()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.Move(SessionStart.AddMinutes(2), Loc.GroupPublic("222"), "B")
            + LogText.Move(SessionStart.AddMinutes(3), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(4)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(5), Process(SessionStart));
        harness.Engine.Initialize();

        var before = harness.Snapshot();
        Assert.Equal(3, before.History.Count);
        Assert.Empty(before.Marks);

        // 1行目（111 への1回目の訪問）を指して「ハート」を選ぶ。
        harness.Engine.SetMark(before.History[0].EventId, InstanceMark.Heart);

        var after = harness.Snapshot();
        var rows = RowFormatter.Build(after.History, after.CurrentEventId, harness.Time, harness.Clock.UtcNow, after.Marks);

        Assert.True(after.Generation > before.Generation, "行を描き直すため世代を進める");
        Assert.Equal(InstanceMark.Heart, rows[0].Mark);
        Assert.Equal(InstanceMark.None, rows[1].Mark);
        Assert.Equal(InstanceMark.Heart, rows[2].Mark);

        // 3行目（同じインスタンスへの3回目）で同じ印を選ぶと、まとめて外れる。
        harness.Engine.SetMark(after.History[2].EventId, InstanceMark.Heart);

        var cleared = harness.Snapshot();
        Assert.Empty(cleared.Marks);
    }

    /// <summary>知らない行を指定しても落ちない（期限切れで消えた直後など）。</summary>
    [Fact]
    public void 消えた行への指定は何もしない()
    {
        using var dir = new TempLogDirectory();
        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(3), Process(SessionStart));
        harness.Engine.Initialize();

        var before = harness.Snapshot().Generation;
        harness.Engine.SetMark("いない行", InstanceMark.Check);

        Assert.Empty(harness.Snapshot().Marks);
        Assert.Equal(before, harness.Snapshot().Generation);
    }

    private static VisitRecord Record(string eventId, string worldId, string instanceId, DateTime visitedUtc)
        => new()
        {
            EventId = eventId,
            SourceSessionId = "s",
            SuccessByteOffset = 0,
            SessionOrder = 0,
            LocationKey = $"{worldId}:{instanceId}",
            WorldId = worldId,
            InstanceId = instanceId,
            AccessType = AccessType.Public,
            WorldName = "世界",
            VisitedAtUtc = visitedUtc,
            LeftAtUtc = visitedUtc.AddMinutes(5),
            VisitOrdinal = 1,
        };
}
