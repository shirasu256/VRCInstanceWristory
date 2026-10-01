using System.Runtime.InteropServices;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウに使うWin32 API（→実装メモ5.39）。
///
/// WinForms / WPF は使わない。どちらも実行に「Windowsデスクトップランタイム」が要り、
/// これまで .NET ランタイムだけで動いていた環境で起動できなくなるため。
/// 画面の中身はパネルと同じくGDI+で描くので、ウィンドウに要るのは枠・メッセージ・描画先の受け渡しだけで足りる。
/// </summary>
internal static class NativeMethods
{
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;

    public const uint CS_VREDRAW = 0x0001;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_DBLCLKS = 0x0008;

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SW_SHOWNORMAL = 1;

    public const int HTCLIENT = 1;
    public const int IDC_ARROW = 32512;
    public const int IDC_HAND = 32649;

    public const uint TME_LEAVE = 0x00000002;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public static readonly nint HWND_TOPMOST = -1;
    public static readonly nint HWND_NOTOPMOST = -2;

    /// <summary>メッセージ専用のウィンドウの親（クリップボードの持ち主→<see cref="ClipboardText"/>）。</summary>
    public static readonly nint HWND_MESSAGE = -3;

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2。</summary>
    public static readonly nint DpiAwarenessPerMonitorV2 = -4;

    public const uint SPI_GETWORKAREA = 0x0030;

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE。タイトルバーを暗い色にする（Windows 11）。</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClassW(string className, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(in MSG msg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(in MSG msg);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hwnd);

    /// <summary>そのウィンドウがまだあるか（例外で抜けたときの後片付け→<see cref="DesktopWindow"/>）。</summary>
    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint hwnd, in PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);

    /// <summary>一部だけを描き直す（状態の段の点の点滅→実装メモ5.91）。</summary>
    [DllImport("user32.dll", EntryPoint = "InvalidateRect")]
    public static extern bool InvalidateArea(nint hwnd, in RECT rect, bool erase);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint hwnd, nuint id, uint elapse, nint callback);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(nint hwnd, nuint id);

    [DllImport("user32.dll")]
    public static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT track);

    [DllImport("user32.dll")]
    public static extern nint LoadCursorW(nint instance, nint name);

    [DllImport("user32.dll")]
    public static extern nint SetCursor(nint cursor);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(nint hwnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    public static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfoW(uint action, uint param, out RECT rect, uint winIni);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? moduleName);

    // ------------------------------------------------------------------ アイコンとタスクトレイ（→実装メモ5.41）

    public const uint WM_SETICON = 0x0080;
    public const nint ICON_SMALL = 0;
    public const nint ICON_BIG = 1;

    public const nint SIZE_MINIMIZED = 1;
    public const int SW_HIDE = 0;
    public const int SW_RESTORE = 9;
    public const int SW_SHOWMINNOACTIVE = 7;

    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint NIN_KEYSELECT = 0x0401;

    public const uint NIM_ADD = 0;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NOTIFYICON_VERSION_4 = 4;

    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_SHOWTIP = 0x80;

    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;

    public const int SM_CXICON = 11;
    public const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? item);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    // ------------------------------------------------------------------ 二重起動（→実装メモ5.52）

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hwnd);

    // ------------------------------------------------------------------ 右クリックと、グループ名を打ち込む欄（→実装メモ5.42・5.48）

    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint EM_SETSEL = 0x00B1;
    public const uint EM_LIMITTEXT = 0x00C5;

    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_BORDER = 0x00800000;
    public const uint ES_AUTOHSCROLL = 0x0080;

    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;

    public const int GWLP_WNDPROC = -4;

    public const int FW_NORMAL = 400;
    public const uint DEFAULT_CHARSET = 1;
    public const uint CLEARTYPE_QUALITY = 5;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    public static extern nint CallWindowProcW(nint previous, nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hwnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint SetFocus(nint hwnd);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint handle);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(nint hdc, uint color);

    [DllImport("gdi32.dll")]
    public static extern uint SetBkColor(nint hdc, uint color);

    // ファイルを選ぶ画面（写真を開くアプリ→実装メモ5.55、グループ名の読み込み・書き出し→5.65）。

    public const int OFN_OVERWRITEPROMPT = 0x00000002;
    public const int OFN_HIDEREADONLY = 0x00000004;
    public const int OFN_NOCHANGEDIR = 0x00000008;
    public const int OFN_PATHMUSTEXIST = 0x00000800;
    public const int OFN_FILEMUSTEXIST = 0x00001000;
    public const int OFN_EXPLORER = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENFILENAMEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetOpenFileNameW(ref OPENFILENAMEW ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetSaveFileNameW(ref OPENFILENAMEW ofn);

    /// <summary>ダウンロードフォルダー（グループ名のインポートの始めの場所→実装メモ5.66）。</summary>
    public static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, nint token, out nint path);

    // 知らせの小さな画面（グループ名の読み込み・書き出しの結果→実装メモ5.65）。

    public const uint MB_OK = 0x00000000;
    public const uint MB_ICONERROR = 0x00000010;
    public const uint MB_ICONINFORMATION = 0x00000040;

    // 確かめる小さな画面（更新して再起動→実装メモ5.121）。既定のボタンは「いいえ」にする。
    public const uint MB_YESNO = 0x00000004;
    public const uint MB_DEFBUTTON2 = 0x00000100;
    public const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);

    /// <summary>GDI の色（0x00BBGGRR）。</summary>
    public static uint ColorRef(System.Drawing.Color color) => (uint)(color.R | (color.G << 8) | (color.B << 16));

    public static int LowWord(nint value) => unchecked((short)((long)value & 0xFFFF));

    public static int HighWord(nint value) => unchecked((short)(((long)value >> 16) & 0xFFFF));
}
