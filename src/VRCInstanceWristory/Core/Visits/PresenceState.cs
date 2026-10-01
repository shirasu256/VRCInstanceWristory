namespace VRCInstanceWristory.Core.Visits;

/// <summary>現在地の状態（仕様6.2節）。</summary>
public enum PresenceState
{
    /// <summary>開始時・解析不明・離脱・候補失効。</summary>
    Unknown,

    /// <summary>Destination set / 有効なJoiningの受信後。</summary>
    Transitioning,

    /// <summary>対象インスタンスへの入室が確定している。</summary>
    InTarget,

    /// <summary>対象外インスタンスへの入室が確定している。</summary>
    InExcluded,

    /// <summary>クライアント終了。同じセッションの後続ログで解除しない。</summary>
    Ended,
}
