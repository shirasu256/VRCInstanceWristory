using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VRCInstanceWristory.Cli;
using VRCInstanceWristory.Desktop;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// 「アプリを更新」を押したときに確かめる画面の見本（<c>--render-sample --update-confirm</c>→実装メモ5.123）。
///
/// 確かめる画面は Windows が描く小さな画面（<c>MessageBox</c>）なので、自分では描けない。
/// この実行ファイルで本物を出し（ウィンドウのスレッドと同じ DPI の扱い）、出たところを写してから「いいえ」で閉じる。
/// 画面に一瞬出るので、利用者が VR の最中には使わない。
/// </summary>
public static class UpdateConfirmSample
{
    /// <summary>見本に使う新しいバージョン。</summary>
    public const string SampleVersion = "0.2.0";

    public static int Run(string outputPath)
    {
        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var thread = new Thread(() =>
        {
            NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
            UpdateConfirm.Ask(0, SampleVersion);
        })
        {
            IsBackground = true,
            Name = "Update confirm sample",
        };
        thread.Start();

        var dialog = WaitForDialog(TimeSpan.FromSeconds(5));

        if (dialog == 0)
        {
            Console.Error.WriteLine("確かめる画面が出ませんでした。");
            return ExitCode.Failure;
        }

        // 描き終わるのを待ってから写す。
        Thread.Sleep(400);

        try
        {
            GetWindowRect(dialog, out var rect);
            using var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bitmap))
            {
                var hdc = g.GetHdc();

                try
                {
                    PrintWindow(dialog, hdc, PwRenderFullContent);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            bitmap.Save(full, ImageFormat.Png);
        }
        finally
        {
            // 「いいえ」で閉じる（更新はしない）。
            NativeMethods.PostMessageW(dialog, WmCommand, IdNo, 0);
            thread.Join(TimeSpan.FromSeconds(5));
        }

        Console.WriteLine($"保存先       : {full}（確かめる画面・v{SampleVersion}）");
        return ExitCode.Success;
    }

    private static nint WaitForDialog(TimeSpan timeout)
    {
        var limit = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < limit)
        {
            // 小さな画面のウィンドウの種類は "#32770"。
            var found = NativeMethods.FindWindowW("#32770", UpdateConfirm.Caption);

            if (found != 0 && IsWindowVisible(found))
                return found;

            Thread.Sleep(20);
        }

        return 0;
    }

    private const uint PwRenderFullContent = 2;
    private const uint WmCommand = 0x0111;
    private const nint IdNo = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);
}
