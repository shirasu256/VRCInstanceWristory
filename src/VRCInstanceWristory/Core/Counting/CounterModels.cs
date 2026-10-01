namespace VRCInstanceWristory.Core.Counting;

/// <summary>時刻の出典（仕様3.3節）。精度が異なるため区別して保持する。</summary>
public enum TimeSource
{
    /// <summary>不明。60分境界の判定に使えない。</summary>
    Unknown = 0,

    /// <summary>Windowsのプロセス情報。開始・終了時刻として最も正確。</summary>
    Process,

    /// <summary>ログ由来。ファイル名はセッション開始、HandleApplicationQuitは終了処理開始。</summary>
    Log,

    /// <summary>存否確認・通知の受信時刻。実際の終了時刻とは区別する。</summary>
    Observed,
}

/// <summary>カウント期間の起点。同じ起点なら同じ期間とみなす。</summary>
/// <param name="SourceSessionId">期間の先頭にあたるログセッション。</param>
/// <param name="StartUtc">その起動時刻。</param>
/// <param name="Reason">起点をそう判断した理由（診断用）。</param>
public sealed record EpochAnchor(string SourceSessionId, DateTime StartUtc, string Reason);

/// <summary>カウント期間の写し（→<see cref="VisitCounter.Capture"/>）。</summary>
public sealed record CounterState(string EpochId, bool BaselineKnown, EpochAnchor? Anchor, Dictionary<string, int> Counts);

/// <summary>eventIdごとに保存する、その訪問時点の回数。</summary>
/// <param name="Ordinal">null は「回数不明」。</param>
public sealed record StoredEvent(string EventId, string LocationKey, DateTime VisitedAtUtc, int? Ordinal, string EpochId);

/// <summary>カウント期間の判定結果。</summary>
/// <param name="StartSessionId">期間の先頭セッション。</param>
/// <param name="BaselineKnown">0開始の基準を確定できたか。false なら回数を不明として扱う。</param>
/// <param name="Kind">起点をそう判断した理由。</param>
public sealed record EpochResolution(string StartSessionId, DateTime StartUtc, bool BaselineKnown, EpochReason Kind)
{
    /// <summary>理由の名前（診断の記録と、チェックポイントの起点に書く→<see cref="EpochAnchor.Reason"/>）。</summary>
    public string Reason => Kind.Code();
}
