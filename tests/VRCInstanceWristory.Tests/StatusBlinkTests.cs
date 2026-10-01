using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 状態の段の点の点滅（赤は1秒周期に2値・黄は5秒周期で穏やかに）と、見出しの「履歴リセット」の色（2026-09-29のユーザー指定→実装メモ5.91）。
/// </summary>
public class StatusBlinkTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void 赤は1秒周期に2値で点滅する()
    {
        var values = Enumerable.Range(0, 200).Select(i => StatusBlink.Alpha(StatusTone.Error, S(i * 0.01))).Distinct().ToList();
        Assert.Equal([1f, StatusBlink.ErrorLow], values);

        Assert.Equal(1f, StatusBlink.Alpha(StatusTone.Error, S(0.1)));
        Assert.Equal(StatusBlink.ErrorLow, StatusBlink.Alpha(StatusTone.Error, S(0.6)));
        Assert.Equal(1f, StatusBlink.Alpha(StatusTone.Error, S(1.1)));
    }

    [Fact]
    public void 黄は5秒周期で滑らかに点滅する()
    {
        Assert.Equal(1f, StatusBlink.Alpha(StatusTone.Warning, S(0)), 3);
        Assert.Equal(StatusBlink.WarningLow, StatusBlink.Alpha(StatusTone.Warning, S(2.5)), 3);
        Assert.Equal(1f, StatusBlink.Alpha(StatusTone.Warning, S(5)), 3);

        // 50ms（ウィンドウの描き直しの間隔）ごとの変化は小さい＝穏やか。
        for (var t = 0.0; t < 5.0; t += 0.05)
        {
            var step = MathF.Abs(StatusBlink.Alpha(StatusTone.Warning, S(t + 0.05)) - StatusBlink.Alpha(StatusTone.Warning, S(t)));
            Assert.True(step < 0.03f, $"{t}: {step}");
        }
    }

    [Theory]
    [InlineData(StatusTone.Good)]
    [InlineData(StatusTone.Quiet)]
    [InlineData(StatusTone.Idle)]
    public void 赤と黄のほかは点滅しない(StatusTone tone)
    {
        Assert.False(StatusBlink.Blinks(tone));
        Assert.All(Enumerable.Range(0, 60), i => Assert.Equal(1f, StatusBlink.Alpha(tone, S(i * 0.1))));
    }

    private static DesktopView WindowWith(DesktopStatus status, Func<TimeSpan> clock)
    {
        var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()) with { LaunchWithSteamVr = false }, _ => { })
        {
            BlinkClock = clock,
        };

        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetStatus(status);
        return view;
    }

    [Fact]
    public void 赤の点は点いているときと薄いときで描き分け_点だけを描き直す()
    {
        var now = TimeSpan.Zero;
        var error = DesktopStatus.Initial with { VrError = VrConnectError.TextureDeviceExhausted, VrServerSeen = true };
        using var view = WindowWith(error, () => now);

        Assert.Equal(StatusTone.Error, view.StatusItems[2].Tone);
        Assert.True(view.StatusBlinking);

        using var bitmap = view.RenderToBitmap();
        var slot = view.StatusSlot(2);
        var center = new Point((int)(slot.X + 3.5f), (int)(slot.Y + (slot.Height / 2f)));
        var lit = bitmap.GetPixel(center.X, center.Y);

        // 点だけを描き直す。薄いときは地の色に近くなる。
        now = TimeSpan.FromSeconds(0.6);
        using (var g = Graphics.FromImage(bitmap))
        {
            var area = view.RenderBlinkingDots(g);
            Assert.NotNull(area);
            Assert.True(area.Value.Contains(center));
            Assert.True(area.Value.Width < 20 && area.Value.Height < 20 * 2);
        }

        var dim = bitmap.GetPixel(center.X, center.Y);
        Assert.True(lit.R - dim.R > 120, $"{lit} → {dim}");
    }

    [Fact]
    public void 点滅する点がなければ描き直さない()
    {
        using var view = WindowWith(DesktopStatus.Initial, () => TimeSpan.Zero);
        using var bitmap = view.RenderToBitmap();
        using var g = Graphics.FromImage(bitmap);

        Assert.DoesNotContain(view.StatusItems, i => StatusBlink.Blinks(i.Tone));
        Assert.False(view.StatusBlinking);
        Assert.Null(view.RenderBlinkingDots(g));
    }

    [Fact]
    public void 履歴リセットのボタンはクラッシュの赤よりわずかに白い()
    {
        var style = new PanelStyle();

        Assert.Equal(style.Crash.R, style.ClearButton.R);
        Assert.InRange(style.ClearButton.G - style.Crash.G, 10, 30);
        Assert.InRange(style.ClearButton.B - style.Crash.B, 10, 30);

        using var renderer = new PanelRenderer(style);
        renderer.RenderRows(renderer.Measure(SampleRows.Build()));
        renderer.Compose(0f, new PanelDecorations { Countdown = "59:12" });

        var rect = renderer.ClearButtonRect();
        var pixels = renderer.GetPixels();
        var i = (((int)rect.Y + 1) * style.Width + (int)(rect.X + (rect.Width / 2f))) * 4;

        // 枠の上辺（BGRA）。
        Assert.Equal(style.ClearButton.B, pixels[i]);
        Assert.Equal(style.ClearButton.G, pixels[i + 1]);
        Assert.Equal(style.ClearButton.R, pixels[i + 2]);
    }
}
