namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 「あと n 分で履歴がリセットされる」ことを知らせる合図（2026-09-28のユーザー指定→実装メモ5.87）。
///
/// 残り時間が表示タイミング（<c>Observe</c> に渡す <c>lead</c>・既定3分→実装メモ5.89）を上から下へまたいだ瞬間に1回だけ知らせる。
///
/// | 場面 | 知らせるか |
/// | --- | --- |
/// | 数えている間に残りが表示タイミングを切った | 知らせる（1回だけ） |
/// | 「延長」などで残りが表示タイミングより上へ戻り、また切った | もう一度知らせる |
/// | 数え始めた時点で表示タイミング以下（保持時間が短い・起動した時点で残りが少ない） | 知らせない（またいでいない） |
/// | カウントダウン停止中・自動リセットを止めている・行がない・VRChatが動いていない | 知らせない |
/// | スリープ明けなどで一気に0になった | 知らせない（もう消えている） |
/// </summary>
public sealed class ResetWarningTrigger
{
    /// <summary>既定の表示タイミング。</summary>
    public static readonly TimeSpan DefaultLead = TimeSpan.FromMinutes(ResetWarningOptions.DefaultLeadMinutes);

    // 直前に見たとき、数えていて残りが表示タイミングより多かったか。ここから下りたときだけ知らせる。
    private bool _armed;

    /// <summary>いまの状態を見て、知らせる瞬間なら true を返す。</summary>
    /// <param name="countingDown">自動リセットを使っていて、期限を数えているか（停止中でない）。</param>
    /// <param name="remaining">リセットまでの残り時間。</param>
    /// <param name="relevant">知らせる意味があるか（消える行があり、VRChatが動いている）。</param>
    /// <param name="lead">表示タイミング（リセットの何分前か）。</param>
    public bool Observe(bool countingDown, TimeSpan remaining, bool relevant, TimeSpan lead)
    {
        if (!countingDown)
        {
            _armed = false;
            return false;
        }

        if (remaining > lead)
        {
            _armed = true;
            return false;
        }

        var fire = _armed && relevant && remaining > TimeSpan.Zero;
        _armed = false;
        return fire;
    }
}
