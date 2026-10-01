using System.Drawing;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// SteamVRのダッシュボードに出す設定の画面（2026-09-26のユーザー指定→実装メモ5.40）。
///
/// 中身はデスクトップのウィンドウの右側と同じ <see cref="SettingsView"/> で、2列に並べる。
/// レーザーの操作はマウスのイベント（左下が原点）として届くので、左上が原点の画素へ直して当てる。
/// OpenVRには触れない部分（<see cref="SettingsDashboardView"/>）だけを検証する。
/// </summary>
public class DashboardTests
{
    private sealed class Fixture : IDisposable
    {
        public Fixture(AppSettings? settings = null)
        {
            View = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(settings ?? new AppSettings()), Commands.Add);
        }

        public SettingsDashboardView View { get; }

        public List<DesktopCommand> Commands { get; } = [];

        /// <summary>部品の中心を、ダッシュボードのマウス座標（左下が原点）で表す。</summary>
        public (float X, float Y) OverlayMouseAt(SettingsView.HitKind kind, int index = 0)
        {
            var rect = View.TargetRect(kind, index);
            Assert.NotNull(rect);

            var x = rect.Value.X + (rect.Value.Width / 2f);
            var y = rect.Value.Y + (rect.Value.Height / 2f);
            return (x, View.Height - y);
        }

        public void Press(SettingsView.HitKind kind, int index, TimeSpan now)
        {
            var (x, y) = OverlayMouseAt(kind, index);
            var point = SettingsDashboardView.FromOverlayMouse(x, y, View.Height);

            View.PointerMove(point);
            View.PointerDown(point, now);
        }

        public void Dispose() => View.Dispose();
    }

    private static DesktopCommand.ChangeSettings LastChange(Fixture f)
        => Assert.IsType<DesktopCommand.ChangeSettings>(f.Commands[^1]);

    [Fact]
    public void ダッシュボードのマウス座標は左下が原点なので上下を直す()
    {
        Assert.Equal(new PointF(100f, 700f), SettingsDashboardView.FromOverlayMouse(100f, 48f, 748));
        Assert.Equal(new PointF(0f, 0f), SettingsDashboardView.FromOverlayMouse(0f, 748f, 748));
    }

    [Fact]
    public void リセットまでの時間は1行に収まる最大の大きさで描きインスタンスタイプも同じ倍率にする()
    {
        // 2026-10-01のユーザー指定（→実装メモ5.106）。名前と − の間は名前の字1つぶん空ける。
        using var f = new Fixture();
        var factor = f.View.EmphasisScale;
        Assert.True(factor > 1f);

        var label = SettingsSteppers.Spec(SettingsStepper.Retention).Label;
        var inner = f.View.TargetRect(SettingsView.HitKind.AutoReset)!.Value.X;
        var minus = f.View.TargetRect(SettingsView.HitKind.StepperMinus, (int)SettingsStepper.Retention)!.Value;
        var plus = f.View.TargetRect(SettingsView.HitKind.StepperPlus, (int)SettingsStepper.Retention)!.Value;

        using (var painter = new UiPainter(new PanelStyle(), SettingsDashboardView.Scale * factor))
        {
            var font = painter.ProminentLabel;
            Assert.True(inner + painter.MeasureWidth(label, font) + font.Size <= minus.X + 0.5f);
        }

        // 少しでも大きくすると収まらない（最大である）。
        using (var larger = new UiPainter(new PanelStyle(), SettingsDashboardView.Scale * (factor + 0.02f)))
        {
            var font = larger.ProminentLabel;
            var controls = larger.S((21.6f * 2f) + 84f);
            Assert.True(inner + larger.MeasureWidth(label, font) + font.Size + controls > plus.Right);
        }

        // インスタンスタイプの行の高さも同じ倍率（ほかのチェックの行の倍）。
        var type = f.View.TargetRect(SettingsView.HitKind.TargetType, 0)!.Value;
        var check = f.View.TargetRect(SettingsView.HitKind.AutoReset)!.Value;
        Assert.Equal(check.Height * factor, type.Height, 2);

        // デスクトップのウィンドウ（既定）は1倍のまま。
        using var window = new SettingsView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        window.Layout(PointF.Empty, 1f, columns: 1);
        Assert.Equal(1f, window.EmphasisScale);
    }

    [Fact]
    public void 設定を2列に並べた横長の画面にする()
    {
        using var f = new Fixture();

        // 等倍の2倍の画素で描く。横長で、ダッシュボードの枠に収まる。
        // 「起動」「行の操作とグループ名」を足して縦に伸びたので（→実装メモ5.42・5.53）、3:2 前後になる。
        // 周りの透明な余白（→実装メモ5.107）を除いた、設定の画面そのものの幅で見る。
        Assert.InRange(f.View.Content.Width, 1400, 1600);
        // 「行の操作」と「グループ名」を別の枠に分けて、さらに少し縦に伸びた（→実装メモ5.64）。
        // 「手首パネル」「グループ名」の最上部に説明を足して、もう少し伸びた（→実装メモ5.66）。
        // 「VR オーバーレイ」の枠と、履歴の自動リセットの切り替えと断りを足して、さらに縦に伸びた（→実装メモ5.71）。
        // 「履歴自動リセットの予告通知」と「AFK中はカウントダウンを停止する」を足して、ほぼ正方形になった（→実装メモ5.89）。
        // 「起動」「外部連携」を外し、「インスタンス操作」を左へ移した（→実装メモ5.90）。
        Assert.InRange((float)f.View.Width / f.View.Height, 0.95f, 2.4f);

        // 左の列に「履歴」「記録する種類」「インスタンス操作」「グループ名」、右の列に「VR オーバーレイ」「手首パネル」「予告通知」。
        var retention = f.View.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value;
        var width = f.View.TargetRect(SettingsView.HitKind.StepperPlus, 1)!.Value;
        var type = f.View.TargetRect(SettingsView.HitKind.TargetType, 0)!.Value;
        var marks = f.View.TargetRect(SettingsView.HitKind.ReturnAction, 1)!.Value;
        var wrist = f.View.TargetRect(SettingsView.HitKind.WristSide, 1)!.Value;
        var reshow = f.View.TargetRect(SettingsView.HitKind.WarningReshow)!.Value;

        Assert.True(retention.Right < f.View.Width / 2f);
        Assert.True(type.Right < f.View.Width / 2f);
        Assert.True(marks.Right < f.View.Width / 2f);
        Assert.True(width.X > f.View.Width / 2f);
        Assert.True(wrist.X > f.View.Width / 2f);
        Assert.True(reshow.X > f.View.Width / 2f);

        // どの部品も画面の中に収まる。
        Assert.True(marks.Bottom < f.View.Height && reshow.Bottom < f.View.Height);

        // ウィンドウだけの設定（写真を開くアプリ・常に手前）は出さない。
        Assert.Null(f.View.TargetRect(SettingsView.HitKind.TopMost));
        Assert.Null(f.View.TargetRect(SettingsView.HitKind.PhotoViewer));
    }

    [Fact]
    public void レーザーでプラスを押すとウィンドウと同じ変更を送る()
    {
        using var f = new Fixture();

        f.Press(SettingsView.HitKind.StepperPlus, 0, TimeSpan.Zero);
        f.View.PointerUp();

        var change = LastChange(f);
        Assert.Equal(70, change.Settings.RetentionMinutes);
        Assert.Equal(SettingsField.RetentionMinutes, change.Fields);
        Assert.Equal("70 分", f.View.StepperText(0));
    }

    [Fact]
    public void 上下を直さずに当てると別の部品になる()
    {
        // 左下が原点の座標をそのまま使うと、上下が反対の位置を押したことになる（直し忘れの検出）。
        using var f = new Fixture();
        var (x, y) = f.OverlayMouseAt(SettingsView.HitKind.StepperPlus, 0);

        f.View.PointerDown(new PointF(x, y), TimeSpan.Zero);

        Assert.DoesNotContain(f.Commands, c => c is DesktopCommand.ChangeSettings { Fields: SettingsField.RetentionMinutes });
    }

    [Fact]
    public void 押し続けると0点4秒後から0点07秒ごとに繰り返す()
    {
        using var f = new Fixture();

        f.Press(SettingsView.HitKind.StepperPlus, 0, TimeSpan.Zero);
        Assert.Equal(70, LastChange(f).Settings.RetentionMinutes);

        f.View.Update(TimeSpan.FromMilliseconds(300));
        Assert.Single(f.Commands);

        f.View.Update(TimeSpan.FromMilliseconds(400));
        Assert.Equal(80, LastChange(f).Settings.RetentionMinutes);

        f.View.Update(TimeSpan.FromMilliseconds(440));
        Assert.Equal(2, f.Commands.Count);

        f.View.Update(TimeSpan.FromMilliseconds(470));
        Assert.Equal(90, LastChange(f).Settings.RetentionMinutes);

        // 離したら止まる。
        f.View.PointerUp();
        f.View.Update(TimeSpan.FromSeconds(2));
        Assert.Equal(3, f.Commands.Count);
    }

    [Fact]
    public void 押し続けていてもレーザーが外れたら繰り返さない()
    {
        using var f = new Fixture();

        f.Press(SettingsView.HitKind.StepperPlus, 0, TimeSpan.Zero);
        f.View.PointerMove(null);
        f.View.Update(TimeSpan.FromSeconds(1));

        Assert.Single(f.Commands);
    }

    [Fact]
    public void 種類の付け外しと配置の戻しもできる()
    {
        using var f = new Fixture();
        var friends = TargetAccessTypes.Selectable.ToList().IndexOf(AccessType.Friends);

        f.Press(SettingsView.HitKind.TargetType, friends, TimeSpan.Zero);
        f.View.PointerUp();

        var change = LastChange(f);
        Assert.Contains(AccessType.Friends, change.Settings.TargetTypes);
        Assert.Equal(SettingsField.TargetAccessTypes, change.Fields);

        f.Press(SettingsView.HitKind.ResetPlacement, 0, TimeSpan.Zero);
        Assert.IsType<DesktopCommand.ResetPlacement>(f.Commands[^1]);
    }

    [Fact]
    public void ウィンドウで変わった値を受け取っても送り返さない()
    {
        using var f = new Fixture();

        f.View.SetSettings(f.View.Settings with { RetentionMinutes = 120, BackgroundOpacity = 0.5f });

        Assert.Empty(f.Commands);
        Assert.Equal("120 分", f.View.StepperText(0));
        Assert.Equal("50 %", f.View.StepperText(2));
        Assert.True(f.View.Dirty);

        f.View.Render();
        Assert.False(f.View.Dirty);
    }

    [Fact]
    public void ウィンドウもダッシュボードで変わった値を受け取って送り返さない()
    {
        var commands = new List<DesktopCommand>();
        var style = new PanelStyle();
        using var view = new DesktopView(style, DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);

        Color PanelCorner()
        {
            using var bitmap = view.RenderToBitmap();
            var panel = view.PanelRect;
            return bitmap.GetPixel((int)panel.X + 4, (int)panel.Y + 4);
        }

        var before = PanelCorner();
        view.SetSettings(view.Settings with { ScrollRowsPerSecond = 9f, BackgroundOpacity = 0.6f });

        Assert.Empty(commands);
        Assert.Equal("9.0 行/秒", view.StepperText(5));

        // 背景の不透明度はウィンドウのパネルの絵にも出す（地の色が透けて暗くなる）。渡した配色はウィンドウが写して使うので変えない。
        var after = PanelCorner();
        Assert.True(after.R < before.R && after.G < before.G && after.B < before.B);
        Assert.Equal(new PanelStyle().BackgroundOpacity, style.BackgroundOpacity);
    }

    [Fact]
    public void 指している部品が変わったときだけ描き直す()
    {
        using var f = new Fixture();
        f.View.Render();

        var (x, y) = f.OverlayMouseAt(SettingsView.HitKind.StepperPlus, 1);
        var point = SettingsDashboardView.FromOverlayMouse(x, y, f.View.Height);

        f.View.PointerMove(point);
        Assert.True(f.View.Dirty);
        f.View.Render();

        // 同じ部品の中で動いただけなら描き直さない（レーザーは毎フレーム少しずつ動く）。
        f.View.PointerMove(new PointF(point.X + 1f, point.Y + 1f));
        Assert.False(f.View.Dirty);
    }

    [Fact]
    public void 画素はテクスチャと同じBGRAで大きさぴったり()
    {
        using var f = new Fixture();
        f.View.Render();

        var pixels = f.View.GetPixels();
        Assert.Equal(f.View.Width * f.View.Height * 4, pixels.Length);

        // 設定の画面の背景は不透明で、周りの余白は透明（→実装メモ5.107）。
        var content = f.View.Content;
        Assert.Equal(255, pixels[(((content.Y * f.View.Width) + content.X) * 4) + 3]);
        Assert.Equal(0, pixels[3]);
    }

    [Fact]
    public void ダッシュボードの枠の中で設定の画面は75パーセントの面積に見える()
    {
        // 2026-10-01のユーザー指定（→実装メモ5.107）。ダッシュボードは絵を枠に合わせて出すので、
        // 透明な余白を足した絵の中で設定の画面が占める面積が、そのまま枠の中での割合になる。縦横の比は変えない。
        using var f = new Fixture();
        var content = f.View.Content;

        var ratio = (float)(content.Width * content.Height) / (f.View.Width * f.View.Height);
        Assert.InRange(ratio, 0.745f, 0.755f);
        Assert.Equal((float)content.Width / content.Height, (float)f.View.Width / f.View.Height, 2);

        // 真ん中に置き、部品はすべて設定の画面の中に並ぶ。
        Assert.InRange(content.X - (f.View.Width - content.Right), -1, 1);
        Assert.InRange(content.Y - (f.View.Height - content.Bottom), -1, 1);

        foreach (var kind in new[] { SettingsView.HitKind.AutoReset, SettingsView.HitKind.Vibration, SettingsView.HitKind.WarningReshow, SettingsView.HitKind.GroupIdWithName })
            Assert.True(content.Contains(Rectangle.Round(f.View.TargetRect(kind)!.Value)), kind.ToString());

        // 余白の上は何も指さない。
        f.View.PointerMove(new PointF(2f, 2f));
        f.View.PointerDown(new PointF(2f, 2f), TimeSpan.Zero);
        f.View.PointerUp();
        Assert.Empty(f.Commands);
    }

    [Fact]
    public void アイコンは透明の地に一覧の図を描く()
    {
        using var icon = SettingsDashboardView.RenderThumbnail(new PanelStyle(), 128);

        Assert.Equal(0, icon.GetPixel(0, 0).A);
        Assert.Equal(255, icon.GetPixel(64, 64).A);

        // いまいる行の線はアクセント色。
        var accent = new PanelStyle().Accent;
        var found = Enumerable.Range(0, 128).Any(y => icon.GetPixel(90, y).ToArgb() == accent.ToArgb());
        Assert.True(found);
    }
}

