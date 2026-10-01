using System.Drawing;
using System.Runtime.InteropServices;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// グループ名を打ち込む欄（Win32 の EDIT・→実装メモ5.48）。開いている間だけウィンドウの子として置く。
/// EDIT をそのまま使うので、日本語入力（IME）も使える。Enter で確定、Esc でやめる。欄から離れても確定する。
///
/// 欄からの「閉じて」はその場では処理せず、<see cref="DoneMessage"/> を親のウィンドウへ送る（自分のメッセージの処理の中で自分を壊さない）。
/// 親は受け取ったら <see cref="Commit"/> か <see cref="Cancel"/> を呼ぶ。ウィンドウのスレッドだけが触る。
/// </summary>
internal sealed class InlineTextEdit : IDisposable
{
    /// <summary>欄から「閉じて」と頼むときに親へ届く（wParam が 1 なら確定、0 ならやめる）。</summary>
    public const uint DoneMessage = WM_APP + 3;

    private readonly PanelStyle _style;
    private readonly IDiagnostics _log;

    // 欄の横取りの手続き。ネイティブ側が持つ間、集められないよう持っておく。
    private readonly WndProc _proc;

    private nint _edit;
    private nint _parent;
    private nint _previousProc;
    private nint _font;
    private nint _brush;
    private string? _groupId;

    public InlineTextEdit(PanelStyle style, IDiagnostics log)
    {
        _style = style;
        _log = log;
        _proc = EditProc;
    }

    /// <summary>
    /// 欄を置く。開いていた欄は何も変えずに閉じる。
    /// </summary>
    public void Open(nint parent, TextEditRequest request)
    {
        Cancel();

        var rect = Rectangle.Round(request.Rect);
        var fontHeight = -(int)MathF.Round(request.FontPixels);

        _font = CreateFontW(fontHeight, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, request.FontFamily);
        _brush = CreateSolidBrush(ColorRef(_style.Header));

        _edit = CreateWindowExW(
            0,
            "EDIT",
            request.Initial,
            WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
            rect.X,
            rect.Y,
            Math.Max(40, rect.Width),
            Math.Max(16, rect.Height),
            parent,
            0,
            GetModuleHandleW(null),
            0);

        if (_edit == 0)
        {
            _log.Error($"グループ名を打ち込む欄を作れません（{Marshal.GetLastWin32Error()}）。");
            Cancel();
            return;
        }

        _parent = parent;
        _groupId = request.GroupId;

        SendMessageW(_edit, WM_SETFONT, _font, 1);
        SendMessageW(_edit, EM_LIMITTEXT, AppSettings.MaxGroupNameLength, 0);
        SendMessageW(_edit, EM_SETSEL, 0, -1);

        // Enter と Esc を拾うため、欄のメッセージを横取りする（標準の EDIT は1行だと Enter を無視するだけ）。
        _previousProc = SetWindowLongPtrW(_edit, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));

        SetFocus(_edit);
    }

    /// <summary>打ち込んだ名前を取り出して欄を閉じる。開いていなければ null。</summary>
    public (string GroupId, string Text)? Commit()
    {
        if (_edit == 0 || _groupId is not { } groupId)
            return null;

        var length = GetWindowTextLengthW(_edit);
        var buffer = new char[Math.Max(1, length + 1)];
        var read = GetWindowTextW(_edit, buffer, buffer.Length);
        var text = new string(buffer, 0, Math.Max(0, read));

        Cancel();
        return (groupId, text);
    }

    /// <summary>何も変えずに欄を閉じる。</summary>
    public void Cancel()
    {
        var edit = _edit;
        _edit = 0;
        _groupId = null;

        if (edit != 0)
        {
            if (_previousProc != 0)
                SetWindowLongPtrW(edit, GWLP_WNDPROC, _previousProc);

            DestroyWindow(edit);
        }

        _previousProc = 0;

        if (_font != 0)
            DeleteObject(_font);

        if (_brush != 0)
            DeleteObject(_brush);

        _font = 0;
        _brush = 0;
    }

    /// <summary>
    /// 親が受けた <c>WM_CTLCOLOREDIT</c>。この欄なら、打ち込む欄もパネルと同じ暗い配色にして、塗りの筆を返す。
    /// </summary>
    public bool TryColor(nint hdc, nint control, out nint brush)
    {
        brush = 0;

        if (_edit == 0 || control != _edit)
            return false;

        SetTextColor(hdc, ColorRef(_style.Text));
        SetBkColor(hdc, ColorRef(_style.Header));
        brush = _brush;
        return true;
    }

    private nint EditProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            // 欄を壊すのは、この欄のメッセージを返したあと（自分の処理の中で自分を壊さない）。
            switch (message)
            {
                case WM_KEYDOWN when wParam == VK_RETURN:
                    PostMessageW(_parent, DoneMessage, 1, 0);
                    return 0;

                case WM_KEYDOWN when wParam == VK_ESCAPE:
                    PostMessageW(_parent, DoneMessage, 0, 0);
                    return 0;

                case WM_CHAR when wParam == VK_RETURN || wParam == VK_ESCAPE:
                    // 既定の処理は警告音を鳴らすので、ここで止める。
                    return 0;

                case WM_KILLFOCUS:
                    PostMessageW(_parent, DoneMessage, 1, 0);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"グループ名の欄でエラー: {ex}");
        }

        return CallWindowProcW(_previousProc, hwnd, message, wParam, lParam);
    }

    public void Dispose() => Cancel();
}
