using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 二重起動を防ぐ（2026-09-26のユーザー指定→実装メモ5.52）。
///
/// 1つ目の起動が名前付きの Mutex を持ち、2つ目の起動はそれを見つけたら何もせずに終わる。
/// そのとき1つ目のウィンドウへ知らせて前に出させる（タスクトレイに入っていても戻す）。
/// ウィンドウを前に出す権利は、利用者が起動した2つ目の側にあるので、先に <c>AllowSetForegroundWindow</c> で1つ目へ譲る。
///
/// SteamVR が起動のついでに開いた2つ目（<c>--from-steamvr</c>）のときは、ウィンドウを前に出さない
/// （利用者はVRの中にいる）。1つ目にはSteamVRへつなぐきっかけだけを渡す（→実装メモ5.50）。
/// </summary>
public static class SingleInstance
{
    /// <summary>1つ目の起動が持つ Mutex の名前（同じサインインの中で1つ）。</summary>
    public const string MutexName = @"Local\VRCInstanceWristory.v1";

    /// <summary>2つ目の起動から1つ目のウィンドウへ送る合図（RegisterWindowMessage の名前）。</summary>
    public const string ActivateMessageName = "VRCInstanceWristory.Activate";

    /// <summary>合図の wParam。ウィンドウを前に出す。</summary>
    public const nint ShowWindowRequest = 0;

    /// <summary>合図の wParam。SteamVR が開いた起動なので前には出さず、SteamVRへつなぐきっかけだけにする。</summary>
    public const nint FromSteamVrRequest = 1;

    /// <summary>1つ目の起動がいるか。Mutex を持たずに確かめる（持つのは1つ目になると決まった後）。</summary>
    public static bool IsRunning(string name = MutexName)
    {
        if (!Mutex.TryOpenExisting(name, out var existing))
            return false;

        existing.Dispose();
        return true;
    }

    /// <summary>
    /// 1つ目の起動のウィンドウへ合図を送る。ウィンドウが見つからなければ false
    /// （<c>--no-window</c> で動いている・ウィンドウを作れなかった）。
    /// </summary>
    public static bool ActivateExisting(bool fromSteamVr)
    {
        var hwnd = FindWindowW(DesktopWindow.ClassName, null);
        if (hwnd == 0)
            return false;

        if (!fromSteamVr && GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid != 0)
            AllowSetForegroundWindow(pid);

        var message = RegisterWindowMessageW(ActivateMessageName);
        return message != 0 && PostMessageW(hwnd, message, fromSteamVr ? FromSteamVrRequest : ShowWindowRequest, 0);
    }
}
