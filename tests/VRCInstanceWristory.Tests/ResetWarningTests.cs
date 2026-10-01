using System.Numerics;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 履歴リセットの予告のアイコン（2026-09-28のユーザー指定→実装メモ5.87。設定は2026-09-29→5.89）。
/// 残りが表示タイミング（既定3分）を切った瞬間に1回だけ知らせ、2秒間隔で点滅回数（既定5回）だけ点滅して消える。
/// </summary>
public class ResetWarningTests
{
    private static readonly TimeSpan Min = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(5);

    [Fact]
    public void 残りが表示タイミングを切った瞬間に1回だけ知らせる()
    {
        var trigger = new ResetWarningTrigger();

        Assert.False(trigger.Observe(true, 6 * Min, true, Lead));
        Assert.False(trigger.Observe(true, TimeSpan.FromSeconds(301), true, Lead));
        Assert.True(trigger.Observe(true, TimeSpan.FromSeconds(299.9), true, Lead));

        // 切ったあとは知らせ続けない。
        Assert.False(trigger.Observe(true, 4 * Min, true, Lead));
        Assert.False(trigger.Observe(true, TimeSpan.FromSeconds(1), true, Lead));
    }

    [Fact]
    public void 表示タイミングの既定は3分()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), ResetWarningTrigger.DefaultLead);
        Assert.Equal(3, new AppSettings().ResetWarningLeadMinutes);

        var trigger = new ResetWarningTrigger();
        Assert.False(trigger.Observe(true, 4 * Min, true, ResetWarningTrigger.DefaultLead));
        Assert.False(trigger.Observe(true, TimeSpan.FromSeconds(181), true, ResetWarningTrigger.DefaultLead));
        Assert.True(trigger.Observe(true, TimeSpan.FromSeconds(179), true, ResetWarningTrigger.DefaultLead));
    }

    [Fact]
    public void 延長で表示タイミングより上へ戻ったら次に切ったときもう一度知らせる()
    {
        var trigger = new ResetWarningTrigger();

        trigger.Observe(true, 6 * Min, true, Lead);
        Assert.True(trigger.Observe(true, 4 * Min, true, Lead));

        // 「延長」で60分へ戻る。
        Assert.False(trigger.Observe(true, 60 * Min, true, Lead));
        Assert.True(trigger.Observe(true, 5 * Min, true, Lead));
    }

    [Fact]
    public void 数え始めた時点で表示タイミング以下なら知らせない()
    {
        // 保持時間5分：停止中の 5:00 から数え始める。
        var trigger = new ResetWarningTrigger();
        Assert.False(trigger.Observe(false, 5 * Min, true, Lead));
        Assert.False(trigger.Observe(true, TimeSpan.FromSeconds(299), true, Lead));

        // 起動した時点で残り3分。
        var restarted = new ResetWarningTrigger();
        Assert.False(restarted.Observe(true, 3 * Min, true, Lead));
    }

    [Fact]
    public void 停止中_行がない_一気に0になったときは知らせない()
    {
        // 表示タイミングを切る前に滞在し直した（停止中）。
        var trigger = new ResetWarningTrigger();
        trigger.Observe(true, 6 * Min, true, Lead);
        Assert.False(trigger.Observe(false, 60 * Min, true, Lead));
        Assert.False(trigger.Observe(true, 4 * Min, true, Lead));

        // 消える行がない（VRChatが動いていない）ときは、またいでも知らせない。
        var empty = new ResetWarningTrigger();
        empty.Observe(true, 6 * Min, true, Lead);
        Assert.False(empty.Observe(true, 4 * Min, false, Lead));

        // スリープ明けで残りが一気に0。
        var slept = new ResetWarningTrigger();
        slept.Observe(true, 30 * Min, true, Lead);
        Assert.False(slept.Observe(true, TimeSpan.Zero, true, Lead));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    public void 点滅は2秒間隔で点滅回数だけして消える(int count)
    {
        var blink = new ResetWarningBlink();
        var start = TimeSpan.FromSeconds(100);
        blink.Start(start, count);

        var peaks = 0;
        var wasOn = false;

        for (var ms = 0; ms < (count * 2000) + 2000; ms += 10)
        {
            var alpha = blink.Alpha(start + TimeSpan.FromMilliseconds(ms));
            Assert.InRange(alpha, 0f, 1f);

            var on = alpha > 0.5f;
            if (on && !wasOn)
                peaks++;

            wasOn = on;
        }

        Assert.Equal(count, peaks);

        // 各周期の真ん中では出ていて、後半は消えている。点滅の間隔は2秒。
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(1f, blink.Alpha(start + TimeSpan.FromSeconds((2 * i) + 0.6)));
            Assert.Equal(0f, blink.Alpha(start + TimeSpan.FromSeconds((2 * i) + 1.6)));
        }

        // 2秒×回数で終わる。
        Assert.True(blink.Active(start + TimeSpan.FromSeconds((2 * count) - 0.1)));
        Assert.False(blink.Active(start + TimeSpan.FromSeconds(2 * count)));
        Assert.Equal(ResetWarningBlink.DurationFor(count), blink.Duration);
    }

    [Fact]
    public void 現れるときと消えるときは短く渡す()
    {
        var blink = new ResetWarningBlink();
        blink.Start(TimeSpan.Zero);

        // 始まった瞬間は0で、0.15秒で出しきる（オンとオフを一瞬で切り替えない→実装メモ5.16）。
        Assert.Equal(0f, blink.Alpha(TimeSpan.Zero));
        Assert.InRange(blink.Alpha(TimeSpan.FromSeconds(0.075)), 0.4f, 0.6f);
        Assert.Equal(1f, blink.Alpha(TimeSpan.FromSeconds(0.15)));
        Assert.InRange(blink.Alpha(TimeSpan.FromSeconds(1.125)), 0.4f, 0.6f);
    }

    // ---------------------------------------------------------------- 表示位置と大きさ

    [Fact]
    public void 表示位置はマイクの周りの8方向で既定は左下()
    {
        var mic = ResetWarningOverlay.MicCenter;
        Assert.Equal(ResetWarningPosition.BottomLeft, ResetWarningPositions.Default);
        Assert.Equal(ResetWarningPosition.BottomLeft, new AppSettings().WarningPosition);

        foreach (var position in ResetWarningPositions.Order)
        {
            var offset = ResetWarningOverlay.OffsetFor(position, 1f);
            var (x, y) = ResetWarningPositions.Direction(position);

            Assert.Equal(mic.Z, offset.Z);
            Assert.Equal(Math.Sign(x), Math.Sign(MathF.Round(offset.X - mic.X, 4)));
            Assert.Equal(Math.Sign(y), Math.Sign(MathF.Round(offset.Y - mic.Y, 4)));
        }

        // 左は、マイクと同じ高さで、重ならない（中心どうしの間は、2つの幅の半分の和に近い）。
        var left = ResetWarningOverlay.OffsetFor(ResetWarningPosition.Left, 1f);
        Assert.Equal(mic.Y, left.Y);
        Assert.InRange(mic.X - left.X, 0.04f, 0.06f);
    }

    [Fact]
    public void 表示位置は左下から巡って下の次は左下へ戻る()
    {
        var names = ResetWarningPositions.Order.Select(ResetWarningPositions.DisplayName).ToArray();
        Assert.Equal(["左下", "左", "左上", "上", "右上", "右", "右下", "下"], names);

        var position = ResetWarningPosition.BottomLeft;

        for (var i = 0; i < ResetWarningPositions.Order.Length; i++)
            position = ResetWarningPositions.Next(position, +1);

        Assert.Equal(ResetWarningPosition.BottomLeft, position);
        Assert.Equal(ResetWarningPosition.Bottom, ResetWarningPositions.Next(ResetWarningPosition.BottomLeft, -1));
    }

    [Fact]
    public void 大きくすると図の端からの間を保ったまま外へずれる()
    {
        var mic = ResetWarningOverlay.MicCenter;
        var small = ResetWarningOverlay.OffsetFor(ResetWarningPosition.Left, 0.6f);
        var large = ResetWarningOverlay.OffsetFor(ResetWarningPosition.Left, 1.4f);

        // 半分の幅の差だけ離れる。
        var expected = ResetWarningOverlay.SizeMeters * ResetWarningOverlay.GlyphRatio * (1.4f - 0.6f) / 2f;
        Assert.Equal(expected, (mic.X - large.X) - (mic.X - small.X), 4);
    }

    [Fact]
    public void アイコンは目の方へ向ける()
    {
        foreach (var position in ResetWarningPositions.Order)
        {
            // 置くのは奥行き1.15mへ広げた位置（→実装メモ5.94）。
            var offset = ResetWarningOverlay.PlacementFor(position, 1f);
            var transform = ResetWarningOverlay.Transform(position, 1f);
            Assert.Equal(offset, Math3d.Translation(transform));

            // 面の表（+Z）が頭の原点を向き、上は頭の上のまま。
            var toEye = Vector3.Normalize(-offset);
            Assert.True(Vector3.Dot(Math3d.Back(transform), toEye) > 0.999f);
            Assert.True(Math3d.Up(transform).Y > 0.9f);
            Assert.True(MathF.Abs(Math3d.Right(transform).Y) < 1e-4f);
        }
    }

    [Fact]
    public void 設定で選べる値()
    {
        Assert.Equal([0.6f, 0.8f, 1f, 1.2f, 1.4f], ResetWarningOptions.Scales);
        Assert.Equal(1f, ResetWarningOptions.Scales[ResetWarningOptions.Scales.Length / 2]);
        Assert.Equal([0.5f, 0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 0.8f, 0.85f, 0.9f, 0.92f, 0.94f, 0.96f, 0.98f, 1f], ResetWarningOptions.Opacities);
        Assert.Equal([1, 3, 5, 10, 30], ResetWarningOptions.BlinkCounts);
        Assert.Equal([1, 3, 5], ResetWarningOptions.LeadMinutes);

        // 「リセット予告アイコンを表示する」は既定でオフ（2026-09-29のユーザー指定→実装メモ5.90）。
        var defaults = new AppSettings();
        Assert.False(defaults.ResetWarningEnabled);
        Assert.False(Desktop.DesktopSettings.From(defaults).ResetWarningEnabled);
        Assert.Equal(1f, defaults.ResetWarningScale);
        Assert.Equal(1f, defaults.ResetWarningOpacity);
        Assert.Equal(5, defaults.ResetWarningBlinkCount);
        Assert.True(defaults.ResetWarningReshowAfterAfk);
        Assert.False(defaults.PauseCountdownWhileAfk);
    }

    [Fact]
    public void アイコンの絵は枠線なしの黄色一色で縁を透明に残す()
    {
        using var bitmap = ResetWarningOverlay.Render(ResetWarningOverlay.TextureSize);

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A);

        // 輪（左端の中ほど）と、3本の線（真ん中の線の中ほど・いちばん下の線）が描いてある。どれも黄色。
        var size = bitmap.Width;
        var yellow = ResetWarningOverlay.Color.ToArgb();
        Assert.Contains(Enumerable.Range((int)(size * 0.1f), (int)(size * 0.1f)), x => bitmap.GetPixel(x, size / 2).ToArgb() == yellow);
        Assert.Equal(yellow, bitmap.GetPixel((int)(size * 0.55f), (int)(size * 0.5f)).ToArgb());
        Assert.Equal(yellow, bitmap.GetPixel((int)(size * 0.55f), (int)(size * 0.61f)).ToArgb());

        // 縁取りなし：描いてある画素は、端の半透明（アンチエイリアス）を含めてすべて黄色の色味（影の黒を含まない）。
        // ほとんど透明な端の画素は GDI+ の丸めで数段ずれるので、差 40 までを同じ色とみなす（影の黒なら 200 以上ずれる）。
        static bool Near(int a, int b) => Math.Abs(a - b) <= 40;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.A == 0)
                    continue;

                Assert.True(Near(p.R, ResetWarningOverlay.Color.R) && Near(p.G, ResetWarningOverlay.Color.G) && Near(p.B, ResetWarningOverlay.Color.B), $"({x},{y}) {p}");
            }
        }
    }

    // ---------------------------------------------------------------- 見逃したときの出し直し

    private static readonly DateTime T0 = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Blink = ResetWarningBlink.DurationFor(5);

    [Fact]
    public void AFKの間に表示タイミングが来たら戻ったときに出す()
    {
        var scheduler = new ResetWarningScheduler();

        Assert.False(scheduler.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: true, Blink));

        // AFK の間に5分を切った。出さずに覚えておく。
        Assert.False(scheduler.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: true, reshow: true, Blink));
        Assert.False(scheduler.Observe(T0.AddMinutes(2), true, 3.9 * Min, true, Lead, away: true, reshow: true, Blink));

        // 戻ってきた。まだリセットされていないので出す。1回だけ。
        Assert.True(scheduler.Observe(T0.AddMinutes(3), true, 2.9 * Min, true, Lead, away: false, reshow: true, Blink));
        Assert.False(scheduler.Observe(T0.AddMinutes(3.1), true, 2.8 * Min, true, Lead, away: false, reshow: true, Blink));
    }

    [Fact]
    public void 点滅の途中でAFKになったら戻ったときにもう一度出す()
    {
        var scheduler = new ResetWarningScheduler();

        scheduler.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: true, Blink);
        Assert.True(scheduler.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: false, reshow: true, Blink));

        // 点滅の3秒目で HMD を外した。
        Assert.False(scheduler.Observe(T0.AddMinutes(1).AddSeconds(3), true, 4.85 * Min, true, Lead, away: true, reshow: true, Blink));
        Assert.True(scheduler.Observe(T0.AddMinutes(2), true, 3.9 * Min, true, Lead, away: false, reshow: true, Blink));
    }

    [Fact]
    public void 点滅が終わってからAFKになったときは出し直さない()
    {
        var scheduler = new ResetWarningScheduler();

        scheduler.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: true, Blink);
        Assert.True(scheduler.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: false, reshow: true, Blink));

        Assert.False(scheduler.Observe(T0.AddMinutes(1).AddSeconds(30), true, 4.4 * Min, true, Lead, away: true, reshow: true, Blink));
        Assert.False(scheduler.Observe(T0.AddMinutes(2), true, 3.9 * Min, true, Lead, away: false, reshow: true, Blink));
    }

    [Fact]
    public void 戻る前にリセットされた_延長した_停止したら出し直さない()
    {
        // AFK の間にリセットされた（残り0・行がない）。
        var reset = new ResetWarningScheduler();
        reset.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: true, Blink);
        reset.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: true, reshow: true, Blink);
        reset.Observe(T0.AddMinutes(6), true, TimeSpan.Zero, false, Lead, away: true, reshow: true, Blink);
        Assert.False(reset.Observe(T0.AddMinutes(7), true, 60 * Min, false, Lead, away: false, reshow: true, Blink));

        // 延長で表示タイミングより上へ戻った。
        var extended = new ResetWarningScheduler();
        extended.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: true, Blink);
        extended.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: true, reshow: true, Blink);
        Assert.False(extended.Observe(T0.AddMinutes(2), true, 60 * Min, true, Lead, away: false, reshow: true, Blink));

        // 対象のインスタンスへ戻って停止した。
        var stopped = new ResetWarningScheduler();
        stopped.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: true, Blink);
        stopped.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: true, reshow: true, Blink);
        Assert.False(stopped.Observe(T0.AddMinutes(2), false, 60 * Min, true, Lead, away: false, reshow: true, Blink));
    }

    [Fact]
    public void 再表示をオフにするとAFKの間でも出して出し直さない()
    {
        var scheduler = new ResetWarningScheduler();

        scheduler.Observe(T0, true, 6 * Min, true, Lead, away: false, reshow: false, Blink);
        Assert.True(scheduler.Observe(T0.AddMinutes(1), true, 4.9 * Min, true, Lead, away: true, reshow: false, Blink));
        Assert.False(scheduler.Observe(T0.AddMinutes(2), true, 3.9 * Min, true, Lead, away: false, reshow: false, Blink));
    }

    // ---------------------------------------------------------------- PanelPresenter

    private static EngineSnapshot Snapshot(DateTime? deadline, bool autoReset = true, int rows = 3, bool clientRunning = true)
        => new()
        {
            RetentionDeadlineUtc = deadline,
            AutoReset = autoReset,
            ClientRunning = clientRunning,
            Presence = PresenceState.InExcluded,
            Health = LogHealth.Ok,
            History = Enumerable.Range(0, rows).Select(i => new VisitRecord
            {
                EventId = "s1@e" + i,
                SourceSessionId = "s1",
                SuccessByteOffset = i,
                SessionOrder = i,
                LocationKey = "wrld_x:" + i,
                WorldId = "wrld_x",
                InstanceId = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AccessType = Core.Locations.AccessType.Public,
                VisitedAtUtc = new DateTime(2026, 9, 28, 11, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                LeftAtUtc = new DateTime(2026, 9, 28, 11, 30, 0, DateTimeKind.Utc),
            }).ToList(),
            CurrentEventId = null,
            MenuPageOpen = false,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = 1,
        };

    [Fact]
    public void 表示の受け手へは表示タイミングを切ったときに1回だけ渡す()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var clock = new ManualClock(now);
        var target = new RecordingPanelTarget();
        var presenter = new PanelPresenter(target, new LogTimeConverter(TimeZoneInfo.Utc), NullDiagnostics.Instance, clock);
        var deadline = now.AddMinutes(4);

        presenter.Apply(Snapshot(deadline));
        clock.Advance(TimeSpan.FromSeconds(59));
        presenter.Apply(Snapshot(deadline));
        Assert.Equal(0, target.Warnings);

        // 既定の表示タイミングは3分。
        clock.Advance(TimeSpan.FromSeconds(2));
        presenter.Apply(Snapshot(deadline));
        Assert.Equal(1, target.Warnings);

        clock.Advance(TimeSpan.FromMinutes(1));
        presenter.Apply(Snapshot(deadline));
        Assert.Equal(1, target.Warnings);
    }

    [Fact]
    public void 表示タイミングを変えるとその時間で渡す()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var clock = new ManualClock(now);
        var target = new RecordingPanelTarget();
        var presenter = new PanelPresenter(target, new LogTimeConverter(TimeZoneInfo.Utc), NullDiagnostics.Instance, clock)
        {
            ResetWarningLead = TimeSpan.FromMinutes(1),
        };
        var deadline = now.AddMinutes(3);

        presenter.Apply(Snapshot(deadline));
        clock.Advance(TimeSpan.FromMinutes(1.5));
        presenter.Apply(Snapshot(deadline));
        Assert.Equal(0, target.Warnings);

        clock.Advance(TimeSpan.FromMinutes(1));
        presenter.Apply(Snapshot(deadline));
        Assert.Equal(1, target.Warnings);
    }

    [Fact]
    public void 予告をオフ_自動リセットを止めている_行がないときは渡さない()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        foreach (var (enabled, autoReset, rows) in new[] { (false, true, 3), (true, false, 3), (true, true, 0) })
        {
            var clock = new ManualClock(now);
            var target = new RecordingPanelTarget();
            var presenter = new PanelPresenter(target, new LogTimeConverter(TimeZoneInfo.Utc), NullDiagnostics.Instance, clock)
            {
                ResetWarningEnabled = enabled,
            };
            var deadline = now.AddMinutes(4);

            presenter.Apply(Snapshot(deadline, autoReset, rows));
            clock.Advance(TimeSpan.FromMinutes(2));
            presenter.Apply(Snapshot(deadline, autoReset, rows));

            Assert.Equal(0, target.Warnings);
        }
    }

    [Fact]
    public void 予告はVR側だけへ渡す()
    {
        var vr = new RecordingPanelTarget();
        var desktop = new RecordingPanelTarget();
        var targets = new PanelTargets { Vr = vr, Desktop = desktop };

        ((IPanelTarget)targets).ShowResetWarning();

        Assert.Equal(1, vr.Warnings);
        Assert.Equal(0, desktop.Warnings);
    }
}
