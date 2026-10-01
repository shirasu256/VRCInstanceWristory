using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// VRChatのクラッシュの検出と、その見せ方（2026-09-21のユーザー指定→実装メモ5.30）。
///
/// クラッシュすると退出のログが書かれないので、ログだけでは「入室したきり」の行になり、
/// 退出時刻も人数も残らない。プロセスの消滅（またはログの途切れ）と、
/// 正常終了の記録（`VRCApplication: HandleApplicationQuit` / `OnApplicationQuit`）の
/// 有無を突き合わせてクラッシュを判定し、次の行との間に「∧ VRChat クライアントクラッシュ ∨」の帯を入れる。
/// 帯の中のその文字だけを赤にする。
/// </summary>
public class CrashTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 11, 1, 0, 0);

    private static readonly LogTimeConverter Time = new(EngineHarness.Tokyo);

    /// <summary>直前の間隔を60分以上空けて、回数の基準を確定させる。</summary>
    private static void WriteEarlierSession(TempLogDirectory dir)
    {
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));
    }

    /// <summary>
    /// 対象インスタンスにいる間にプロセスが消え、正常終了の記録がなければクラッシュ。
    /// 退出時刻はプロセスが消えた時刻、人数はそのときの在室者の数になる。
    /// </summary>
    [Fact]
    public void 正常終了の記録なくプロセスが消えたらクラッシュとして記録する()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A", people: 12)
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        harness.Engine.Initialize();

        // 滞在中は退出していない。
        var staying = Assert.Single(harness.Snapshot().History);
        Assert.Null(staying.LeftAtUtc);
        Assert.False(staying.EndedByCrash);

        var crashedAt = SessionStart.AddMinutes(30);
        harness.SetNow(crashedAt);
        harness.Processes.Exit(harness.Utc(crashedAt));
        harness.Engine.Update();

        var record = Assert.Single(harness.Snapshot().History);
        Assert.Equal(harness.Utc(crashedAt), record.LeftAtUtc);
        Assert.Equal(12, record.PeopleCount);
        Assert.True(record.EndedByCrash);
    }

    /// <summary>
    /// 正常終了のログがあれば、同じ「プロセスが消えた」でもクラッシュにしない。
    /// 退出時刻はログに書かれた時刻をそのまま使う。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 正常終了ならクラッシュにしない(bool alternateWording)
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        var quitAt = SessionStart.AddMinutes(20);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A", people: 5)
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2))
            + (alternateWording ? LogText.QuitAlternate(quitAt) : LogText.Quit(quitAt)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(21), Process(SessionStart));
        harness.Engine.Initialize();

        harness.Processes.Exit(harness.Utc(SessionStart.AddMinutes(21)));
        harness.Engine.Update();

        var record = Assert.Single(harness.Snapshot().History);
        Assert.Equal(harness.Utc(quitAt), record.LeftAtUtc);
        Assert.Equal(5, record.PeopleCount);
        Assert.False(record.EndedByCrash);
    }

    /// <summary>
    /// クラッシュしたときにこのアプリが動いていなくても、あとからログを読んで判定する。
    /// 消えた時刻は分からないので、ログが伸びなくなった時刻（ファイルの最終更新）を退出時刻にする。
    /// </summary>
    [Fact]
    public void 起動時にも過去のクラッシュを判定する()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        var crashedFile = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A", people: 8));

        var crashedAt = SessionStart.AddMinutes(25);
        Time.TryToUtc(crashedAt, out var crashedUtc);
        File.SetLastWriteTimeUtc(crashedFile, crashedUtc);

        // クラッシュから30分後に立ち上げ直した。
        var restart = SessionStart.AddMinutes(55);
        dir.WriteSession(
            restart,
            LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("222"), "B", people: 3)
            + LogText.WorldsTabShown(restart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, restart.AddMinutes(3), Process(restart, pid: 777));
        harness.Engine.Initialize();

        var history = harness.Snapshot().History;
        Assert.Equal(["111", "222"], history.Select(v => v.InstanceId));

        Assert.True(history[0].EndedByCrash);
        Assert.Equal(crashedUtc, history[0].LeftAtUtc);
        Assert.Equal(8, history[0].PeopleCount);

        // 立ち上げ直したあとの行はふつうの行。まだ滞在中なので退出もしていない。
        Assert.False(history[1].EndedByCrash);
        Assert.Null(history[1].LeftAtUtc);
    }

    /// <summary>
    /// 動いているVRChatのログセッションを、終わったものとして扱わない。
    /// 滞在中の行に退出時刻やクラッシュの印を付けてしまわないことの確認。
    /// </summary>
    [Fact]
    public void 動いているセッションはクラッシュにしない()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A", people: 4)
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(30), Process(SessionStart));
        harness.Engine.Initialize();
        harness.Engine.Update();

        var record = Assert.Single(harness.Snapshot().History);
        Assert.Null(record.LeftAtUtc);
        Assert.Null(record.PeopleCount);
        Assert.False(record.EndedByCrash);
        Assert.Equal(PresenceState.InTarget, harness.Snapshot().Presence);
    }

    /// <summary>
    /// 履歴が消える前（60分以内）に立ち上げ直して対象へ戻ると、
    /// クラッシュした行と戻った行の間に帯が入る（2026-09-21のユーザー指定）。
    /// </summary>
    [Fact]
    public void 消える前に立ち上げ直すと行の間にクラッシュの帯が入る()
    {
        using var dir = new TempLogDirectory();
        WriteEarlierSession(dir);

        dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A", people: 12)
            + LogText.WorldsTabShown(SessionStart.AddMinutes(2)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(10), Process(SessionStart));
        harness.Engine.Initialize();

        var crashedAt = SessionStart.AddMinutes(30);
        harness.SetNow(crashedAt);
        harness.Processes.Exit(harness.Utc(crashedAt));
        harness.Engine.Update();

        // 5分後に立ち上げ直して、対象インスタンスへ戻る。
        var restart = SessionStart.AddMinutes(35);
        dir.WriteSession(
            restart,
            LogText.Visit(restart.AddMinutes(1), Loc.GroupPublic("222"), "B", people: 3)
            + LogText.WorldsTabShown(restart.AddMinutes(2)));

        harness.SetNow(restart.AddMinutes(3));
        harness.Processes.Start(Process(restart, pid: 777), harness.Utc(restart));
        harness.Engine.Update();

        var snapshot = harness.Snapshot();
        var rows = RowFormatter.Build(snapshot.History, snapshot.CurrentEventId, harness.Time, harness.Clock.UtcNow);

        Assert.Equal(2, rows.Count);

        Assert.False(rows[0].CrashedBefore);

        // 戻った行の上に帯を入れる。
        Assert.True(rows[1].CrashedBefore);

        // 対象外を経由していないので「対象外のインスタンスへ移動」は並ばない。
        Assert.False(rows[1].ExcludedBefore);
        Assert.Equal("∧ VRChat クライアントクラッシュ ∨", string.Concat(RowFormatter.BandSegments(rows[1].CrashedBefore, rows[1].ExcludedBefore).Select(s => s.Text)));
    }

    /// <summary>帯の文言は「対象外のインスタンスへ移動」と同じ形（∧ … ∨）にする。</summary>
    [Fact]
    public void 帯の文言はクラッシュしたことが分かる形にする()
    {
        Assert.Equal("∧ VRChat クライアントクラッシュ ∨", string.Concat(RowFormatter.BandSegments(crashed: true, excluded: false).Select(s => s.Text)));
        Assert.Equal("∧ 対象外のインスタンスへ移動 ∨", string.Concat(RowFormatter.BandSegments(crashed: false, excluded: true).Select(s => s.Text)));
        Assert.Equal(string.Empty, string.Concat(RowFormatter.BandSegments(crashed: false, excluded: false).Select(s => s.Text)));
    }

    /// <summary>
    /// クラッシュと「対象外のインスタンスへ移動」が重なる区間は、帯を2枚にせず
    /// 1行にまとめて中身を並べる（2026-09-21のユーザー指定）。
    /// </summary>
    [Fact]
    public void 両方が重なるときは一行にまとめる()
    {
        Assert.Equal(
            "∧ VRChat クライアントクラッシュ・対象外のインスタンスへ移動 ∨",
            string.Concat(RowFormatter.BandSegments(crashed: true, excluded: true).Select(s => s.Text)));
    }

    /// <summary>
    /// 赤くするのは帯の中の「VRChat クライアントクラッシュ」だけ。
    /// 挟みの記号と「対象外のインスタンスへ移動」は他の帯と同じ色のままにする。
    /// </summary>
    [Fact]
    public void 赤くするのはクラッシュの文字だけ()
    {
        var segments = RowFormatter.BandSegments(crashed: true, excluded: true);

        Assert.Equal(
            [("∧ ", false), ("VRChat クライアントクラッシュ", true), ("・", false), ("対象外のインスタンスへ移動", false), (" ∨", false)],
            segments.Select(x => (x.Text, x.Crash)));

        // クラッシュのない帯には赤い部分がない。
        Assert.DoesNotContain(RowFormatter.BandSegments(crashed: false, excluded: true), x => x.Crash);

        // どちらも立たなければ帯そのものがない。
        Assert.Empty(RowFormatter.BandSegments(crashed: false, excluded: false));
    }

    private static VisitRecord Record(
        DateTime nowUtc,
        int minutesAgo,
        int? leftMinutesAgo,
        string id,
        bool crashed = false,
        bool excludedBefore = false,
        bool onlyFirstInstance = false,
        bool excludedAfter = false)
        => new()
        {
            EventId = $"s1@{id}-{minutesAgo}",
            SourceSessionId = "s1",
            SuccessByteOffset = minutesAgo,
            SessionOrder = 0,
            LocationKey = $"wrld_x:{id}",
            WorldId = "wrld_x",
            InstanceId = id,
            AccessType = AccessType.Public,
            WorldName = "ワールド",
            VisitedAtUtc = nowUtc - TimeSpan.FromMinutes(minutesAgo),
            LeftAtUtc = leftMinutesAgo is { } left ? nowUtc - TimeSpan.FromMinutes(left) : null,
            VisitOrdinal = 1,
            EndedByCrash = crashed,
            ExcludedBefore = excludedBefore,
            ExcludedOnlyFirstInstance = onlyFirstInstance,
            ExcludedAfter = excludedAfter,
        };

    /// <summary>
    /// クラッシュから立ち上げ直して、最初に入ったホームワールドだけを通って戻ったときは、
    /// 「対象外のインスタンスへ移動」を出さず「VRChat クライアントクラッシュ」だけにする（2026-10-01のユーザー指定→実装メモ5.125）。
    /// </summary>
    [Fact]
    public void クラッシュのあと最初のホームワールドだけを挟んだときはクラッシュの帯だけ()
    {
        var now = new DateTime(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);

        string Band(VisitRecord previous, VisitRecord next)
        {
            var rows = RowFormatter.Build([previous, next], currentEventId: null, Time, now);
            return string.Concat(RowFormatter.BandSegments(rows[1].CrashedBefore, rows[1].ExcludedBefore).Select(s => s.Text));
        }

        // 最初のホームワールドだけ → クラッシュの帯だけ。
        Assert.Equal("∧ VRChat クライアントクラッシュ ∨",
            Band(Record(now, 90, 80, "111", crashed: true), Record(now, 5, null, "222", excludedBefore: true, onlyFirstInstance: true)));

        // ホームワールドのあとにも対象外へ寄った → 両方。
        Assert.Equal("∧ VRChat クライアントクラッシュ・対象外のインスタンスへ移動 ∨",
            Band(Record(now, 90, 80, "111", crashed: true), Record(now, 5, null, "222", excludedBefore: true)));

        // クラッシュする前に対象外へ移っていた → 両方。
        Assert.Equal("∧ VRChat クライアントクラッシュ・対象外のインスタンスへ移動 ∨",
            Band(Record(now, 90, 80, "111", crashed: true, excludedAfter: true), Record(now, 5, null, "222", excludedBefore: true, onlyFirstInstance: true)));

        // クラッシュでなければ、ホームワールドだけでも対象外への移動を出す（これまでどおり）。
        Assert.Equal("∧ 対象外のインスタンスへ移動 ∨",
            Band(Record(now, 90, 80, "111"), Record(now, 5, null, "222", excludedBefore: true, onlyFirstInstance: true)));
    }

    /// <summary>
    /// クラッシュと「対象外のインスタンスへ移動」が重なっても、帯は1枚のまま。
    /// 行の高さも1枚ぶんしか増えない。
    /// </summary>
    [Fact]
    public void 両方が重なっても帯は一枚のまま()
    {
        var now = new DateTime(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = RowFormatter.Build(
            [Record(now, 90, 80, "111", crashed: true), Record(now, 5, null, "222", excludedBefore: true)],
            currentEventId: null,
            Time,
            now);

        Assert.True(rows[1].CrashedBefore);
        Assert.True(rows[1].ExcludedBefore);

        var layouts = renderer.Measure(rows);

        Assert.Equal(style.RowHeight, layouts[0].Height);
        Assert.Equal(style.RowHeight + style.AbsenceBandHeight, layouts[1].Height);

        renderer.Render(layouts, 0f);
        var pixels = renderer.GetPixels();

        (byte B, byte G, byte R, byte A) At(int x, int y)
        {
            var i = ((y * style.Width) + x) * 4;
            return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
        }

        var bandTop = style.ViewportTop + style.RowHeight;
        var surface = At(8, style.ViewportTop + (style.RowHeight / 2));

        // 帯は1枚（行より明るい）。
        Assert.True(At(8, bandTop + (style.AbsenceBandHeight / 2)).G > surface.G);

        // 1枚ぶんを過ぎたら行の背景へ戻る。
        Assert.Equal(surface.G, At(8, bandTop + style.AbsenceBandHeight + 2).G);

        // その1枚に文字がある。
        Assert.True(HasText(At, style, bandTop));
    }

    private static bool HasText(Func<int, int, (byte B, byte G, byte R, byte A)> at, PanelStyle style, int bandTop)
    {
        for (var y = bandTop + 2; y < bandTop + style.AbsenceBandHeight - 2; y++)
        {
            for (var x = 0; x < style.Width; x++)
            {
                if (at(x, y).A > style.BackgroundAlpha)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 帯の中の「VRChat クライアントクラッシュ」だけを赤で描く
    /// （2026-09-21のユーザー指定）。行の時刻や、クラッシュのない帯は赤くしない。
    /// </summary>
    [Fact]
    public void 帯のクラッシュの文字だけを赤で描く()
    {
        var now = new DateTime(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        // 1行目→2行目はクラッシュの帯、2行目→3行目は「対象外のインスタンスへ移動」だけの帯。
        var rows = RowFormatter.Build(
            [Record(now, 200, 190, "111", crashed: true), Record(now, 180, 140, "222"), Record(now, 100, null, "333", excludedBefore: true)],
            currentEventId: null,
            Time,
            now);

        Assert.True(rows[1].CrashedBefore);
        Assert.False(rows[1].ExcludedBefore);
        Assert.False(rows[2].CrashedBefore);
        Assert.True(rows[2].ExcludedBefore);

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);
        var pixels = renderer.GetPixels();

        int Reddest(int fromY, int toY)
        {
            var reddest = 0;

            for (var y = fromY; y < toY; y++)
            {
                for (var x = 0; x < style.Width; x++)
                {
                    var i = ((y * style.Width) + x) * 4;
                    reddest = Math.Max(reddest, pixels[i + 2] - Math.Max(pixels[i + 1], pixels[i]));
                }
            }

            return reddest;
        }

        var crashBandTop = style.ViewportTop + style.RowHeight;
        var absenceBandTop = crashBandTop + style.AbsenceBandHeight + style.RowHeight;

        // クラッシュの帯には、赤が他の成分よりはっきり強い画素がある。
        Assert.True(
            Reddest(crashBandTop, crashBandTop + style.AbsenceBandHeight) > 40,
            "クラッシュの帯には赤い文字があるはず");

        // 「対象外のインスタンスへ移動」だけの帯にはない。
        Assert.True(
            Reddest(absenceBandTop, absenceBandTop + style.AbsenceBandHeight) < 20,
            "クラッシュのない帯は赤くしない");

        // 行の中身（時刻を含む）も赤くしない。
        Assert.True(Reddest(style.ViewportTop, crashBandTop) < 20, "行の時刻は赤くしない");
    }

    /// <summary>
    /// クラッシュの帯の上下も端まで線を伸ばす。前の行から切り離して見せるため
    /// （「対象外のインスタンスへ移動」の帯と同じ扱い）。
    /// </summary>
    [Fact]
    public void クラッシュの帯の上下も端まで線を伸ばす()
    {
        var now = new DateTime(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        // 1行目→2行目は帯なし、2行目（クラッシュ）→3行目にクラッシュの帯。
        var rows = RowFormatter.Build(
            [Record(now, 90, 80, "111"), Record(now, 75, 70, "222", crashed: true), Record(now, 65, null, "333")],
            currentEventId: null,
            Time,
            now);

        Assert.True(rows[2].CrashedBefore);
        Assert.False(rows[2].ExcludedBefore);

        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);
        var pixels = renderer.GetPixels();

        var background = pixels[((((style.ViewportTop + 20) * style.Width) + 2) * 4) + 1];
        bool Painted(int x, int y) => pixels[(((y * style.Width) + x) * 4) + 1] > background + 15;

        var bandTop = style.ViewportTop + (style.RowHeight * 2);

        Assert.True(Painted(0, bandTop - 1), "帯の上の線は左端まで伸びるはず");
        Assert.True(Painted(style.Width - 1, bandTop - 1), "帯の上の線は右端まで伸びるはず");

        var bandBottom = bandTop + style.AbsenceBandHeight - 1;

        Assert.True(Painted(0, bandBottom), "帯の下にも線を出すはず");
        Assert.True(Painted(style.Width - 1, bandBottom), "帯の下の線は右端まで伸びるはず");

        // それより前の区切りは今までどおり短いまま。
        Assert.False(Painted(2, style.ViewportTop + style.RowHeight - 1));
    }
}
