using System.Text.Json.Nodes;
using VRCInstanceWristory.Cli;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// SteamVRと一緒に起動する（→実装メモ5.50）・Windowsのログオン時に起動する（→実装メモ5.51）の登録と、起動の引数
/// （いずれも2026-09-26のユーザー指定）。
///
/// どちらも利用者の環境（SteamVRの登録・レジストリ）を変えるものなので、自動検証では本物に触れない。
/// レジストリは差し替えた入れ物（<see cref="FakeStartupRegistry"/>）で確かめる。
/// SteamVRの見張りは <see cref="SteamVrWatcherTests"/>、旧名からの引き継ぎは <see cref="LegacyNameTests"/>、
/// 二重起動は <see cref="SingleInstanceTests"/> にある。
/// </summary>
public class StartupTests
{
    private const string Exe = @"C:\Apps\VRC Instance Wristory\VRCInstanceWristory.exe";

    // ------------------------------------------------------------------ Windowsのログオン時（→5.51）

    [Fact]
    public void ログオン時の起動はこの実行ファイルをタスクトレイに入れて起動する1行を書く()
    {
        var registry = new FakeStartupRegistry();
        var startup = new StartupRegistration(registry, Exe);

        Assert.False(startup.IsEnabled);
        Assert.True(startup.SetEnabled(true));

        // パスは空白を含むので引用符で囲み、--minimized を付ける。
        Assert.Equal($"\"{Exe}\" --minimized", registry.Run[StartupRegistration.ValueName]);

        Assert.False(startup.SetEnabled(false));
        Assert.Empty(registry.Run);
    }

    [Fact]
    public void タスクマネージャーで無効にされていれば登録していない扱いにし_登録し直すと有効に戻す()
    {
        var registry = new FakeStartupRegistry();
        var startup = new StartupRegistration(registry, Exe);
        startup.SetEnabled(true);

        // タスクマネージャーの「無効」は、先頭のバイトが奇数（0x03）。
        registry.Approved[StartupRegistration.ValueName] = [0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];
        Assert.False(startup.IsEnabled);

        registry.Approved[StartupRegistration.ValueName] = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.True(startup.IsEnabled);

        registry.Approved[StartupRegistration.ValueName] = [0x03];
        Assert.True(startup.SetEnabled(true));
        Assert.False(registry.Approved.ContainsKey(StartupRegistration.ValueName));
    }

    [Fact]
    public void 別の場所の実行ファイルを指す登録は登録していない扱いにし_登録し直すとこちらを指す()
    {
        var registry = new FakeStartupRegistry();
        registry.Run[StartupRegistration.ValueName] = "\"D:\\old\\VRCInstanceWristory.exe\" --minimized";

        var startup = new StartupRegistration(registry, Exe);
        Assert.False(startup.IsEnabled);

        startup.SetEnabled(true);
        Assert.Equal(Exe, StartupRegistration.ExecutableOf(registry.Run[StartupRegistration.ValueName]));
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\" --minimized", "C:\\a b\\x.exe")]
    [InlineData("C:\\x.exe --minimized", "C:\\x.exe")]
    [InlineData("C:\\x.exe", "C:\\x.exe")]
    public void 登録の1行から実行ファイルを取り出す(string command, string expected)
        => Assert.Equal(expected, StartupRegistration.ExecutableOf(command));

    [Fact]
    public void 起動の引数を読む()
    {
        var options = CommandLine.Parse(["--minimized", "--from-steamvr", "--verbose"]);

        Assert.True(options.Minimized);
        Assert.True(options.FromSteamVr);
        Assert.Equal(AppMode.Live, options.Mode);
        Assert.False(options.ShowHelp);
    }

    // ------------------------------------------------------------------ SteamVRと一緒に（→5.50）

    [Fact]
    public void SteamVRが起動するときはタスクトレイに入れて_SteamVRから開いたと分かる引数を付ける()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Resources, "vrcinstancewristory.vrmanifest")))!;
        var app = manifest["applications"]![0]!;

        Assert.Equal(SteamVrSession.ApplicationKey, (string?)app["app_key"]);
        Assert.True((bool)app["is_dashboard_overlay"]!);

        var arguments = ((string?)app["arguments"])!.Split(' ');
        Assert.Contains("--minimized", arguments);
        Assert.Contains("--from-steamvr", arguments);

        var options = CommandLine.Parse(arguments);
        Assert.True(options.Minimized && options.FromSteamVr && !options.ShowHelp);
    }

    [Fact]
    public void SteamVRにつながっていない間はSteamVRと一緒の起動を切り替えられない()
    {
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        // 「起動」は一般設定の下のほう（→実装メモ5.72）。送らずに全部見える高さにする。
        view.Resize(new System.Drawing.Size(1060, 1400), 1f);
        view.SelectTab(DesktopTab.Startup);

        var check = view.TargetRect(SettingsView.HitKind.LaunchWithSteamVr)!.Value;

        view.MouseMove(SampleWindow.Center(check));
        Assert.False(view.IsClickable(SampleWindow.Center(check)));
        view.MouseDown(SampleWindow.Center(check));
        view.MouseUp();
        Assert.Empty(commands);

        // つながって登録の状態が分かれば押せる。結果は主ループが返してくる。
        view.SetStartupState(launchWithSteamVr: false, launchAtLogon: false);
        view.MouseDown(SampleWindow.Center(check));
        view.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.LaunchWithSteamVr, change.Fields);
        Assert.True(change.Settings.LaunchWithSteamVr);
    }

    [Fact]
    public void ログオン時の起動のチェックは登録を変える命令を送り_設定ファイルには書かない()
    {
        var commands = new List<DesktopCommand>();
        using var view = new DesktopView(new PanelStyle(), DesktopSettings.From(new AppSettings()), commands.Add);
        view.Resize(new System.Drawing.Size(1060, 1400), 1f);
        view.SelectTab(DesktopTab.Startup);

        var check = view.TargetRect(SettingsView.HitKind.LaunchAtLogon)!.Value;
        view.MouseMove(SampleWindow.Center(check));
        view.MouseDown(SampleWindow.Center(check));
        view.MouseUp();

        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(SettingsField.LaunchAtLogon, change.Fields);
        Assert.True(change.Settings.LaunchAtLogon);
        Assert.True((change.Fields & DesktopSettings.StartupFields) == change.Fields);

        // 設定ファイルへ写しても、ファイルには書かない（正本はレジストリ）。
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        File.WriteAllText(path, "{ \"idFontPixels\": 44 }");
        var settings = AppSettings.Load(path, new CollectingDiagnostics());
        change.Settings.ApplyTo(settings, change.Fields);
        settings.SaveFields(path, change.Fields);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("launch", text, StringComparison.OrdinalIgnoreCase);
    }
}
