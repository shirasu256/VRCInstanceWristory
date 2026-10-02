using System.Drawing;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 「該当履歴が無い場合も表示する」（2026-10-02のユーザー指定→実装メモ5.128）。
///
/// - 既定はオフ。VR オーバーレイ設定のタブで「ロード画面中もパネルを表示する」の上に置く
/// - オンなら、表示条件を満たしているのに出す行がないときも手首のパネルを出し、「該当する履歴はありません」と書く
/// - デスクトップのウィンドウの「記録した訪問はまだありません」も同じ文にした（描くのは同じ <see cref="PanelRenderer"/>）
/// </summary>
public class EmptyHistoryPanelTests
{
    private static DesktopView View(DesktopSettings settings, List<DesktopCommand> commands)
    {
        var view = new DesktopView(new PanelStyle(), settings, commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SelectTab(DesktopTab.Panel);
        return view;
    }

    private static PointF CheckPoint(RectangleF rect) => new(rect.X + 4f, rect.Y + (rect.Height / 2f));

    [Fact]
    public void 既定はオフ()
    {
        Assert.False(new AppSettings().ShowPanelWhenEmpty);
        Assert.False(DesktopSettings.From(new AppSettings()).ShowWhenEmpty);
    }

    [Fact]
    public void ロード画面中もパネルを表示するの上にあり_その項目だけを送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = View(DesktopSettings.From(new AppSettings()), commands);

        var grab = view.TargetRect(SettingsView.HitKind.PanelGrab)!.Value;
        var empty = view.TargetRect(SettingsView.HitKind.ShowWhenEmpty)!.Value;
        var loading = view.TargetRect(SettingsView.HitKind.ShowDuringLoading)!.Value;
        Assert.True(empty.Top > grab.Bottom);
        Assert.True(loading.Top >= empty.Bottom);

        view.MouseDown(CheckPoint(empty));
        view.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.EmptyHistory, change.Fields);
        Assert.True(change.Settings.ShowWhenEmpty);
        Assert.True(change.Settings.ShowDuringLoading);

        var settings = new AppSettings();
        change.Settings.ApplyTo(settings, change.Fields);
        Assert.True(settings.ShowPanelWhenEmpty);
    }

    [Fact]
    public void VRオーバーレイ機能をオフにしている間は押せない()
    {
        var commands = new List<DesktopCommand>();
        using var view = View(DesktopSettings.From(new AppSettings()) with { VrOverlayEnabled = false }, commands);

        var empty = view.TargetRect(SettingsView.HitKind.ShowWhenEmpty)!.Value;
        Assert.False(view.IsClickable(CheckPoint(empty)));
    }

    [Fact]
    public void 設定ファイルへ書き戻せる()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        new AppSettings { ShowPanelWhenEmpty = true }.SaveFields(path, SettingsField.EmptyHistory);
        Assert.True(AppSettings.Load(path, new CollectingDiagnostics()).ShowPanelWhenEmpty);
        Assert.Contains("\"showPanelWhenEmpty\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void 行がなければ表示領域に該当する履歴はありませんと書く()
    {
        Assert.Equal("該当する履歴はありません", PanelRenderer.EmptyMessage);

        using var renderer = new PanelRenderer(new PanelStyle());
        renderer.SetViewportForContent(0f);
        renderer.RenderRows([]);
        renderer.Compose(0f);

        // 文字がなければ、表示領域は背景の1色だけになる。
        Assert.True(ViewportColors(renderer) > 1);
    }

    private static byte[] ViewportPixels(PanelRenderer renderer)
    {
        var pixels = renderer.GetPixels();
        var style = renderer.Style;
        var start = style.ViewportTop * renderer.Width * 4;
        var length = renderer.ViewportHeight * renderer.Width * 4;
        return pixels.AsSpan(start, length).ToArray();
    }

    private static int ViewportColors(PanelRenderer renderer)
    {
        var pixels = ViewportPixels(renderer);
        var colors = new HashSet<int>();

        for (var i = 0; i < pixels.Length; i += 4)
            colors.Add(BitConverter.ToInt32(pixels, i));

        return colors.Count;
    }
}
