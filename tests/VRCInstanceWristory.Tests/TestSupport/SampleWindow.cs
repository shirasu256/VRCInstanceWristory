using System.Drawing;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 見本の履歴を並べたデスクトップのウィンドウの中身（<see cref="DesktopView"/>）と、それをマウスで操作する処理。
/// Win32 のウィンドウは作らないので、画面には何も出ない。
/// </summary>
public static class SampleWindow
{
    /// <summary>既定の大きさで、見本の行・詳しい情報・残り時間を並べたウィンドウの中身。送った命令は <paramref name="commands"/> へ積む。</summary>
    public static DesktopView Create(List<DesktopCommand> commands, AppSettings? settings = null)
    {
        var view = new DesktopView(new PanelStyle(), DesktopSettings.From(settings ?? new AppSettings()), commands.Add);

        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetRows(SampleRows.Build());
        view.SetDetails(SampleRows.Details());
        view.SetCountdown("59:12");
        view.SetStatus(DesktopStatus.Initial);
        return view;
    }

    /// <summary>その行の中ほど（画面の座標）。末尾から見えているので、下から数えて求める。</summary>
    public static PointF PointOfRow(DesktopView view, IReadOnlyList<DisplayRow> rows, string instanceId)
    {
        // 行は上に寄せて並び、表示領域はウィンドウの下まで伸びる（→実装メモ5.71）。行の見えている矩形の中ほどを指す。
        var panel = view.PanelRect;
        var index = rows.ToList().FindLastIndex(r => r.InstanceId == instanceId);
        var rect = view.RowScreenRect(index);

        return new PointF(panel.X + (panel.Width * 0.42f), rect.Y + (rect.Height / 2f));
    }

    /// <summary>矩形の中心。</summary>
    public static PointF Center(RectangleF rect) => new(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));

    /// <summary>その点へマウスを動かして左ボタンで押す。</summary>
    public static void Click(DesktopView view, PointF point)
    {
        view.MouseMove(point);
        view.MouseDown(point);
        view.MouseUp();
    }

    /// <summary>その矩形の中心を左ボタンで押す。</summary>
    public static void Click(DesktopView view, RectangleF rect) => Click(view, Center(rect));
}
