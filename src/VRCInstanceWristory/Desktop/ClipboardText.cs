using System.Runtime.InteropServices;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 文字をクリップボードへ写す（外部連携のコマンドの「コピー」→実装メモ5.83）。WinForms を使わないので Win32 で行う（→実装メモ5.39）。
///
/// <c>OpenClipboard</c> にウィンドウを渡さないと <c>SetClipboardData</c> が失敗するので、その場でメッセージ専用のウィンドウを作って渡す。
/// ダッシュボードから押したとき（デスクトップのウィンドウを出していないこともある）も同じ経路で写せる。
/// 写した文字はクリップボードが持つので、ウィンドウを壊した後も残る。
/// ほかのアプリがクリップボードを開いたままだと空くまで少し待つので、主ループ（VRの1フレームごとの処理）からは
/// <see cref="SetInBackground"/> で別のスレッドに任せる。
/// </summary>
public static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>開けないときに試し直す回数と間（合わせて長くて200ms）。</summary>
    private const int OpenAttempts = 10;

    private const int OpenRetryMs = 20;

    /// <summary>別のスレッドで写すものを1つずつ通す。</summary>
    private static readonly Lock Gate = new();

    /// <summary>最後に頼まれた写しの番号。これより古い頼みは、順番を待つ間に追い越されたので写さない。</summary>
    private static long _latest;

    /// <summary>
    /// 別のスレッドで写し、結果を記録に残す。呼んだ側は待たない（クリップボードが空くのを待つ間、主ループを止めない）。
    /// 写す間だけのスレッドで、ウィンドウ（持ち主）もそのスレッドで作って壊す。
    /// 続けて頼まれたときは1つずつ写し、あとから頼まれたものが必ず最後に残るようにする（古い頼みは写さない）。
    /// </summary>
    public static void SetInBackground(string text, IDiagnostics log)
    {
        var order = Interlocked.Increment(ref _latest);

        var thread = new Thread(() =>
        {
            try
            {
                lock (Gate)
                {
                    if (order != Interlocked.Read(ref _latest))
                        return;

                    if (Set(text, log))
                        log.Info($"クリップボードへ写しました: {text}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"クリップボードへ写す途中でエラー: {ex}");
            }
        })
        {
            Name = "ClipboardText",
            IsBackground = true,
        };

        thread.Start();
    }

    /// <summary>写せたら true。ほかのアプリがクリップボードを開いたままなら、少し待って試し直す（その間は返らない）。</summary>
    public static bool Set(string text, IDiagnostics log)
    {
        var owner = CreateWindowExW(0, "STATIC", string.Empty, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);

        try
        {
            if (!Open(owner))
            {
                log.Warn("クリップボードを開けません（ほかのアプリが使用中の可能性があります）。");
                return false;
            }

            try
            {
                EmptyClipboard();

                var bytes = (text.Length + 1) * sizeof(char);
                var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);

                if (memory == 0)
                    return false;

                var target = GlobalLock(memory);

                if (target == 0)
                {
                    GlobalFree(memory);
                    return false;
                }

                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
                GlobalUnlock(memory);

                // 渡せたら、その後はクリップボードのもの（こちらで解放しない）。
                if (SetClipboardData(CF_UNICODETEXT, memory) == 0)
                {
                    GlobalFree(memory);
                    log.Warn("クリップボードへ写せません。");
                    return false;
                }

                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (owner != 0)
                DestroyWindow(owner);
        }
    }

    private static bool Open(nint owner)
    {
        for (var i = 0; i < OpenAttempts; i++)
        {
            if (OpenClipboard(owner))
                return true;

            Thread.Sleep(OpenRetryMs);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);
}
