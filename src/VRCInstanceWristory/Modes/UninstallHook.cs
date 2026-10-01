using Valve.VR;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// アンインストールするときの片付け（Velopack の <c>OnBeforeUninstallFastCallback</c>→実装メモ5.121）。
///
/// 本体（インストール先）は Velopack が消す。ここでは、インストール先の外に残る登録だけを外す。
/// Velopack の決まりで、画面は出せず、30秒以内に終える必要がある。
///
/// | 残るもの | 片付け方 |
/// | --- | --- |
/// | Windowsのログオン時の起動（<c>HKCU\…\Run</c>） | 値を消す（<see cref="StartupRegistration.Remove"/>） |
/// | SteamVRへの登録（「SteamVRと一緒に起動する」） | SteamVR が動いていれば、自動起動を切って manifest の登録を外す |
/// | 保存先（<c>%LocalAppData%\VRCInstanceWristory</c>） | 消さない（入れ直したときに設定と訪問履歴を引き継ぐため。消し方は README に書く） |
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
