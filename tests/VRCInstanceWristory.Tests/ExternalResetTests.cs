using System.Diagnostics;
using System.Drawing;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 外部からの履歴リセットのコマンド（2026-09-28のユーザー指定→実装メモ5.83）。
///
/// OyasumiVR の「コマンドの実行」は、書いた内容を一時的な .bat にして cmd で実行する。同じ動かし方で、
/// cmd の echo が名前付きパイプへ届くことを確かめる。パイプの名前は検証ごとに変える
/// （本物の名前 <see cref="ExternalCommandListener.DefaultPipeName"/> は起動中のアプリが持っているので使わない）。
/// </summary>
public class ExternalResetTests
{
    private static string TestPipeName() => "VRCInstanceWristory.test." + Guid.NewGuid().ToString("N");

    /// <summary>OyasumiVR と同じく、コマンドを .bat に書いて cmd で実行する。ウィンドウは出さない。</summary>
    private static void RunLikeOyasumiVr(string commands)
    {
        using var tempFile = new TempFile("reset", ".bat");
        File.WriteAllText(tempFile.Path, commands);

        var start = new ProcessStartInfo("cmd.exe", $"/C \"{tempFile.Path}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10000));
    }

    private static bool WaitFor(ExternalCommandListener listener, out ExternalCommand command, int milliseconds = 3000)
    {
        ExternalCommand taken = default;
        var received = Eventually.True(() => listener.TryTake(out taken), TimeSpan.FromMilliseconds(milliseconds));
        command = taken;
        return received;
    }

    /// <summary>
    /// 受け口がそのパイプを作ったか。パイプの一覧を読むだけで、つなぎには行かない
    /// （<see cref="File.Exists"/> で確かめると、それが1回の接続として受け口に使われてしまう）。
    /// </summary>
    private static bool PipeExists(string name)
        => Directory.GetFiles(@"\\.\pipe\").Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// コマンドを受け口へ届くまで書き直す。受け口がパイプを作る前（受け取った直後の作り直しの間も）に書くと、
    /// cmd は「見つかりません」で終わって何も届かない。一度だけ眠ってから書くと、PC が混んでいるときに間に合わない。
    /// </summary>
    private static bool SendUntilReceived(ExternalCommandListener listener, string commandLine, out ExternalCommand command)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < Eventually.DefaultTimeout)
        {
            RunLikeOyasumiVr(commandLine);

            // cmd はもう終わっているので、書けていれば受け口はすぐに受け取る。
            if (WaitFor(listener, out command, 1000))
                return true;
        }

        command = default;
        return false;
    }

    [Fact]
    public void コマンドはcmdの1行でパイプへ書く()
    {
        Assert.Equal(@"echo reset>\\.\pipe\VRCInstanceWristory", ExternalCommandListener.CommandLine());
    }

    [Theory]
    [InlineData("reset\r\n")]
    [InlineData(" RESET ")]
    [InlineData("reset")]
    public void リセットの語を読む(string text)
    {
        Assert.Equal(ExternalCommand.ResetHistory, ExternalCommandListener.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("reset-history")]
    [InlineData("rm -rf")]
    public void 知らない語は無視する(string text)
    {
        Assert.Null(ExternalCommandListener.Parse(text));
    }

    // cmd.exe を起動して名前付きパイプへ書くので、ほかの検証より遅く、PC の混み具合の影響を受ける。

    [Fact]
    [Trait("Category", "Integration")]
    public void cmdのechoで書いたコマンドが届く()
    {
        var name = TestPipeName();
        using var listener = new ExternalCommandListener(name, new CollectingDiagnostics());
        listener.Start();

        Assert.True(SendUntilReceived(listener, ExternalCommandListener.CommandLine(name), out var command));
        Assert.Equal(ExternalCommand.ResetHistory, command);

        // 続けてもう1回書いても届く（受け終わったらパイプを作り直している）。
        Assert.True(SendUntilReceived(listener, ExternalCommandListener.CommandLine(name), out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void 知らない語は積まない()
    {
        var name = TestPipeName();
        using var listener = new ExternalCommandListener(name, new CollectingDiagnostics());
        listener.Start();

        // 受け口がパイプを作るのを待ってから書く（作る前に書くと、cmd が「見つかりません」で終わり、何も確かめられない）。
        Assert.True(Eventually.True(() => PipeExists(name)));

        RunLikeOyasumiVr($@"echo hello>\\.\pipe\{name}");
        Assert.False(WaitFor(listener, out _, 500));

        // その後の正しいコマンドは届く。
        Assert.True(SendUntilReceived(listener, ExternalCommandListener.CommandLine(name), out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void 受け付けをやめるとパイプを閉じる()
    {
        var name = TestPipeName();
        using var listener = new ExternalCommandListener(name, new CollectingDiagnostics());

        listener.SetEnabled(true);
        Assert.True(listener.Listening);

        listener.SetEnabled(false);
        Assert.False(listener.Listening);

        // パイプがないので cmd は「見つかりません」で終わり、何も届かない。
        RunLikeOyasumiVr(ExternalCommandListener.CommandLine(name));
        Assert.False(WaitFor(listener, out _, 300));

        // もう一度オンにすれば受け付ける。
        listener.SetEnabled(true);
        Assert.True(SendUntilReceived(listener, ExternalCommandListener.CommandLine(name), out _));
    }

    /// <summary>思わぬ例外で受け口が止まったら、黙って「受付中」のままにせず、PC側へ知らせて止まったことにする。</summary>
    [Fact]
    public void 思わぬ例外で止まったら受付中にしない()
    {
        // "anonymous" はパイプの名前に使えず、作るときに IOException 以外の例外になる。
        var log = new CollectingDiagnostics();
        using var listener = new ExternalCommandListener("anonymous", log);
        listener.Start();

        Assert.True(Eventually.True(() => !listener.Listening));
        Assert.Contains(log.Messages, m => m.StartsWith("ERROR") && m.Contains("受け口が止まりました"));
    }

    [Fact]
    public void 最終実行の時刻を保存して読み戻す()
    {
        using var tempDirectory = new TempDirectory("reset");
        var directory = tempDirectory.Path;
        var path = Path.Combine(directory, "external-reset.json");

        Assert.Null(ExternalResetRecord.Load(path));

        var at = new DateTime(2026, 9, 28, 14, 30, 0, DateTimeKind.Utc);
        ExternalResetRecord.Save(path, at);

        var loaded = ExternalResetRecord.Load(path);
        Assert.Equal(at, loaded);
        Assert.Equal(DateTimeKind.Utc, loaded!.Value.Kind);

        // 壊れていれば、なかったものとして読む。
        File.WriteAllText(path, "{");
        var log = new CollectingDiagnostics();
        Assert.Null(ExternalResetRecord.Load(path, log));
        Assert.Contains(log.Messages, m => m.StartsWith("WARN"));
    }

    [Fact]
    public void 最終実行の表示()
    {
        Assert.Equal("コマンド実行履歴無し", SettingsView.LastRunText(null));

        var at = new DateTime(2026, 9, 28, 14, 30, 0, DateTimeKind.Utc);
        Assert.Equal($"最終実行: {at.ToLocalTime():yyyy-MM-dd HH:mm}", SettingsView.LastRunText(at));
    }

    [Fact]
    public void 受け付けるかは既定でオンで設定ファイルへ書き戻す()
    {
        Assert.True(new AppSettings().ExternalResetEnabled);

        using var tempDirectory = new TempDirectory("reset");
        var directory = tempDirectory.Path;
        var path = Path.Combine(directory, "settings.json");

        var settings = new AppSettings();
        settings.Save(path);

        (DesktopSettings.From(settings) with { ExternalResetEnabled = false }).ApplyTo(settings, SettingsField.ExternalReset);
        Assert.False(settings.ExternalResetEnabled);
        settings.SaveFields(path, SettingsField.ExternalReset);

        var reloaded = AppSettings.Load(path, new CollectingDiagnostics());
        Assert.False(reloaded.ExternalResetEnabled);

        // 最終実行の時刻は設定ファイルには書かない。
        settings.ExternalResetLastRunUtc = DateTime.UtcNow;
        settings.Save(path);
        Assert.DoesNotContain("externalResetLastRun", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 一般設定の外部連携で切り替えとコピーを送る()
    {
        var commands = new List<DesktopCommand>();
        using var view = SampleWindow.Create(commands);

        // 一般設定の一番下にあるので、送らずに全部見える高さにする。
        view.Resize(new Size(1060, 1800), 1f);
        view.SelectTab(DesktopTab.Startup);

        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.ExternalReset)!.Value);
        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.False(change.Settings.ExternalResetEnabled);
        Assert.Equal(SettingsField.ExternalReset, change.Fields);

        SampleWindow.Click(view, view.TargetRect(SettingsView.HitKind.CopyCommand)!.Value);
        var copy = Assert.IsType<DesktopCommand.CopyText>(commands[^1]);
        Assert.Equal(ExternalCommandListener.CommandLine(), copy.Text);

        // コマンドの枠・最終実行・説明は、切り替えの下に順に並ぶ（コピーは切り替えより下）。
        Assert.True(view.TargetRect(SettingsView.HitKind.CopyCommand)!.Value.Y > view.TargetRect(SettingsView.HitKind.ExternalReset)!.Value.Bottom);
    }

    /// <summary>「外部連携」はダッシュボードには出さない（2026-09-29のユーザー指定→実装メモ5.90）。ウィンドウだけ。</summary>
    [Fact]
    public void 外部連携はダッシュボードには出さない()
    {
        using var dashboard = new SettingsDashboardView(new PanelStyle(), DesktopSettings.From(new AppSettings()), _ => { });

        Assert.Null(dashboard.TargetRect(SettingsView.HitKind.ExternalReset));
        Assert.Null(dashboard.TargetRect(SettingsView.HitKind.CopyCommand));
    }
}
