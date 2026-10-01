using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 2026-09-13に報告された「位置調整とスクロール中にUIがちらつく」への対策。
/// 一瞬の追跡欠落・命中の取りこぼし・ファイル読み取りの失敗で
/// 表示条件が落ちないことを確かめる。
/// </summary>
public class FlickerTests
{
    private static readonly DateTime SessionStart = new(2026, 9, 13, 12, 0, 0);

    /// <summary>
    /// 2026-09-19の実機確認: 「カウントリセット」を押すたびに1回ちらついていた。
    /// 押しても行の内容は変わらないのに世代番号が進み、同じ絵を描き直していたため。
    /// </summary>
    [Fact]
    public void カウントリセットでは行が変わらないので世代を進めない()
    {
        using var dir = new TempLogDirectory();
        var earlier = SessionStart.AddHours(-3);
        dir.WriteSession(earlier, LogText.Noise(earlier) + LogText.Quit(earlier.AddMinutes(30)));

        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();

        var leftAt = SessionStart.AddMinutes(3);
        TempLogDirectory.Append(file, LogText.LeftRoom(leftAt));
        harness.SetNow(leftAt.AddMinutes(1));
        harness.Engine.Update();

        var before = harness.Snapshot();
        harness.Engine.ResetRetention();
        var after = harness.Snapshot();

        // 期限だけが動き、世代番号と行はそのまま（＝下絵を描き直さない）。
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.History, after.History);
        Assert.NotEqual(before.RetentionDeadlineUtc, after.RetentionDeadlineUtc);
    }

    /// <summary>
    /// 世代番号だけが進んで行が同じ場合も描き直さない（他の経路からの世代更新への備え）。
    /// 内容が変われば従来どおり描き直す。
    /// </summary>
    [Fact]
    public void 同じ行なら世代が進んでも描き直さない()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);
        var scroll = new Core.Scrolling.ScrollController();
        var settings = new AppSettings();

        using var session = new SteamVrSession(NullDiagnostics.Instance);
        var input = new SteamVrInput(NullDiagnostics.Instance);
        var controller = new OverlayController(session, input, settings, renderer, scroll, NullDiagnostics.Instance);

        controller.SetContent(Rows("111"));
        Assert.Equal(1, controller.ContentUpdates);

        // 世代だけが進んで中身は同じ。描き直さない。
        controller.SetContent(Rows("111"));
        Assert.Equal(1, controller.ContentUpdates);

        // 中身が変われば従来どおり描き直す。
        controller.SetContent(Rows("222"));
        Assert.Equal(2, controller.ContentUpdates);
    }

    private static List<Core.Presentation.DisplayRow> Rows(string instanceId, float stayFraction = 0f)
        => [new Core.Presentation.DisplayRow(
            EventId: "e-" + instanceId,
            InstanceId: instanceId,
            TimeText: "12:00",
            OrdinalText: "（1回目）",
            WorldName: "確認用ワールド",
            TypeText: "Public",
            IsCurrent: true,
            StayFraction: stayFraction)];

    /// <summary>
    /// 滞在時間の棒が伸びたときは、世代が同じでも描き直す（→実装メモ5.28）。
    /// 棒は1分単位に丸めてあるので、これが起きるのは最大でも1分に1回。
    /// </summary>
    [Fact]
    public void 滞在時間の棒が伸びたら世代が同じでも描き直す()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);
        var scroll = new Core.Scrolling.ScrollController();
        var settings = new AppSettings();

        using var session = new SteamVrSession(NullDiagnostics.Instance);
        var input = new SteamVrInput(NullDiagnostics.Instance);
        var controller = new OverlayController(session, input, settings, renderer, scroll, NullDiagnostics.Instance);

        controller.SetContent(Rows("111", stayFraction: 0.10f));
        Assert.Equal(1, controller.ContentUpdates);

        // 同じ長さのまま渡し直しても描き直さない。
        controller.SetContent(Rows("111", stayFraction: 0.10f));
        Assert.Equal(1, controller.ContentUpdates);

        // 1分ぶん伸びたら、世代が同じでも描き直す。
        controller.SetContent(Rows("111", stayFraction: 0.12f));
        Assert.Equal(2, controller.ContentUpdates);
    }

    [Fact]
    public void 猶予の内側なら条件が成立しているとみなす()
    {
        var timer = new GraceTimer(TimeSpan.FromMilliseconds(500));
        var now = TimeSpan.FromSeconds(10);

        // 一度も成立していなければ保持しない。
        Assert.False(timer.IsHolding(now));

        timer.Signal(now);
        Assert.True(timer.IsHolding(now));
        Assert.True(timer.IsHolding(now + TimeSpan.FromMilliseconds(499)));
        Assert.True(timer.IsHolding(now + TimeSpan.FromMilliseconds(500)));
        Assert.False(timer.IsHolding(now + TimeSpan.FromMilliseconds(501)));
    }

    [Fact]
    public void 一度も動いていないタイマーでも経過時間の計算で落ちない()
    {
        // 2026-09-14の不具合: 最後の時刻に TimeSpan.MinValue を置いていたため、
        // 初回の「now - 最後の時刻」で OverflowException になり、起動直後に停止していた。
        var interval = new IntervalTimer(TimeSpan.FromMilliseconds(33));
        var grace = new GraceTimer(TimeSpan.FromMilliseconds(500));

        foreach (var now in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(30) })
        {
            Assert.True(interval.IsDue(now));   // 一度も動いていなければ常に「時間が来ている」
            Assert.False(grace.IsHolding(now)); // 一度も成立していなければ保持しない
        }
    }

    [Fact]
    public void 間隔タイマーは指定した間隔で1回だけ通す()
    {
        var timer = new IntervalTimer(TimeSpan.FromMilliseconds(33));
        var now = TimeSpan.FromSeconds(3);

        Assert.True(timer.TryTick(now));                                   // 初回は通る
        Assert.False(timer.TryTick(now));                                  // 同じ時刻では通らない
        Assert.False(timer.TryTick(now + TimeSpan.FromMilliseconds(32)));
        Assert.True(timer.TryTick(now + TimeSpan.FromMilliseconds(33)));   // 間隔ちょうどで通る

        timer.Reset();
        Assert.True(timer.TryTick(now));
    }

    [Fact]
    public void 追跡が数フレーム欠けても保持し続ける()
    {
        // 90Hzで3フレーム（約33ms）欠けても、猶予500msの内側なので消えない。
        var timer = new GraceTimer(TimeSpan.FromMilliseconds(500));
        var now = TimeSpan.Zero;
        var frame = TimeSpan.FromMilliseconds(1000.0 / 90.0);

        for (var i = 0; i < 10; i++)
        {
            timer.Signal(now);
            now += frame;
        }

        // 3フレーム欠落
        now += frame * 3;
        Assert.True(timer.IsHolding(now));

        // 1秒欠け続けたら保持をやめる。
        now += TimeSpan.FromSeconds(1);
        Assert.False(timer.IsHolding(now));
    }

    [Fact]
    public void 対象が変わったら猶予を打ち切る()
    {
        var timer = new GraceTimer(TimeSpan.FromMilliseconds(200));
        var now = TimeSpan.FromSeconds(5);

        timer.Signal(now);
        timer.Reset();

        Assert.False(timer.IsHolding(now));
    }

    [Fact]
    public void ログを一時的に開けなくても現在地と表示条件を失わない()
    {
        using var dir = new TempLogDirectory();
        var file = dir.WriteSession(
            SessionStart,
            LogText.Visit(SessionStart.AddMinutes(1), Loc.GroupPublic("111"), "A")
            + LogText.WorldsTabShown(SessionStart.AddMinutes(1).AddSeconds(30)));

        using var harness = new EngineHarness(dir.Path, SessionStart.AddMinutes(2), Process(SessionStart));
        harness.Engine.Initialize();
        Assert.True(harness.Snapshot().ContentReady);

        // ほかのプロセスが書き込み共有を拒んで開いている状態を作る。
        // このあいだ、先頭バイト列のハッシュは読めない。
        using (var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            for (var i = 0; i < 3; i++)
            {
                harness.SetNow(SessionStart.AddMinutes(2).AddSeconds(3 * (i + 1)));
                harness.Engine.Update();

                var blocked = harness.Snapshot();
                Assert.True(blocked.ContentReady, "一時的に読めないだけでパネルを消してはいけない");
                Assert.Equal(PresenceState.InTarget, blocked.Presence);
                Assert.Equal(Core.LogHealth.Ok, blocked.Health);
            }
        }

        // 解放後も引き続き追従できる。
        TempLogDirectory.Append(file, LogText.Move(SessionStart.AddMinutes(3), Loc.GroupPublic("222"), "B"));
        harness.SetNow(SessionStart.AddMinutes(4));
        harness.Engine.Update();

        Assert.Equal(["111", "222"], harness.Snapshot().History.Select(v => v.InstanceId));
    }

    /// <summary>
    /// 画素は BGRA のまま取り出せる（D3D11 の B8G8R8A8_UNORM と同じ並び）。
    /// 2026-09-23まではRGBAへの並べ替えも持っていたが、SetOverlayRaw を使わなくなって不要になった。
    /// </summary>
    [Fact]
    public void 画素はBGRAで今の高さぶんだけ取り出せる()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = SampleRows.Build();
        var layouts = renderer.Measure(rows);
        renderer.Render(layouts, 0f);

        Assert.Equal(style.Width * renderer.Height * 4, renderer.GetPixels().Length);
    }

    /// <summary>画素の配列が足りないときは、ポインターで書き始める前に止める（はみ出して書くとヒープを壊す）。</summary>
    [Fact]
    public void 画素の配列が足りなければ写さずに止める()
    {
        using var bitmap = new System.Drawing.Bitmap(8, 8);

        Assert.Throws<ArgumentException>(() => PanelRenderer.CopyPixels(bitmap, new byte[(8 * 8 * 4) - 1]));
        Assert.Equal(8 * 8 * 4, PanelRenderer.CopyPixels(bitmap, new byte[8 * 8 * 4]).Length);
    }
}
