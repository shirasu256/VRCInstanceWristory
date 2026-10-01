using System.Drawing;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 初回起動の案内（2026-09-30のユーザー指定）。デスクトップのウィンドウが、まだ「わかった」を押したことがなければ
/// 中身の代わりに全面へ出し、初期設定の画面の「はじめる」で閉じて settings.json に記録を残す。
/// </summary>
public class WelcomeViewTests
{

    private static PointF Center(RectangleF r) => new(r.X + (r.Width / 2f), r.Y + (r.Height / 2f));

    [Fact]
    public void Render_DrawsWristPanelAtWindowSize()
    {
        using var bitmap = WelcomeView.Render(new PanelStyle(), DesktopView.DefaultClientSize);

        Assert.Equal(DesktopView.DefaultClientSize, bitmap.Size);

        // イラストの左半分に、手首のパネルの枠（アクセント色）が描かれている。
        var accent = new PanelStyle().Accent;
        var found = false;

        for (var y = 110; y < 520 && !found; y += 2)
        {
            for (var x = 90; x < 520 && !found; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                found = Math.Abs(c.R - accent.R) < 8 && Math.Abs(c.G - accent.G) < 8 && Math.Abs(c.B - accent.B) < 8;
            }
        }

        Assert.True(found);
    }

    [Theory]
    [InlineData(1060, 760)]
    [InlineData(860, 660)]
    [InlineData(1290, 990)]
    [InlineData(1590, 1140)]
    public void ButtonRect_FitsInsideWindowAtAnySize(int width, int height)
    {
        var client = new Size(width, height);
        var button = WelcomeView.ButtonRect(client);

        Assert.True(new RectangleF(0, 0, width, height).Contains(button));

        // 既定の大きさの 300×60（200×40 の1.5倍）を、ウィンドウに収める倍率で縮めたもの。
        var scale = MathF.Min(width / 1060f, height / 760f);
        Assert.Equal(300f * scale, button.Width, 1);
        Assert.Equal(60f * scale, button.Height, 1);
    }

    [Fact]
    public void DesktopView_WhileWelcomeOpen_OnlyTheButtonResponds()
    {
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.ShowWelcome();

        Assert.True(view.WelcomeOpen);
        Assert.False(view.StatusBlinking);
        Assert.False(view.MinimizesToTray);

        // 案内の外（ふだんなら見出しの「リセット」やタブがある場所）を押しても何も起きない。
        var clear = view.ClearButtonScreenRect();
        view.MouseDown(Center(clear));
        view.MouseUp();
        view.RightMouseDown(new PointF(300f, 300f));
        view.MouseWheel(new PointF(300f, 300f), 120);

        Assert.True(view.WelcomeOpen);
        Assert.False(view.ClearConfirmOpen);
        Assert.Empty(commands);
        Assert.False(view.IsClickable(Center(clear)));

        // 「わかった」を指すと手の形のカーソルになり、押すと初期設定の画面へ進む（まだ終えた合図は出さない）。
        var button = Center(view.WelcomeButtonRect());
        Assert.True(view.IsClickable(button));

        view.MouseMove(button);
        view.MouseDown(button);
        view.MouseUp();

        Assert.False(view.WelcomeOpen);
        Assert.True(view.SetupOpen);
        Assert.Empty(commands);
    }

    /// <summary>「わかった」を押して初期設定の画面を出したウィンドウ（→実装メモ5.98）。</summary>
    private static DesktopView OpenSetup(List<DesktopCommand> commands, AppSettings? settings = null, Size? client = null, float scale = 1f)
    {
        var view = new DesktopView(new PanelStyle(), DesktopSettings.From(settings ?? new AppSettings()), commands.Add);
        view.Resize(client ?? DesktopView.DefaultClientSize, scale);
        view.ShowWelcome();

        var button = Center(view.WelcomeButtonRect());
        view.MouseDown(button);
        view.MouseUp();
        return view;
    }

    [Fact]
    public void Setup_ShowsTheRequestedItemsAndTheAfkToggle()
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenSetup(commands);

        Assert.True(view.SetupOpen);
        Assert.False(view.StatusBlinking);

        // 初期設定を終えるまでは、最小化してもタスクトレイへ入れない（2026-10-01のユーザー指定→実装メモ5.113）。
        Assert.False(view.MinimizesToTray);

        // リセットまでの時間・訪問履歴に含めるインスタンスタイプ・起動・パネル位置・VRChatのAFKを検知する（2026-09-30のユーザー指定）。
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.StepperPlus, 0));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.TargetType, 0));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.LaunchAtLogon));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.LaunchWithSteamVr));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.WristSide, 1));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.AfkDetection));

        // ほかの設定（自動リセットの切り替え・手首パネルの幅など）は出さない。
        Assert.Null(view.TargetRect(SettingsView.HitKind.AutoReset));
        Assert.Null(view.TargetRect(SettingsView.HitKind.StepperPlus, 1));
        Assert.Null(view.TargetRect(SettingsView.HitKind.AfkPause));
    }

    [Theory]
    [InlineData(1060, 760, 1f)]
    [InlineData(860, 660, 1f)]
    [InlineData(1290, 990, 1.25f)]
    [InlineData(1290, 990, 1.5f)]
    public void Setup_FitsInsideWindow(int width, int height, float scale)
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenSetup(commands, client: new Size(width, height), scale: scale);
        var window = new RectangleF(0, 0, width, height);

        Assert.True(window.Contains(view.SetupButtonRect()));
        Assert.True(window.Contains(view.TargetRect(SettingsView.HitKind.AfkDetection)!.Value));
        Assert.True(window.Contains(view.TargetRect(SettingsView.HitKind.TargetType, 7)!.Value));

        // 設定の部品と「はじめる」は重ならない。
        Assert.True(view.TargetRect(SettingsView.HitKind.AfkDetection)!.Value.Bottom < view.SetupButtonRect().Y);
    }

    [Fact]
    public void Setup_ChangesSettingsInPlace_AndStartFinishesOnboarding()
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenSetup(commands);

        // 「VRChatのAFKを検知する」を押すと、その場で設定を変える合図を出す（主ループがここで OSC の受け口を開く）。
        view.MouseDown(Center(view.TargetRect(SettingsView.HitKind.AfkDetection)!.Value));
        view.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.AfkDetection | SettingsField.AfkPause | SettingsField.ResetWarning, change.Fields);
        Assert.True(change.Settings.AfkDetectionEnabled);

        // AFK を使う2つの設定も一緒にオンにする（2026-10-01のユーザー指定→実装メモ5.116）。ほかの予告通知の値は変えない。
        Assert.True(change.Settings.PauseCountdownWhileAfk);
        Assert.True(change.Settings.WarningReshowAfterAfk);
        Assert.Equal(DesktopSettings.From(new AppSettings()) with
        {
            AfkDetectionEnabled = true,
            PauseCountdownWhileAfk = true,
            WarningReshowAfterAfk = true,
        }, change.Settings);

        // パネル位置も同じ部品で変えられる。
        view.MouseDown(Center(view.TargetRect(SettingsView.HitKind.WristSide, 1)!.Value));
        view.MouseUp();
        Assert.Equal(SettingsField.WristSide, Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]).Fields);

        // 部品の外を押しても何も起きない。
        commands.Clear();
        view.MouseDown(new PointF(10f, 10f));
        view.MouseUp();
        view.RightMouseDown(new PointF(10f, 10f));
        Assert.Empty(commands);
        Assert.True(view.SetupOpen);

        // 「はじめる」を押すと、ふだんの画面に戻り、案内を終えた合図を出す。
        var start = view.SetupButtonRect();
        Assert.True(view.IsClickable(Center(start)));
        view.MouseMove(Center(start));
        view.MouseDown(Center(start));
        view.MouseUp();

        Assert.False(view.SetupOpen);
        Assert.False(view.WelcomeOpen);
        Assert.IsType<DesktopCommand.FinishWelcome>(Assert.Single(commands));

        // 初期設定を終えたら、最小化でタスクトレイへ入れる（→実装メモ5.113）。
        Assert.True(view.MinimizesToTray);

        // ふだんの画面の部品に戻っている（右側の「一般設定」の並び）。「VRChatのAFKを検知する」は初期設定の画面だけに出す（→実装メモ5.99）。
        Assert.Equal(DesktopTab.Startup, view.Tab);
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.AutoReset));
        Assert.NotNull(view.TargetRect(SettingsView.HitKind.AfkPause));
        Assert.Null(view.TargetRect(SettingsView.HitKind.AfkDetection));
    }

    [Fact]
    public void Setup_StepperRepeatsLikeTheSettingsTab()
    {
        var commands = new List<DesktopCommand>();
        using var view = OpenSetup(commands);

        view.MouseDown(Center(view.TargetRect(SettingsView.HitKind.StepperPlus, 0)!.Value));
        Assert.True(view.Repeating);
        view.MouseUp();
        Assert.False(view.Repeating);
        Assert.Contains(commands, c => c is DesktopCommand.ChangeSettings { Fields: SettingsField.RetentionMinutes });
    }

    [Fact]
    public void DesktopView_RendersWelcomeInsteadOfContent()
    {
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });
        view.Resize(DesktopView.DefaultClientSize, 1f);
        view.ShowWelcome();

        using var fromView = view.RenderToBitmap();
        using var expected = WelcomeView.Render(new PanelStyle(), DesktopView.DefaultClientSize, buttonPointed: false);

        for (var y = 0; y < expected.Height; y += 37)
        {
            for (var x = 0; x < expected.Width; x += 41)
                Assert.Equal(expected.GetPixel(x, y), fromView.GetPixel(x, y));
        }
    }

    [Fact]
    public void AfkDetection_IsOffByDefault_AndGatesTheOscListener()
    {
        var settings = new AppSettings();

        // 起動しただけでファイアウォールの許可の画面を出さないよう、既定はオフ（→実装メモ5.98）。
        Assert.False(settings.AfkDetectionEnabled);
        Assert.False(settings.NeedsAfkState);

        // AFK を使う設定をオンにしても、検知をオンにするまでは受け口を開かない。
        settings.PauseCountdownWhileAfk = true;
        Assert.False(settings.NeedsAfkState);

        settings.AfkDetectionEnabled = true;
        Assert.True(settings.NeedsAfkState);

        // AFK を使う設定がすべてオフでも、検知をオンにしたら開く（オンにしたその場で許可を求めるため）。
        settings.PauseCountdownWhileAfk = false;
        settings.ResetWarningEnabled = false;
        Assert.True(settings.NeedsAfkState);
    }

    [Theory]
    // afkDetectionEnabled がない古い設定ファイルは、それまでの決まり（AFK を使う設定のどれかがオンなら開く）で決める。
    [InlineData("""{ "pauseCountdownWhileAfk": true }""", true)]
    [InlineData("""{ "resetWarningEnabled": true, "resetWarningReshowAfterAfk": true }""", true)]
    [InlineData("""{ "resetWarningEnabled": true, "resetWarningReshowAfterAfk": false }""", false)]
    [InlineData("""{ "retentionMinutes": 60 }""", false)]
    // 書いてあれば、その値のまま。
    [InlineData("""{ "pauseCountdownWhileAfk": true, "afkDetectionEnabled": false }""", false)]
    [InlineData("""{ "AfkDetectionEnabled": true }""", true)]
    public void AfkDetection_MigratesOldSettingsFiles(string json, bool expected)
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, json);
        Assert.Equal(expected, AppSettings.Load(path, new NullDiagnostics()).AfkDetectionEnabled);
    }

    [Fact]
    public void AfkDetection_IsSavedAndReadBack()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """{ "retentionMinutes": 60 }""");
        var settings = AppSettings.Load(path, new NullDiagnostics());
        (DesktopSettings.From(settings) with { AfkDetectionEnabled = true }).ApplyTo(settings, SettingsField.AfkDetection);
        settings.SaveFields(path, SettingsField.AfkDetection);

        Assert.Contains("\"afkDetectionEnabled\": true", File.ReadAllText(path));
        Assert.True(AppSettings.Load(path, new NullDiagnostics()).AfkDetectionEnabled);
    }

    /// <summary>設定の部品をすべて1列に並べたもの（予告通知の部品も押せるよう、リセット予告アイコンをオンにしておく）。</summary>
    private static SettingsView AllSettings(List<DesktopCommand> commands)
    {
        var view = new SettingsView(new PanelStyle(), DesktopSettings.From(new AppSettings { ResetWarningEnabled = true }), commands.Add);
        view.Layout(new PointF(0f, 0f), 1f, columns: 1, SettingsSections.All);
        return view;
    }

    /// <summary>押して、送った設定を画面へ返す（主ループが画面へ返すのと同じ）。</summary>
    private static void Click(SettingsView view, List<DesktopCommand> commands, SettingsView.HitKind kind)
    {
        view.PointerDown(Center(view.TargetRect(kind)!.Value));
        view.PointerUp();
        view.SetSettings(Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]).Settings);
    }

    [Fact]
    public void AfkPause_TurningOnAlsoTurnsOnDetection_AndTurningBothOffTurnsItOff()
    {
        var commands = new List<DesktopCommand>();
        using var view = AllSettings(commands);

        // 検知がオフの間は、AFK を使う設定はオフに見え、押せる（グレーアウトしない→実装メモ5.99）。
        Assert.False(SettingsView.AfkPauseActive(view.Settings));

        // オンにすると、検知も一緒にオンにする（主ループがその場で受け口を開き、ファイアウォールの許可の画面が出る）。
        Click(view, commands, SettingsView.HitKind.AfkPause);
        var on = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.AfkPause | SettingsField.AfkDetection, on.Fields);
        Assert.True(on.Settings.PauseCountdownWhileAfk);
        Assert.True(on.Settings.AfkDetectionEnabled);

        // 「AFKから復帰時に再表示する」も、検知がオンになったのでオンに見える（値の既定はオン）。
        Assert.True(SettingsView.ReshowActive(view.Settings));

        // 片方をオフにしても、もう片方が働いていれば検知はオンのまま。
        Click(view, commands, SettingsView.HitKind.AfkPause);
        Assert.Equal(SettingsField.AfkPause, Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]).Fields);
        Assert.True(view.Settings.AfkDetectionEnabled);

        // 2つともオフにすると、検知もオフにする（受け口を閉じる）。
        Click(view, commands, SettingsView.HitKind.WarningReshow);
        var off = Assert.IsType<DesktopCommand.ChangeSettings>(commands[^1]);
        Assert.Equal(SettingsField.ResetWarning | SettingsField.AfkDetection, off.Fields);
        Assert.False(off.Settings.WarningReshowAfterAfk);
        Assert.False(off.Settings.AfkDetectionEnabled);
    }

    [Fact]
    public void WarningReshow_TurningOnAlsoTurnsOnDetection()
    {
        var commands = new List<DesktopCommand>();
        using var view = AllSettings(commands);

        // 値の既定はオンだが、検知がオフなのでオフに見える。押すとオンにして検知も一緒にオンにする。
        Assert.True(view.Settings.WarningReshowAfterAfk);
        Assert.False(SettingsView.ReshowActive(view.Settings));

        Click(view, commands, SettingsView.HitKind.WarningReshow);
        var on = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.ResetWarning | SettingsField.AfkDetection, on.Fields);
        Assert.True(on.Settings.WarningReshowAfterAfk);
        Assert.True(on.Settings.AfkDetectionEnabled);
    }

    [Fact]
    public void AfkSettings_WorkOnlyWhileDetecting()
    {
        // 値がオンでも、検知がオフなら働かない（主ループはこちらを使う）。
        Assert.False(new AppSettings { PauseCountdownWhileAfk = true }.AfkPauseActive);
        Assert.False(new AppSettings { ResetWarningReshowAfterAfk = true }.ResetWarningReshowActive);
        Assert.True(new AppSettings { PauseCountdownWhileAfk = true, AfkDetectionEnabled = true }.AfkPauseActive);
        Assert.True(new AppSettings { AfkDetectionEnabled = true }.ResetWarningReshowActive);
    }

    [Fact]
    public void WelcomeCompleted_IsOffByDefault_AndSavedInSettingsFile()
    {
        // 専用のファイルは作らず、settings.json の1項目にする（2026-09-30のユーザー指定→実装メモ5.100）。
        Assert.False(new AppSettings().WelcomeCompleted);

        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, """{ "retentionMinutes": 90 }""");
        var settings = AppSettings.Load(path, new NullDiagnostics());
        Assert.False(settings.WelcomeCompleted);

        settings.WelcomeCompleted = true;
        settings.SaveFields(path, SettingsField.Welcome);

        var text = File.ReadAllText(path);
        Assert.Contains("\"welcomeCompleted\": true", text);
        Assert.Contains("\"retentionMinutes\": 90", text);
        Assert.True(AppSettings.Load(path, new NullDiagnostics()).WelcomeCompleted);
    }
}
