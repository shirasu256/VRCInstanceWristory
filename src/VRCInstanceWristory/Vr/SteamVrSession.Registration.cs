using System.Text;
using VRCInstanceWristory.Infrastructure;
using Valve.VR;

namespace VRCInstanceWristory.Vr;

/// <summary>
/// SteamVR へのアプリの登録（binding の編集画面に出す・「SteamVRと一緒に起動する」→実装メモ5.50）と、旧名の登録の片付け（→実装メモ5.84）。
/// </summary>
public sealed partial class SteamVrSession
{
    /// <summary>
    /// アプリの登録（<c>AddApplicationManifest</c>）に成功したか（→実装メモ5.85）。
    /// 失敗すると「SteamVRと一緒に起動する」と、SteamVR の割り当ての画面に出すことができない。
    /// </summary>
    public bool ApplicationRegistered { get; private set; }

    /// <summary>登録した manifest の場所。「SteamVRと一緒に起動する」を入れたときに、残す登録で登録し直すのに使う。</summary>
    private string? _manifestPath;

    /// <summary>
    /// SteamVRへアプリを識別させ、binding編集画面に出せるようにする。
    ///
    /// ふだんは一時的な登録（SteamVRを終了すると消える）で、手動起動の運用は変えない。
    /// 「SteamVRと一緒に起動する」を選んでいるとき（→実装メモ5.50）だけは、いまの場所で登録し直して残す
    /// （フォルダーを移していても、起動されるのはいま動いている実行ファイルになる）。
    /// </summary>
    public void RegisterApplicationManifest(string manifestPath)
    {
        if (!_initialized || OpenVR.Applications is null)
            return;

        var full = Path.GetFullPath(manifestPath);
        if (!File.Exists(full))
        {
            log.Warn($"アプリケーションmanifestが見つかりません: {full}");
            ApplicationRegistered = false;
            return;
        }

        _manifestPath = full;

        var legacyAutoLaunch = RemoveLegacyApplication();
        var permanent = legacyAutoLaunch || OpenVR.Applications.GetApplicationAutoLaunch(ApplicationKey);
        var error = OpenVR.Applications.AddApplicationManifest(full, bTemporary: !permanent);
        ApplicationRegistered = error == EVRApplicationError.None;

        if (!ApplicationRegistered)
            log.Warn($"AddApplicationManifest: {error}");

        if (legacyAutoLaunch)
        {
            var autoLaunch = OpenVR.Applications.SetApplicationAutoLaunch(ApplicationKey, true);
            if (autoLaunch != EVRApplicationError.None)
                log.Warn($"旧名の「SteamVRと一緒に起動する」を引き継げません: {autoLaunch}");
        }

        var pid = (uint)Environment.ProcessId;
        var identify = OpenVR.Applications.IdentifyApplication(pid, ApplicationKey);
        if (identify != EVRApplicationError.None)
            log.Info($"IdentifyApplication: {identify}");
    }

    /// <summary>
    /// 旧名（<see cref="LegacyName.SteamVrApplicationKey"/>）の登録を外す（→実装メモ5.84）。
    /// 残すと、SteamVRが旧い実行ファイルを起動し続ける。旧名の登録が自動起動だったかを返す（引き継ぐため）。
    /// </summary>
    private bool RemoveLegacyApplication()
    {
        var applications = OpenVR.Applications;
        if (!applications.IsApplicationInstalled(LegacyName.SteamVrApplicationKey))
            return false;

        var autoLaunch = applications.GetApplicationAutoLaunch(LegacyName.SteamVrApplicationKey);
        applications.SetApplicationAutoLaunch(LegacyName.SteamVrApplicationKey, false);

        // manifest の場所は登録から読めないので、実行ファイルの場所から作る（旧版は実行ファイルの隣の Resources に置いていた）。
        var error = EVRApplicationError.None;
        var buffer = new StringBuilder(1024);
        applications.GetApplicationPropertyString(LegacyName.SteamVrApplicationKey, EVRApplicationProperty.BinaryPath_String, buffer, (uint)buffer.Capacity, ref error);

        if (error == EVRApplicationError.None && Path.GetDirectoryName(buffer.ToString()) is { Length: > 0 } directory)
        {
            var manifest = Path.Combine(directory, "Resources", LegacyName.ManifestFileName);
            var remove = applications.RemoveApplicationManifest(manifest);
            log.Info($"旧名のSteamVRの登録を外しました（{remove}・自動起動 {(autoLaunch ? "オン" : "オフ")}）: {manifest}");
        }
        else
        {
            log.Warn($"旧名のSteamVRの登録の場所を読めません: {error}。自動起動だけを切りました。");
        }

        return autoLaunch;
    }

    /// <summary>
    /// SteamVRの起動と一緒にこのアプリを起動するよう登録してあるか（→実装メモ5.50）。
    /// 正本はSteamVRの設定（「起動時に開始するオーバーレイ」）で、ここはそれを読むだけ。
    /// </summary>
    public bool GetAutoLaunch()
        => _initialized && OpenVR.Applications is not null && OpenVR.Applications.GetApplicationAutoLaunch(ApplicationKey);

    /// <summary>
    /// SteamVRと一緒に起動する・しないを切り替える（→実装メモ5.50）。
    ///
    /// 起動してもらうには、SteamVRを終了しても消えない登録が要るので、入れるときは manifest を残す形で登録し直す
    /// （<c>AddApplicationManifest(…, bTemporary: false)</c>）。外すときは自動起動だけを切り、登録は残す
    /// （SteamVRの設定画面からも入れ直せるようにするため。OVR Advanced Settings などと同じ）。
    /// 結果として登録されているかを返す。
    /// </summary>
    public bool SetAutoLaunch(bool enabled)
    {
        if (!_initialized || OpenVR.Applications is null)
            return false;

        if (enabled && _manifestPath is { } manifest)
        {
            var add = OpenVR.Applications.AddApplicationManifest(manifest, bTemporary: false);
            if (add != EVRApplicationError.None)
                log.Warn($"AddApplicationManifest（残す登録）: {add}");
        }

        var error = OpenVR.Applications.SetApplicationAutoLaunch(ApplicationKey, enabled);
        if (error != EVRApplicationError.None)
            log.Error($"SteamVRへの自動起動の登録を変えられません: {error}");

        return GetAutoLaunch();
    }
}
