namespace VRCInstanceWristory.Core;

/// <summary>ログ読み取りと現在地の整合（仕様6.3節の表示条件の一部）。</summary>
public enum LogHealth
{
    /// <summary>起動時の全走査中。処理中は非表示。</summary>
    Initializing,

    Ok,

    /// <summary>実行中プロセスに対応するログセッションを特定できない。</summary>
    NoLogMatch,

    /// <summary>複数のVRChatプロセス・複数セッションへの同時追記。自動選択しない。</summary>
    AmbiguousClient,

    /// <summary>読み取り失敗。1秒ごとに再試行する。</summary>
    ReadError,

    /// <summary>短縮・差し替え・位置不整合からの再構築中。</summary>
    Rebuilding,
}
