using System.Runtime.InteropServices;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// ファイルを選ぶ画面（写真を開くアプリ→実装メモ5.55、グループ名の読み込み・書き出し→5.65）。
/// WinForms を使わないので、Win32 の <c>GetOpenFileNameW</c> / <c>GetSaveFileNameW</c> を直接呼ぶ（→実装メモ5.39）。
/// ウィンドウのスレッドから呼ぶ（閉じるまで返らない）。
/// </summary>
internal static class FileDialogs
{
    /// <summary>選んだパスを受け取る欄の長さ（文字）。</summary>
    private const int PathCapacity = 1024;

    /// <summary>グループ名の JSON ファイルを選ぶ画面を出す。<paramref name="save"/> なら書き出す先。選ばずに閉じたら null。</summary>
    public static string? ChooseJsonFile(nint owner, bool save)
    {
        var dialog = new OPENFILENAMEW
        {
            hwndOwner = owner,
            lpstrFilter = "JSON (*.json)\0*.json\0すべてのファイル (*.*)\0*.*\0\0",
            // インポートはダウンロードしたファイルを選ぶことが多いので、ダウンロードフォルダーから始める（→実装メモ5.66）。
            lpstrInitialDir = save ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : DownloadsFolder(),
            lpstrTitle = save ? "グループ名のエクスポート" : "グループ名のインポート",
            lpstrDefExt = "json",
            Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
        };

        return Show(dialog, save ? GroupNameFile.DefaultFileName : string.Empty, save);
    }

    /// <summary>写真を開くアプリの実行ファイルを選ぶ画面を出す。選ばずに閉じたら null。</summary>
    public static string? ChooseExecutable(nint owner)
    {
        var dialog = new OPENFILENAMEW
        {
            hwndOwner = owner,
            lpstrFilter = "アプリ (*.exe)\0*.exe\0すべてのファイル (*.*)\0*.*\0\0",
            lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            lpstrTitle = "写真を開くアプリを選ぶ",
            Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY,
        };

        return Show(dialog, string.Empty, save: false);
    }

    /// <summary>
    /// 画面を出して、選んだパスを返す。<paramref name="dialog"/> には欄（パスを受け取る場所）以外の中身を入れて渡す。
    /// <paramref name="initialFile"/> は最初に欄へ入れておくファイル名。
    /// </summary>
    private static string? Show(OPENFILENAMEW dialog, string initialFile, bool save)
    {
        var buffer = Marshal.AllocHGlobal(PathCapacity * sizeof(char));

        try
        {
            var chars = (initialFile + "\0").ToCharArray();
            Marshal.Copy(chars, 0, buffer, chars.Length);

            dialog.lStructSize = Marshal.SizeOf<OPENFILENAMEW>();
            dialog.nFilterIndex = 1;
            dialog.lpstrFile = buffer;
            dialog.nMaxFile = PathCapacity;

            var chosen = save ? GetSaveFileNameW(ref dialog) : GetOpenFileNameW(ref dialog);
            return chosen ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 利用者のダウンロードフォルダー。場所を移していても追えるよう、既知のフォルダー（FOLDERID_Downloads）として引く。
    /// 引けなければ、ユーザーのフォルダーの下の Downloads。
    /// </summary>
    private static string DownloadsFolder()
    {
        if (SHGetKnownFolderPath(FOLDERID_Downloads, 0, 0, out var path) == 0 && path != 0)
        {
            try
            {
                if (Marshal.PtrToStringUni(path) is { Length: > 0 } folder)
                    return folder;
            }
            finally
            {
                Marshal.FreeCoTaskMem(path);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
