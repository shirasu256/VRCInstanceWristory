namespace VRCInstanceWristory.Core.Locations;

/// <summary>
/// 行のボタン（「ブラウザで開く」／「ここへ戻る」）で何を開くか（2026-09-27のユーザー指定→実装メモ5.53）。
/// 設定 <c>returnAction</c>。
/// </summary>
public enum ReturnAction
{
    /// <summary>
    /// 既定。vrchat.com のそのインスタンスのページを既定のブラウザで開く。ボタンは「ブラウザで開く」。
    /// 動いている VRChat には何も起きない（そのページから自分へ招待を送れば、起動し直さずに移れる）。
    /// </summary>
    Browser,

    /// <summary>
    /// <c>vrchat://launch</c> で VRChat に開かせる。ボタンは「ここへ戻る」。
    /// VRChat はクライアントを起動し直してからそのインスタンスへ入るので、いまのクライアントは閉じ、
    /// FBT のキャリブレーションなど起動中の状態はやり直しになる（2026-09-27の実機確認）。
    /// </summary>
    VrChat,
}

public static class ReturnActions
{
    public static string SettingName(ReturnAction action) => action == ReturnAction.VrChat ? "vrchat" : "browser";

    /// <summary>設定の名前を読む（大文字小文字は問わない）。知らない名前なら null。</summary>
    public static ReturnAction? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "browser" => ReturnAction.Browser,
        "vrchat" => ReturnAction.VrChat,
        _ => null,
    };

    /// <summary>行のボタンの文字。手首のパネルのポップアップとウィンドウの「選んだ行」で同じものを使う。</summary>
    public static string ButtonLabel(ReturnAction action) => action == ReturnAction.VrChat ? "ここへ戻る" : "ブラウザで開く";
}
