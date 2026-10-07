using System.Drawing;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 見出しの残り時間の数字の強調（2026-10-05のユーザー指定→実装メモ5.130）。
/// 「延長」を押したら光って1秒で戻り、残り3分以下の間は3.5秒周期で通常の色と赤みがかった色を行き来する。
/// </summary>
public class CountdownEmphasisTests
{
    private static readonly PanelStyle Style = new();

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    // ---------------------------------------------------------------- 強さ

    [Fact]
    public void 発光は押した瞬間がいちばん強く_1秒で消える()
    {
        Assert.Equal(1f, CountdownEmphasis.Glow(S(0)));
        Assert.Equal(0f, CountdownEmphasis.Glow(S(1)));
        Assert.Equal(0f, CountdownEmphasis.Glow(S(10)));
        Assert.Equal(0f, CountdownEmphasis.Glow(null));
        Assert.Equal(0f, CountdownEmphasis.Glow(S(-0.1)));

        // 途中は増えずに減っていき、終わり際にはほとんど消えている。
        var previous = 1f;

        for (var t = 0.0; t < 1.0; t += 0.033)
        {
            var glow = CountdownEmphasis.Glow(S(t));
            Assert.True(glow <= previous, $"{t}: {previous} → {glow}");
            previous = glow;
        }

        Assert.True(CountdownEmphasis.Glow(S(0.93)) < 0.05f);
    }

    [Fact]
    public void 警告は残り3分を切ってから3_5秒周期で行き来する()
    {
        var from = CountdownEmphasis.WarningFrom;

        // 3分を超えている間は出さない。切った瞬間は通常の色なので、切り替わりで色が跳ばない。
        Assert.Equal(0f, CountdownEmphasis.Warning(from + S(1), countingDown: true));
        Assert.Equal(0f, CountdownEmphasis.Warning(from, countingDown: true));

        // 周期の半分（1.75秒後）がいちばん赤く、1周（3.5秒後）で戻る。
        var period = CountdownEmphasis.WarningPeriod;
        Assert.Equal(TimeSpan.FromSeconds(3.5), period);
        Assert.Equal(1f, CountdownEmphasis.Warning(from - (period / 2), countingDown: true), 4);
        Assert.Equal(0f, CountdownEmphasis.Warning(from - period, countingDown: true), 4);
        Assert.Equal(0.5f, CountdownEmphasis.Warning(from - (period / 4), countingDown: true), 4);
        Assert.Equal(1f, CountdownEmphasis.Warning(from - (period * 10) - (period / 2), countingDown: true), 3);

        // 1秒に30回ほど描くときの1回の変化は小さい（滑らかに変わる）。
        for (var t = 0.0; t < 4.0; t += 0.033)
        {
            var step = MathF.Abs(CountdownEmphasis.Warning(from - S(t + 0.033), true) - CountdownEmphasis.Warning(from - S(t), true));
            Assert.True(step <= 0.1f, $"{t}: {step}");
        }
    }

    /// <summary>
    /// 主ループを、毎フレームの間隔が揺れる状態で回し、<see cref="CountdownEmphasisPacer"/> が出した強さを記録する。
    /// 返すのは、出し直した時刻と、そのとき出した強さ・そのフレームの時刻から求めた強さ。
    /// </summary>
    private static List<(double Time, float Shown, float Exact)> RunLoop(double fps, double jitter, Func<double, float> value, double duration, int seed = 1)
    {
        var random = new Random(seed);
        var pacer = new CountdownEmphasisPacer();
        var updates = new List<(double, float, float)>();
        var previous = float.NaN;

        for (var t = 0.0; t < duration; t += (1.0 / fps) * (1.0 + ((random.NextDouble() * 2.0) - 1.0) * jitter))
        {
            var exact = value(t);
            var (shown, _) = pacer.Next(S(t), exact, 0f);

            if (shown != previous)
                updates.Add((t, shown, exact));

            previous = shown;
        }

        return updates;
    }

    /// <summary>
    /// 主ループの回り方が上下しても（VRChat の負荷）、描き直しは約45回/秒までで、決まった間隔で止まったり刻みが飛んだりしない
    /// （2026-10-06のユーザー指定→実装メモ5.133）。5.132 の「時計の上の刻み」では、50回/秒の主ループで0.2秒ごとに1コマ止まっていた。
    /// </summary>
    [Theory]
    [InlineData(90.0)]
    [InlineData(72.0)]
    [InlineData(60.0)]
    [InlineData(50.0)]
    [InlineData(40.0)]
    [InlineData(30.0)]
    public void 主ループの回り方が上下しても_描き直しは45回毎秒までで_止まらない(double fps)
    {
        var from = CountdownEmphasis.WarningFrom;
        var updates = RunLoop(fps, jitter: 0.15, t => CountdownEmphasis.Warning(from - S(t), countingDown: true), duration: 7.0);

        // 出す強さは、いつもそのフレームの時刻から求めたもの（丸めない・古い刻みを出さない）。
        Assert.All(updates, u => Assert.Equal(u.Exact, u.Shown));

        var gaps = updates.Zip(updates.Skip(1), (a, b) => b.Time - a.Time).ToList();
        var frame = 1.0 / fps;

        // 間隔は、主ループの1フレームと1/45秒の長いほう（＋揺れ）を超えない。止まるコマがない。
        Assert.True(gaps.Max() <= Math.Max(frame, CountdownEmphasisPacer.Interval.TotalSeconds) * 1.15 + frame * 1.15, $"{fps}: 最大 {gaps.Max() * 1000:0.0}ms");

        // 回数は約45回/秒まで（主ループが遅ければその回数）。
        var rate = updates.Count / 7.0;
        // 45回/秒をわずかに超える主ループ（50回/秒）では、期限の手前の幅（Slack）で毎フレーム出すことがある。
        Assert.InRange(rate, Math.Min(fps, CountdownEmphasis.FrameRate) * 0.8, CountdownEmphasis.FrameRate * 1.15);
    }

    [Fact]
    public void 動き始めと止まったときは_間隔を待たずにすぐ出す()
    {
        var pacer = new CountdownEmphasisPacer();

        Assert.Equal((1f, 0f), pacer.Next(S(0), 1f, 0f));

        // 間隔の途中は前の値のまま。
        Assert.Equal((1f, 0f), pacer.Next(S(0.010), 0.9f, 0f));
        Assert.Equal((0.8f, 0f), pacer.Next(S(0.022), 0.8f, 0f));

        // 消えた（0になった）ら、間隔の途中でもすぐ出す。
        Assert.Equal((0f, 0f), pacer.Next(S(0.025), 0f, 0f));

        // また動き始めたら、すぐ出す。
        Assert.Equal((0f, 0.1f), pacer.Next(S(0.030), 0f, 0.1f));
    }

    [Fact]
    public void 数えていない間は残り3分以下でも警告しない()
    {
        Assert.Equal(0f, CountdownEmphasis.Warning(S(61), countingDown: false));
        Assert.False(CountdownEmphasis.Animating(null, S(61), countingDown: false));

        Assert.True(CountdownEmphasis.Animating(null, S(61), countingDown: true));
        Assert.True(CountdownEmphasis.Animating(S(0.2), TimeSpan.FromMinutes(50), countingDown: true));
        Assert.False(CountdownEmphasis.Animating(S(1.1), TimeSpan.FromMinutes(50), countingDown: true));
    }

    // ---------------------------------------------------------------- 手首のパネル

    /// <summary>数字の矩形を少し広げた範囲（光が広がるところまで）の、画素ごとの (R, G, B, A) の合計。</summary>
    private static (long R, long G, long B, long A) Sum(PanelDecorations decorations)
    {
        using var renderer = new PanelRenderer(Style);
        renderer.RenderRows(renderer.Measure(SampleRows.Build()));
        renderer.Compose(0f, decorations);

        var pixels = renderer.GetPixels();
        var rect = Rectangle.Intersect(
            Rectangle.Ceiling(RectangleF.Inflate(renderer.CountdownRectFor(decorations), 6f, 4f)),
            new Rectangle(0, 0, Style.Width, Style.HeaderHeight));

        long r = 0, g = 0, b = 0, a = 0;

        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var i = ((y * Style.Width) + x) * 4;
                (b, g, r, a) = (b + pixels[i], g + pixels[i + 1], r + pixels[i + 2], a + pixels[i + 3]);
            }
        }

        return (r, g, b, a);
    }

    [Fact]
    public void 警告の色は赤みがかり_発光は数字の周りまで明るくする()
    {
        var plain = new PanelDecorations { Countdown = "02:59" };

        var normal = Sum(plain);
        var warning = Sum(plain with { CountdownWarning = 1f });
        var glow = Sum(plain with { CountdownGlow = 1f });

        // 赤みが増し、青緑は減る。
        Assert.True(warning.R - warning.G > normal.R - normal.G + 1000, $"{normal} → {warning}");

        // 光は数字の外まで広がるので、範囲の不透明度も明るさも増える。
        Assert.True(glow.A > normal.A + 1000, $"{normal} → {glow}");
        Assert.True(glow.G + glow.B > normal.G + normal.B + 1000, $"{normal} → {glow}");
    }

    [Fact]
    public void 強調がなければ_これまでと同じ絵になる()
    {
        var plain = new PanelDecorations { Countdown = "59:12" };
        Assert.Equal(Sum(plain), Sum(plain with { CountdownGlow = 0f, CountdownWarning = 0f }));
    }

    // ---------------------------------------------------------------- デスクトップのウィンドウ

    private static DesktopView Window(Func<TimeSpan> clock, AppSettings? settings = null)
    {
        var view = new DesktopView(new PanelStyle(), DesktopSettings.From(settings ?? new AppSettings()) with { LaunchWithSteamVr = false }, _ => { })
        {
            BlinkClock = static () => TimeSpan.Zero,
            EmphasisClock = clock,
        };

        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetRows(SampleRows.Build());
        view.SetCountdown("59:12", TimeSpan.FromSeconds(3552));
        view.SetStatus(DesktopStatus.Initial);
        return view;
    }

    /// <summary>2枚の絵の、<paramref name="area"/> の中で色が違う画素の数。</summary>
    private static int Differences(Bitmap a, Bitmap b, Rectangle area)
    {
        var count = 0;

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    count++;
            }
        }

        return count;
    }

    [Fact]
    public void ウィンドウは延長で光り_見出しだけを描き直して_1秒で戻す()
    {
        var now = TimeSpan.Zero;
        using var view = Window(() => now);

        using var before = view.RenderToBitmap();
        Assert.False(view.CountdownAnimating);

        view.FlashCountdown();
        Assert.True(view.Dirty);
        Assert.True(view.CountdownAnimating);

        using var bitmap = view.RenderToBitmap();
        var digits = Rectangle.Ceiling(view.CountdownScreenRect());
        Assert.True(Differences(before, bitmap, digits) > 50);

        // 途中は見出しの帯だけを描き直す。描き直した絵は、全体を描いたときと同じ。
        now = S(0.5);

        using (var g = Graphics.FromImage(bitmap))
        {
            var area = view.RenderCountdownEmphasis(g);
            Assert.NotNull(area);
            Assert.False(area.Value.IsEmpty);
            Assert.True(area.Value.Contains(digits));
            Assert.True(area.Value.Bottom <= view.PanelRect.Y + (Style.HeaderHeight * (view.PanelRect.Width / Style.Width)) + 1);
        }

        using (var full = view.RenderToBitmap())
            Assert.Equal(0, Differences(full, bitmap, new Rectangle(Point.Empty, bitmap.Size)));

        // 強さが変わっていなければ描き直さない。
        using (var g = Graphics.FromImage(bitmap))
            Assert.Equal(Rectangle.Empty, view.RenderCountdownEmphasis(g));

        // 1秒で消える。消えた姿を描き終えたら止まる。
        now = S(1.1);
        Assert.True(view.CountdownAnimating);

        using (var g = Graphics.FromImage(bitmap))
            Assert.False(view.RenderCountdownEmphasis(g)!.Value.IsEmpty);

        Assert.False(view.CountdownAnimating);
        Assert.Equal(0, Differences(before, bitmap, new Rectangle(Point.Empty, bitmap.Size)));
    }

    [Fact]
    public void ウィンドウは残り3分以下の間_届いた残り時間を自分の時計で進めて色を変える()
    {
        var now = TimeSpan.Zero;
        using var view = Window(() => now);

        // 3分を切った瞬間に届いた。この時点は通常の色。
        view.SetCountdown("03:00", CountdownEmphasis.WarningFrom);
        using var bitmap = view.RenderToBitmap();
        using var start = view.RenderToBitmap();
        Assert.True(view.CountdownAnimating);

        // 次の値が届く前（0.9秒後）でも、赤みが増している。
        now = S(0.9);

        using (var g = Graphics.FromImage(bitmap))
            Assert.False(view.RenderCountdownEmphasis(g)!.Value.IsEmpty);

        var digits = Rectangle.Ceiling(view.CountdownScreenRect());
        Assert.True(Differences(start, bitmap, digits) > 50);

        // 止まったら（対象インスタンスへ戻ったなど）行き来をやめる。
        view.SetCountdownStopped(true);
        view.RenderToBitmap().Dispose();
        Assert.False(view.CountdownAnimating);
    }

    [Fact]
    public void ウィンドウは自動リセットを無効にしている間は警告しない()
    {
        var now = TimeSpan.Zero;
        using var view = Window(() => now, new AppSettings { AutoResetEnabled = false });

        view.SetCountdown("01:00", S(60));
        view.RenderToBitmap().Dispose();
        Assert.False(view.CountdownAnimating);
    }

    // ---------------------------------------------------------------- 知らせ方

    [Fact]
    public void 延長の発光はVR側とウィンドウの両方へ渡す()
    {
        var vr = new RecordingPanelTarget();
        var desktop = new RecordingPanelTarget();
        var targets = new PanelTargets { Vr = vr, Desktop = desktop };

        ((IPanelTarget)targets).FlashCountdown();

        Assert.Equal(1, vr.Flashes);
        Assert.Equal(1, desktop.Flashes);
    }
}
