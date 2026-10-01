using System.Runtime.InteropServices;
using System.Text;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// コンソールの扱い（2026-09-27のユーザー指定→実装メモ5.63）。
///
/// 実行ファイルは Windows のアプリ（<c>OutputType=WinExe</c>）なので、どこから起動してもコンソールのウィンドウは開かない。
/// 出力は起動の仕方で向け先を決める。
///
/// | 起動の仕方 | 出力の向け先 |
/// | --- | --- |
/// | ターミナルから | 起動したターミナル（<c>AttachConsole</c> で親のコンソールへつなぐ） |
/// | パイプ・ファイルへリダイレクト | そのリダイレクト先（受け継いだ標準ハンドル） |
/// | エクスプローラー・スタートアップ・SteamVR（通常動作） | ログのファイル（<see cref="LogFilePath"/>） |
///
/// 2026-09-26までは実行ファイルがコンソールのアプリで、<c>--minimized</c> のときだけ
/// ウィンドウのないコンソールで自分を起動し直していた（5.51）。その迂回はここで畳んだ。
/// </summary>
public static class ConsoleHost
{
    private const uint AttachParentProcess = unchecked((uint)-1);

    /// <summary>画面に出ないときの出力先。</summary>
    public static string LogFilePath => AppPaths.AppLog;

    /// <summary>出力がターミナルかリダイレクト先に届くか（<see cref="Attach"/> の結果）。</summary>
    public static bool HasOutput { get; private set; }

    /// <summary>
    /// 起動の最初に呼ぶ（<see cref="Console"/> へ触る前）。出力がリダイレクトされていればそのまま、
    /// ターミナルから起動していればそのコンソールへつなぐ。
    /// </summary>
    public static void Attach()
        => HasOutput = IsRedirected(GetStdHandle(StdOutputHandle)) || AttachConsole(AttachParentProcess);

    /// <summary>
    /// 出力をログのファイルへ向ける。起動のたびに作り直すので、ファイルが伸び続けることはない。書けなければ何もしない。
    /// </summary>
    public static bool RedirectToLogFile()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);

            var stream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)) { AutoFlush = true };

            Console.SetOut(writer);
            Console.SetError(writer);
            return true;
        }
        catch (Exception)
        {
            // 書けなければ出力は捨てる（もともと画面に出ない起動なので、知らせる先がない）。動作は続ける。
            return false;
        }
    }

    private const int StdOutputHandle = -11;
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;

    private static bool IsRedirected(nint handle)
    {
        if (handle is 0 or -1)
            return false;

        var type = GetFileType(handle);
        return type is FileTypeDisk or FileTypePipe;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(nint handle);
}
