namespace VRCInstanceWristory.Core.Counting;

/// <summary>
/// カウント期間の起点をそう判断した理由（→<see cref="HistoryEngine"/> の ResolveEpoch・実装メモ5.110）。
/// 前回の VRChat の終わりから60分以内かで引き継ぐ規則（→5.59〜5.61）は、2026-10-01のユーザー指定でやめた。
/// チェックポイントの起点には名前（<see cref="EpochReasons.Code"/>）で書くので、前の版の名前が残っていても読める。
/// </summary>
public enum EpochReason
{
    /// <summary>検証で、対象セッションを明示的に期間の先頭とした（<see cref="EngineOptions.AssumeEpochStart"/>）。</summary>
    ExplicitEpochStart,

    /// <summary>
    /// 履歴の自動リセットが無効なので、VRChat の起動ごとに数え直した（2026-10-01のユーザー指定→実装メモ5.109）。
    /// </summary>
    PerLaunch,

    /// <summary>履歴の自動リセットが入ったので数え直した。次の自動リセットまで、VRChat を起動し直しても引き継ぐ（→実装メモ5.110）。</summary>
    SinceAutoReset,

    /// <summary>自動リセットで始めた期間がまだないので、読んだうちいちばん古い起動から数え始めた（→実装メモ5.110）。</summary>
    FirstLaunch,
}

public static class EpochReasons
{
    /// <summary>診断の記録とチェックポイントに書く名前。</summary>
    public static string Code(this EpochReason reason) => reason switch
    {
        EpochReason.ExplicitEpochStart => "explicit-epoch-start",
        EpochReason.PerLaunch => "per-launch",
        EpochReason.SinceAutoReset => "since-auto-reset",
        EpochReason.FirstLaunch => "first-launch",
        _ => reason.ToString(),
    };
}
