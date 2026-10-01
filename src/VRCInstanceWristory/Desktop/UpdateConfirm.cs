using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 「アプリを更新」「更新して再起動」を押したときに確かめる画面（→実装メモ5.121・5.123）。
/// ウィンドウ（<see cref="DesktopWindow"/>）と見本の画像（<c>--render-sample --update-confirm</c>）で同じものを出す。
/// </summary>
public static class UpdateConfirm
{
    public const string Caption = "アップデート";

    /// <summary>情報のアイコン・「はい」「いいえ」。既定のボタンは「いいえ」（Enter の押し間違いで更新しない）。</summary>
    public const uint Flags = MB_YESNO | MB_ICONINFORMATION | MB_DEFBUTTON2;

    /// <summary>本文（2026-10-01のユーザー指定→実装メモ5.123）。<paramref name="version"/> は "0.2.0" の形。</summary>
    public static string Text(string version)
        => $"新バージョン v{version} へ更新します。\n" +
           "更新が完了するとアプリは自動で再起動します。\n" +
           "設定や訪問履歴は引き継がれます。\n" +
           "更新を開始しますか？";

    /// <summary>確かめる画面を出し、「はい」なら true。</summary>
    public static bool Ask(nint owner, string version) => MessageBoxW(owner, Text(version), Caption, Flags) == IDYES;
}
