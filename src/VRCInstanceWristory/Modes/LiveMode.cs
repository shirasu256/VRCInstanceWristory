using VRCInstanceWristory.Cli;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// 通常動作の入口。二重起動・旧名の版を確かめ、出力の向け先と設定を決めてから、<see cref="LiveSession"/> を動かす。
/// </summary>
public static class LiveMode
{
    public static int Run(AppOptions options)
    {
        // v0.1はアプリ自体も単一起動とし、同じ保存先へ複数プロセスが書き込まないようにする。
        // 2つ目の起動は、1つ目のウィンドウを前に出してから終わる（2026-09-26のユーザー指定→実装メモ5.52）。
        using var mutex = new Mutex(initiallyOwned: true, SingleInstance.MutexName, out var createdNew);

        if (!createdNew)
            return HandOverToRunningInstance(options);

        try
        {
            return RunAsFirstInstance(options);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static int RunAsFirstInstance(AppOptions options)
    {
        // 旧名（InstanceIDLogger）の版が起動中なら、同じ手首に2枚出さず、保存先も引き継げないので起動しない（→実装メモ5.84）。
        if (SingleInstance.IsRunning(LegacyName.MutexName))
            return RefuseWhileLegacyRunning(options);

        // 旧名の保存先は、ログのファイルを作る前に移す（新しいフォルダーができてからでは移せない）。
        var migrated = LegacyName.MoveDataDirectory(LegacyName.DefaultDirectory, AppPaths.DataDirectory);

        // ターミナルから起動したのでなければ、出力は画面に出ないのでファイルへ書く（→実装メモ5.63）。
        if (!ConsoleHost.HasOutput)
            ConsoleHost.RedirectToLogFile();

        var log = new ConsoleDiagnostics(options.Verbose);

        if (migrated is not null)
            log.Info(migrated);

        var settingsPath = options.SettingsPath ?? AppPaths.Settings;
        var settings = AppSettings.Load(settingsPath, log);

        // Windowsのログオン時の起動は、設定ファイルではなくスタートアップの登録が正本（→実装メモ5.51）。
        var startup = StartupRegistration.ForCurrentUser();
        startup.MigrateLegacy(log);
        settings.LaunchAtLogon = startup.IsEnabled;

        // 外部からのコマンドで最後にリセットした時刻（→実装メモ5.83）。設定の画面の「最終実行」に出す。
        settings.ExternalResetLastRunUtc = ExternalResetRecord.Load(AppPaths.ExternalReset, log);

        var logDirectory = options.LogDirectory ?? settings.LogDirectory ?? LogDirectory.DefaultLogDirectory();

        Console.WriteLine("VRC Instance Wristory（通常動作）");
        Console.WriteLine($"  ログフォルダー : {logDirectory}");
        Console.WriteLine($"  設定ファイル   : {settingsPath}");
        Console.WriteLine($"  保存先         : {AppPaths.Checkpoint}");
        Console.WriteLine("  終了するには Ctrl+C を押すか、デスクトップのウィンドウを閉じてください。");
        Console.WriteLine();

        // 新しい版への入れ替えは、エンジンの片付け（チェックポイントの保存）まで済んでから頼む（→実装メモ5.121）。
        // 入れ替える Velopack は、このプロセスが終わるのを待ってから始める。
        int exitCode;
        LiveSession session;

        using (session = new LiveSession(options, settings, settingsPath, logDirectory, startup, log))
            exitCode = session.Run();

        // 片付けたあとでも、更新の仕組み（AppUpdater）はまだ使える。
        if (session.UpdatingOnExit)
            session.ApplyUpdateAfterExit();

        return exitCode;
    }

    /// <summary>2つ目の起動として、1つ目へ知らせてから終わる（→実装メモ5.52）。</summary>
    private static int HandOverToRunningInstance(AppOptions options)
    {
        if (SingleInstance.ActivateExisting(options.FromSteamVr))
            Console.WriteLine(options.FromSteamVr
                ? "VRC Instance Wristory は既に起動しています。SteamVRへつなぐよう知らせました。"
                : "VRC Instance Wristory は既に起動しています。そのウィンドウを前に出しました。");
        else
            Console.Error.WriteLine("VRC Instance Wristory は既に起動しています。二重に起動できません。");

        return ExitCode.AlreadyRunning;
    }

    /// <summary>旧名の版が起動中なので起動しない（→実装メモ5.84）。SteamVRが開いた起動のときは画面に何も出さない。</summary>
    private static int RefuseWhileLegacyRunning(AppOptions options)
    {
        const string text = "旧名（InstanceIDLogger）の版が起動しています。タスクトレイから終了してから、もう一度起動してください。";
        Console.Error.WriteLine(text);

        if (!options.FromSteamVr)
            NativeMethods.MessageBoxW(0, text, AppInfo.DisplayName, NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);

        return ExitCode.AlreadyRunning;
    }
}
