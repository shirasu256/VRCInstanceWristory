using System.Drawing;
using System.Globalization;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// デスクトップのウィンドウの中身（<see cref="DesktopView"/>）の、見直しで直した不具合。
/// </summary>
public class DesktopViewFixesTests
{
    [Fact]
    public void 表示倍率が変わった直後に行や詳しい情報が届いても_手放した字で並べない()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);
        var rows = SampleRows.Build();
        var details = SampleRows.Details();

        // 行を選んで「インスタンス詳細」を出し、1回描く。
        var point = SampleWindow.PointOfRow(view, rows, rows[^1].InstanceId);
        view.MouseDown(point);
        view.MouseUp();
        Assert.Equal(DesktopTab.Details, view.Tab);
        view.RenderToBitmap().Dispose();

        // 別のモニターへ移って倍率が変わった（描き手を作り直す）。並べ直す前に、主ループから詳しい情報と状態が届く。
        view.Resize(new Size(1590, 1140), 1.5f);
        view.SetDetails([.. details.Select(d => d with { GroupName = "名前を変えた" })]);
        view.SetStatus(DesktopStatus.Initial with { ClientRunning = true });

        using var bitmap = view.RenderToBitmap();
        Assert.Equal(1590, bitmap.Width);
    }

    [Fact]
    public void 初回起動の案内の間は_何も変わらなければ描き直さない()
    {
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.ShowWelcome();

        Assert.True(view.Dirty);
        view.RenderToBitmap().Dispose();
        Assert.False(view.Dirty);

        // 「わかった」の外でマウスを動かしても、見た目は変わらないので描き直さない。
        view.MouseMove(new PointF(10f, 10f));
        view.MouseMove(new PointF(20f, 15f));
        Assert.False(view.Dirty);

        // 「わかった」を指したら描き直す。
        var button = view.WelcomeButtonRect();
        view.MouseMove(new PointF(button.X + (button.Width / 2f), button.Y + (button.Height / 2f)));
        Assert.True(view.Dirty);
        view.RenderToBitmap().Dispose();
        Assert.False(view.Dirty);
    }

    [Fact]
    public void 設定の数値は_小数点がカンマの言語でもピリオドで出す()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var settings = DesktopSettings.From(new AppSettings()) with { ScrollRowsPerSecond = 9f, PanelWidthMeters = 0.145f, ViewAngleFadeSeconds = 0.25f };
            using var view = new SettingsView(new PanelStyle(), settings, _ => { });

            Assert.Equal("9.0 行/秒", view.StepperText((int)SettingsStepper.ScrollSpeed));
            Assert.Equal("14.5 cm", view.StepperText((int)SettingsStepper.PanelWidth));
            Assert.Equal("0.25 秒", view.StepperText((int)SettingsStepper.FadeSeconds));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void 渡した配色はウィンドウが変えない()
    {
        var style = new PanelStyle();
        var rows = style.VisibleRows;

        using var view = new DesktopView(style, DesktopSettings.From(new AppSettings()), _ => { });
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.SetSettings(view.Settings with { BackgroundOpacity = 0.5f });

        Assert.Equal(rows, style.VisibleRows);
        Assert.Equal(new PanelStyle().BackgroundOpacity, style.BackgroundOpacity);
    }

    [Fact]
    public void 自動リセットを無効にすると_命中の判定より前に見出しの延長が消える()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);

        var before = view.ResetButtonScreenRect();
        Assert.True(before.Width > 0f);

        // ダッシュボードで自動リセットを無効にした。ウィンドウでは何も指していなくても、見出しの「延長」の場所はすぐに変わる。
        view.SetSettings(view.Settings with { AutoResetEnabled = false });
        Assert.NotEqual(before, view.ResetButtonScreenRect());
    }

    [Fact]
    public void 記録する種類が同じなら_別の集合でも同じ設定とみなす()
    {
        var a = DesktopSettings.From(new AppSettings());
        var b = a with { TargetTypes = new HashSet<AccessType>(a.TargetTypes) };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { TargetTypes = new HashSet<AccessType> { AccessType.Public } });
    }
    [Fact]
    public void 終了の通知の受け手は_片付けたあとに通知が届いても待たずに返る()
    {
        var watcher = SessionEndWatcher.Start(new CollectingDiagnostics());
        watcher.Dispose();

        // 保存を待つ合図は手放してある。遅れて届いた WM_ENDSESSION（デスクトップのウィンドウから）でも例外にしない。
        var started = DateTime.UtcNow;
        watcher.OnEndSession(ending: true);

        Assert.True(watcher.Requested);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }
}
