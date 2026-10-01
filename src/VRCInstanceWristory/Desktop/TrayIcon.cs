using System.Runtime.InteropServices;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// タスクトレイのアイコン（→実装メモ5.41）。ウィンドウを最小化している間だけ置く。
/// ウィンドウのスレッドだけが触る。アイコンからの通知は <see cref="CallbackMessage"/> としてウィンドウへ届くので、
/// ウィンドウが <see cref="HandleMessage"/> へ渡し、返ってきた頼み（開く・終了）を行う。
/// </summary>
internal sealed class TrayIcon
{
    /// <summary>タスクトレイのアイコンを押したときにウィンドウへ届く。</summary>
    public const uint CallbackMessage = WM_APP + 2;

    private const uint IconId = 1;
    private const nuint MenuOpen = 1;
    private const nuint MenuExit = 2;

    private readonly IDiagnostics _log;

    public TrayIcon(IDiagnostics log) => _log = log;

    /// <summary>アイコンから頼まれたこと。</summary>
    public enum Request
    {
        None,

        /// <summary>ウィンドウを開く（ダブルクリック・キーボードで選んだ・メニューの「ウィンドウを開く」）。</summary>
        Open,

        /// <summary>アプリを終える（メニューの「… を終了」）。</summary>
        Exit,
    }

    /// <summary>いまタスクトレイにアイコンを置いているか。</summary>
    public bool Visible { get; private set; }

    /// <summary>
    /// アイコンを置く。置けないときは、見失わないようウィンドウを最小化のままタスクバーへ戻しておく。
    /// </summary>
    public void Add(nint hwnd, nint icon)
    {
        var data = Data(hwnd, icon);

        if (!Shell_NotifyIconW(NIM_ADD, ref data))
        {
            _log.Warn("タスクトレイにアイコンを置けませんでした。ウィンドウを最小化のままタスクバーへ戻します。");
            ShowWindow(hwnd, SW_SHOWMINNOACTIVE);
            return;
        }

        // 右クリックを新しい形（WM_CONTEXTMENU）で受け取る。ダブルクリックは WM_LBUTTONDBLCLK で届く。
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
        Visible = true;
    }

    /// <summary>アイコンを外す。置いていなければ何もしない。</summary>
    public void Remove(nint hwnd)
    {
        if (!Visible)
            return;

        var data = Data(hwnd, 0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        Visible = false;
    }

    /// <summary>
    /// 置いていたアイコンを置き直す（エクスプローラーが作り直されて消えたとき・表示倍率が変わってアイコンを描き直したとき）。
    /// 置いていなければ何もしない。
    /// </summary>
    public void Readd(nint hwnd, nint icon, bool explorerRestarted)
    {
        if (!Visible)
            return;

        // エクスプローラーが作り直されたときは、アイコンはもう消えている。
        if (explorerRestarted)
            Visible = false;
        else
            Remove(hwnd);

        Add(hwnd, icon);
    }

    /// <summary>アイコンからの通知（<see cref="CallbackMessage"/> の lParam）を読む。右クリックならメニューを出す。</summary>
    public Request HandleMessage(nint hwnd, nint lParam)
    {
        switch ((uint)LowWord(lParam))
        {
            // ウィンドウを開くのはダブルクリック（2026-09-27のユーザー指定→実装メモ5.68）。1回のクリック（NIN_SELECT）では開かない。
            // キーボードで選んだとき（NIN_KEYSELECT）はダブルクリックがないので、そのまま開く。
            case NIN_KEYSELECT:
            case WM_LBUTTONDBLCLK:
                return Request.Open;

            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                return ShowMenu(hwnd);

            default:
                return Request.None;
        }
    }

    /// <summary>右クリックのメニュー（開く・終了）。</summary>
    private static Request ShowMenu(nint hwnd)
    {
        var menu = CreatePopupMenu();

        try
        {
            AppendMenuW(menu, MF_STRING, MenuOpen, "ウィンドウを開く");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, MenuExit, $"{AppInfo.DisplayName} を終了");

            GetCursorPos(out var cursor);

            // 手前に出しておかないと、メニューの外を押しても閉じない（Win32の決まり）。
            SetForegroundWindow(hwnd);
            var chosen = (nuint)TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, cursor.X, cursor.Y, 0, hwnd, 0);

            return chosen switch
            {
                MenuOpen => Request.Open,
                MenuExit => Request.Exit,
                _ => Request.None,
            };
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static NOTIFYICONDATAW Data(nint hwnd, nint icon) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = hwnd,
        uID = IconId,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
        uCallbackMessage = CallbackMessage,
        hIcon = icon,
        szTip = $"{AppInfo.DisplayName}（ダブルクリックで開く）",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
        uVersion = NOTIFYICON_VERSION_4,
    };
}
