using System.Drawing;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 該当する履歴がない間の見出し（2026-10-05のユーザー指定→実装メモ5.131）。
/// 消える行がないので、残り時間は <c>--:--</c> にし、「延長」は薄くして押せなくする。「履歴リセット」はそのまま。
/// </summary>
public class EmptyHistoryHeaderTests
{
    private static readonly PanelStyle Style = new();

    [Fact]
    public void 数字だけを横棒にして形は変えない()
    {
        Assert.Equal("--:--", Countdown.Blank("00:00"));
        Assert.Equal("--:--", Countdown.Blank("59:12"));
        Assert.Equal("-:--:--", Countdown.Blank("1:40:00"));
    }

    [Fact]
    public void 履歴がなければ延長は押せず_残り時間は横棒にする()
    {
        using var renderer = new PanelRenderer(Style);

        var counting = new PanelDecorations { Countdown = "00:00" };
        var empty = counting with { HistoryEmpty = true };

        Assert.False(renderer.ResetButtonRectFor(counting).IsEmpty);
        Assert.True(renderer.ResetButtonRectFor(empty).IsEmpty);

        // 「履歴リセット」は押せるまま。数字の場所も変わらない。
        Assert.Equal(renderer.ClearButtonRectFor(counting), renderer.ClearButtonRectFor(empty));
        Assert.Equal(renderer.CountdownRectFor(counting), renderer.CountdownRectFor(empty));

        Assert.Equal("00:00", PanelRenderer.CountdownTextFor(counting));
        Assert.Equal("--:--", PanelRenderer.CountdownTextFor(empty));
    }

    [Fact]
    public void 履歴がない見出しの延長は_止まっているときと同じ薄い姿で描く()
    {
        static byte[] Header(PanelDecorations decorations)
        {
            using var renderer = new PanelRenderer(Style);
            renderer.RenderRows([]);
            renderer.Compose(0f, decorations);
            return renderer.GetPixels()[..(Style.Width * Style.HeaderHeight * 4)];
        }

        static byte[] Button(byte[] header, RectangleF rect)
        {
            var r = Rectangle.Round(rect);
            var bytes = new List<byte>();

            for (var y = r.Top; y < r.Bottom; y++)
                bytes.AddRange(header.AsSpan(((y * Style.Width) + r.Left) * 4, r.Width * 4).ToArray());

            return [.. bytes];
        }

        using var geometry = new PanelRenderer(Style);
        var rect = geometry.ResetButtonRect();

        var empty = Header(new PanelDecorations { Countdown = "00:00", HistoryEmpty = true, ResetPointed = true });
        var stopped = Header(new PanelDecorations { Countdown = "60:00", CountdownStopped = true });
        var counting = Header(new PanelDecorations { Countdown = "00:00" });

        Assert.Equal(Button(stopped, rect), Button(empty, rect));
        Assert.NotEqual(Button(counting, rect), Button(empty, rect));
    }

    [Fact]
    public void ウィンドウで履歴がなければ延長をクリックしても送らず_行が来たら押せる()
    {
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetCountdown("00:00", TimeSpan.Zero);

        // 延長の場所（行があるときと同じ）を押しても何も送らない。
        using var geometry = new PanelRenderer(Style);
        var panel = view.PanelRect;
        var scale = panel.Width / Style.Width;
        var button = geometry.ResetButtonRect();
        var point = new PointF(panel.X + ((button.X + (button.Width / 2f)) * scale), panel.Y + ((button.Y + (button.Height / 2f)) * scale));

        Assert.True(view.ResetButtonScreenRect().IsEmpty);
        view.MouseDown(point);
        view.MouseUp();
        Assert.Empty(commands);

        // 残り3分以下でも、消える行がないので色を行き来させない。
        view.SetCountdown("01:00", TimeSpan.FromMinutes(1));
        view.RenderToBitmap().Dispose();
        Assert.False(view.CountdownAnimating);

        // 行が来たら押せる。
        view.SetRows(SampleRows.Build());
        Assert.False(view.ResetButtonScreenRect().IsEmpty);
        Assert.True(view.CountdownAnimating);

        view.MouseDown(point);
        view.MouseUp();
        Assert.IsType<DesktopCommand.ExtendRetention>(Assert.Single(commands));
    }
}
