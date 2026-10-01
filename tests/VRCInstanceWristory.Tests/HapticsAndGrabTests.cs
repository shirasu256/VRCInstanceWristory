using System.Drawing;
using System.Text.Json.Nodes;
using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Tests.TestProcesses;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 「コントローラーの振動」と「手首パネルの移動」の切り替え（2026-09-28のユーザー指定→実装メモ5.78）。
/// </summary>
public class HapticsAndGrabTests
{
    private static readonly PanelPointTarget Row0 = new(PanelPart.Row, 0);
    private static readonly PanelPointTarget Row1 = new(PanelPart.Row, 1);

    [Fact]
    public void 既定は振動がオフで_掴んで移動はオン()
    {
        var defaults = new AppSettings();
        Assert.False(defaults.ControllerVibrationEnabled);
        Assert.True(defaults.PanelGrabEnabled);

        var desktop = DesktopSettings.From(defaults);
        Assert.False(desktop.VibrationEnabled);
        Assert.True(desktop.PanelGrabEnabled);
    }

    [Fact]
    public void 指す部品が変わるたびに弱く鳴らし_外れたときは鳴らさない()
    {
        var haptics = new PanelHaptics();

        Assert.Equal(HapticPulse.Point, haptics.Update(Row0, pressed: false, grabbing: false));
        Assert.Null(haptics.Update(Row0, pressed: false, grabbing: false));               // 同じ行を指し続けても鳴らさない
        Assert.Equal(HapticPulse.Point, haptics.Update(Row1, pressed: false, grabbing: false));
        Assert.Null(haptics.Update(PanelPointTarget.None, pressed: false, grabbing: false)); // 外れた
        Assert.Equal(HapticPulse.Point, haptics.Update(Row1, pressed: false, grabbing: false)); // 戻ったら鳴らす
        Assert.Equal(HapticPulse.Point, haptics.Update(new PanelPointTarget(PanelPart.Extend), pressed: false, grabbing: false));
    }

    [Fact]
    public void 押したときと掴んだときは強く鳴らし_1フレームに1回だけ()
    {
        var haptics = new PanelHaptics();
        haptics.Update(Row0, pressed: false, grabbing: false);

        // 押してポップアップが開き、指す部品が変わった同じフレームでも「押した」の1回だけ。
        Assert.Equal(HapticPulse.Press, haptics.Update(new PanelPointTarget(PanelPart.PopupChoice, 2), pressed: true, grabbing: false));

        // 掴むのは押すより優先。掴んでいる間は指す部品が変わっても鳴らさず、離したら弱く1回。
        Assert.Equal(HapticPulse.Grab, haptics.Update(Row0, pressed: false, grabbing: true));
        Assert.Null(haptics.Update(Row1, pressed: false, grabbing: true));
        Assert.Equal(HapticPulse.Tick, haptics.Update(Row1, pressed: false, grabbing: false));
    }

    [Fact]
    public void 鳴らした理由を残し_離したと指す部品が変わったを区別できる()
    {
        var haptics = new PanelHaptics();

        haptics.Update(Row0, pressed: false, grabbing: false);
        Assert.Equal("指す部品が変わった（Row 0）", haptics.LastReason);

        haptics.Update(Row0, pressed: false, grabbing: false);
        Assert.Null(haptics.LastReason);

        haptics.Update(PanelPointTarget.None, pressed: false, grabbing: true);
        Assert.Equal("掴んだ", haptics.LastReason);

        haptics.Update(Row1, pressed: false, grabbing: false);
        Assert.Equal("離した", haptics.LastReason);
    }

    [Fact]
    public void パネルが隠れたら_次に出て最初に指した部品で鳴らす()
    {
        var haptics = new PanelHaptics();
        haptics.Update(Row0, pressed: false, grabbing: false);
        haptics.Reset();

        Assert.Equal(HapticPulse.Point, haptics.Update(Row0, pressed: false, grabbing: false));
    }

    [Fact]
    public void 振動の形は短く_強さは0から1の間()
    {
        foreach (var pulse in Enum.GetValues<HapticPulse>())
        {
            var (duration, frequency, amplitude) = HapticPulses.Shape(pulse);
            // SteamVR のドライバー向けの資料の範囲: 長さ 0〜10秒、強さ 0 より大きく 1 以下、周波数 1000000/65535〜1000000/300 Hz。
            Assert.InRange(duration, 0f, 0.1f);
            Assert.InRange(frequency, 1000000f / 65535f, 1000000f / 300f);
            Assert.InRange(amplitude, 1f / 255f, 1f);
        }

        // 指したときはパルス1回ぶん（長さ × 周波数 = 1）・強さ 0.10（→実装メモ5.81）。
        var point = HapticPulses.Shape(HapticPulse.Point);
        Assert.Equal(1f, point.DurationSeconds * point.Frequency, 3);
        Assert.Equal(0.10f, point.Amplitude);
        Assert.True(HapticPulses.Shape(HapticPulse.Point).Amplitude < HapticPulses.Shape(HapticPulse.Tick).Amplitude);
        Assert.True(HapticPulses.Shape(HapticPulse.Tick).Amplitude < HapticPulses.Shape(HapticPulse.Press).Amplitude);
    }

    [Fact]
    public void 振動はVRオーバーレイの下_移動はパネル位置の下でロード画面の上に並ぶ()
    {
        using var desktop = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        desktop.Resize(DesktopView.DefaultClientSize, 1f);
        desktop.SelectTab(DesktopTab.Panel);

        var overlay = desktop.TargetRect(SettingsView.HitKind.VrOverlay)!.Value;
        var vibration = desktop.TargetRect(SettingsView.HitKind.Vibration)!.Value;
        Assert.True(vibration.Top >= overlay.Bottom);

        var wrist = desktop.TargetRect(SettingsView.HitKind.WristSide)!.Value;
        var grab = desktop.TargetRect(SettingsView.HitKind.PanelGrab)!.Value;
        var loading = desktop.TargetRect(SettingsView.HitKind.ShowDuringLoading)!.Value;
        Assert.True(grab.Top >= wrist.Bottom);

        // 「有効な場合、手首パネルは掴んで移動できます。」の1行ぶん、ロード画面の切り替えとの間が空く。
        Assert.True(loading.Top > grab.Bottom);
    }

    [Theory]
    [InlineData(SettingsView.HitKind.Vibration, SettingsField.Vibration)]
    [InlineData(SettingsView.HitKind.PanelGrab, SettingsField.PanelGrab)]
    public void 切り替えを押すとその項目だけを送る(SettingsView.HitKind kind, SettingsField field)
    {
        var commands = new List<DesktopCommand>();
        using var desktop = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        desktop.Resize(DesktopView.DefaultClientSize, 1f);
        desktop.SelectTab(DesktopTab.Panel);

        var rect = desktop.TargetRect(kind)!.Value;
        desktop.MouseDown(new PointF(rect.X + 4f, rect.Y + (rect.Height / 2f)));
        desktop.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(field, change.Fields);
        Assert.True(change.Settings.VibrationEnabled != (kind != SettingsView.HitKind.Vibration)); // 振動はオンへ
        Assert.True(change.Settings.PanelGrabEnabled != (kind == SettingsView.HitKind.PanelGrab));  // 移動はオフへ
    }

    [Fact]
    public void VRオーバーレイ機能をオフにしている間はどちらも押せない()
    {
        var commands = new List<DesktopCommand>();
        using var desktop = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()) with { VrOverlayEnabled = false }, commands.Add);
        desktop.Resize(DesktopView.DefaultClientSize, 1f);
        desktop.SelectTab(DesktopTab.Panel);

        foreach (var kind in new[] { SettingsView.HitKind.Vibration, SettingsView.HitKind.PanelGrab })
        {
            var rect = desktop.TargetRect(kind)!.Value;
            var center = new PointF(rect.X + 4f, rect.Y + (rect.Height / 2f));
            Assert.False(desktop.IsClickable(center));
            desktop.MouseDown(center);
            desktop.MouseUp();
        }

        Assert.Empty(commands);
    }

    [Fact]
    public void 振動とパネルを掴む切り替えは設定ファイルへ書き戻せる()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        var settings = new AppSettings { ControllerVibrationEnabled = true, PanelGrabEnabled = false };
        settings.SaveFields(path, SettingsField.Vibration | SettingsField.PanelGrab);

        var loaded = AppSettings.Load(path, new CollectingDiagnostics());
        Assert.True(loaded.ControllerVibrationEnabled);
        Assert.False(loaded.PanelGrabEnabled);

        var text = File.ReadAllText(path);
        Assert.Contains("\"controllerVibrationEnabled\"", text, StringComparison.Ordinal);
        Assert.Contains("\"panelGrabEnabled\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ウィンドウで振動をオンにすると主ループが反映して設定ファイルへ保存する()
    {
        using var dir = new TempLogDirectory();
        var start = new DateTime(2026, 9, 11, 1, 0, 0);
        dir.WriteSession(start, LogText.Visit(start.AddMinutes(1), Loc.GroupPublic("111"), "A"));
        using var harness = new EngineHarness(dir.Path, start.AddMinutes(2), Process(start));
        harness.Engine.Initialize();

        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        var settings = new AppSettings();
        var changes = new SettingsChanges(settings, path, new CollectingDiagnostics(), harness.Clock);
        var commands = new List<DesktopCommand>();
        using var desktop = new DesktopView(new PanelStyle(), DesktopSettings.From(settings), commands.Add);
        desktop.Resize(DesktopView.DefaultClientSize, 1f);
        desktop.SelectTab(DesktopTab.Panel);

        var rect = desktop.TargetRect(SettingsView.HitKind.Vibration)!.Value;
        desktop.MouseDown(new PointF(rect.X + 4f, rect.Y + (rect.Height / 2f)));
        desktop.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        changes.Apply(change.Settings, change.Fields, runtime: null);

        // 手首パネルは主ループと同じ AppSettings を毎フレーム読むので、ここが立てば次のフレームから鳴らす。
        Assert.True(settings.ControllerVibrationEnabled);

        changes.Flush(harness.Engine, force: true);
        Assert.True(AppSettings.Load(path, new CollectingDiagnostics()).ControllerVibrationEnabled);
    }

    [Fact]
    public void 振動のアクションは宣言してあり_同梱の割り当てはすべて左右の手で鳴らせる()
    {
        var resources = Path.Combine(AppContext.BaseDirectory, "Resources");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(resources, "actions.json")))!;
        var haptic = manifest["actions"]!.AsArray().Single(a => (string?)a!["name"] == SteamVrInput.HapticAction)!;
        Assert.Equal("vibration", (string?)haptic["type"]);

        foreach (var localization in manifest["localization"]!.AsArray())
            Assert.NotNull(localization![SteamVrInput.HapticAction]);

        var files = Directory.GetFiles(Path.Combine(resources, "bindings"), "*.json");
        Assert.Equal(5, files.Length);

        foreach (var file in files)
        {
            var main = JsonNode.Parse(File.ReadAllText(file))!["bindings"]!["/actions/main"]!;
            var haptics = main["haptics"]!.AsArray().Select(h => ((string?)h!["output"], (string?)h["path"])).ToList();

            Assert.Contains((SteamVrInput.HapticAction, "/user/hand/right/output/haptic"), haptics);
            Assert.Contains((SteamVrInput.HapticAction, "/user/hand/left/output/haptic"), haptics);
        }
    }
}
