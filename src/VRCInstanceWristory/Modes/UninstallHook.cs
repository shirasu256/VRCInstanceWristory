using Valve.VR;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// アンインストールするときの片付け（Velopack の <c>OnBeforeUninstallFastCallback</c>→実装メモ5.121）。
///
/// 本体（インストール先）は Velopack が消す。ここでは、インストール先の外に残るもの（登録と保存先）を片付ける。
/// Velopack の決まりで、画面は出せず、30秒以内に終える必要がある。
///
/// | 残るもの | 片付け方 |
/// | --- | --- |
/// | Windowsのログオン時の起動（<c>HKCU\…\Run</c>） | 値を消す（<see cref="StartupRegistration.Remove"/>） |
/// | SteamVRへの登録（「SteamVRと一緒に起動する」） | SteamVR が動いていれば、自動起動を切って manifest の登録を外す |
/// | 保存先（<c>%LocalAppData%\VRCInstanceWristory</c>） | 設定・訪問履歴・目印・サムネイル・ログをフォルダーごと消す（2026-10-01のユーザー指定→実装メモ5.124。5.121 では残していた）。VRChat の写真には触れない |
///
/// SteamVR が動いていなければ、SteamVR への登録には触れない。つなぐと SteamVR を起動させてしまう（→実装メモ5.51・5.57）。
/// 登録は残っても、指している実行ファイルがないので、SteamVR は何も起動しない。
/// </summary>
public static class UninstallHook
{
    public static void Run()
    {
        try
        {
            StartupRegistration.ForCurrentUser().Remove();
        }
        catch (Exception)
        {
            // 片付けに失敗しても、アンインストールは止めない（止める手段もない）。
        }

        try
        {
            if (SteamVrWatcher.FindServer() is not null)
                RemoveSteamVrRegistration(Path.Combine(AppContext.BaseDirectory, "Resources", "vrcinstancewristory.vrmanifest"));
        }
        catch (Exception)
        {
            // openvr_api.dll を読めないなど。同上。
        }

        DeleteData(AppPaths.DataDirectory);
    }

    /// <summary>
    /// 保存先のフォルダーを消す（→実装メモ5.124）。名前がこのアプリの保存先（<see cref="AppInfo.InternalName"/>）でなければ何も消さない。
    /// アプリは Velopack が先に終わらせているが、ウイルス対策ソフトなどが一瞬掴んでいることがあるので、少し待って試し直す。
    /// 消えたか（もともとなかったときも）を返す。
    /// </summary>
    public static bool DeleteData(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

        if (!string.Equals(Path.GetFileName(full), AppInfo.InternalName, StringComparison.OrdinalIgnoreCase))
            return false;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(full))
                    Directory.Delete(full, recursive: true);

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }

        return false;
    }

    private static void RemoveSteamVrRegistration(string manifest)
    {
        var error = EVRInitError.None;

        // 設定を読み書きするだけの種類でつなぐ（手首のパネルは出さない）。
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Utility);

        if (error != EVRInitError.None)
            return;

        try
        {
            var applications = OpenVR.Applications;

            if (applications is null)
                return;

            if (applications.IsApplicationInstalled(SteamVrSession.ApplicationKey))
                applications.SetApplicationAutoLaunch(SteamVrSession.ApplicationKey, false);

            applications.RemoveApplicationManifest(manifest);
        }
        finally
        {
            OpenVR.Shutdown();
        }
    }
}
