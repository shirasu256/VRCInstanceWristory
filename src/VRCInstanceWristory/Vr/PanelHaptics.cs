namespace VRCInstanceWristory.Vr;

/// <summary>振動の種類（→実装メモ5.78）。</summary>
public enum HapticPulse
{
    /// <summary>指す部品が変わった（行・見出しのボタン・ポップアップの選択肢・確認の項目）。いちばん弱く短い（→実装メモ5.80）。</summary>
    Point,

    /// <summary>掴んでいたのを離した。</summary>
    Tick,

    /// <summary>トリガーで押して何かが起きた（ポップアップを開く・選ぶ・閉じる、延長、リセットの確認）。</summary>
    Press,

    /// <summary>パネルを掴んだ。</summary>
    Grab,
}

public static class HapticPulses
{
    /// <summary>
    /// 振動の長さ（秒）・周波数（Hz）・強さ（0〜1）。
    /// 操作の手応えとして気づける程度にとどめ、VRChat の操作の邪魔にならないよう短く弱くする。
    ///
    /// 指したとき（<see cref="HapticPulse.Point"/>）は、できるだけ弱く短くする（2026-09-28のユーザー指定→実装メモ5.80・5.81）。
    /// 資料（Driver_API_Documentation の Haptics）で最小とされる長さ 0 秒（パルス1回）・強さ 1% は、Pico 4 では鳴らなかった（5.80）。
    /// そこで長さは周波数の1周期ぶん（資料の参考の実装で「長さ × 周波数」＝パルス1回）、強さは鳴っていた 0.20 の半分にした。
    /// </summary>
    public static (float DurationSeconds, float Frequency, float Amplitude) Shape(HapticPulse pulse) => pulse switch
    {
        HapticPulse.Point => (1f / 160f, 160f, 0.10f),
        HapticPulse.Tick => (0.010f, 160f, 0.20f),
        HapticPulse.Press => (0.025f, 160f, 0.45f),
        HapticPulse.Grab => (0.040f, 120f, 0.55f),
        _ => (0f, 0f, 0f),
    };
}

/// <summary>パネルの中で指せる部品の種類（振動の判定に使う）。</summary>
public enum PanelPart
{
    None,
    Row,
    Extend,
    Clear,
    PopupChoice,
    ConfirmItem,
}

/// <summary>いま指している部品。<see cref="Index"/> は行・選択肢・確認の項目の番号。</summary>
public readonly record struct PanelPointTarget(PanelPart Part, int Index = 0)
{
    public static readonly PanelPointTarget None = new(PanelPart.None);
}

/// <summary>
/// 手首パネルの操作から、いつ振動させるかを決める（2026-09-28のユーザー指定→実装メモ5.78）。
///
/// 1フレームに鳴らすのは1回だけで、掴んだ &gt; 押した &gt; 指す部品が変わった の順に選ぶ。
/// 部品から外れたとき（何も指していない状態へ移ったとき）は鳴らさない。掴んでいる間は指す部品が変わっても鳴らさず、
/// 離したときに1回だけ弱く鳴らす。設定で振動を切っていても状態は追い続け、入れた瞬間にまとめて鳴らさないようにする。
/// </summary>
public sealed class PanelHaptics
{
    private PanelPointTarget _pointed = PanelPointTarget.None;
    private bool _grabbing;

    /// <summary>直前の <see cref="Update"/> で鳴らすと決めた理由（診断の記録用）。鳴らさなかったときは null。</summary>
    public string? LastReason { get; private set; }

    /// <summary>1フレーム分を見て、鳴らす振動を返す（鳴らさなければ null）。</summary>
    /// <param name="pointed">いま指している部品。</param>
    /// <param name="pressed">このフレームのトリガーで何かが起きたか。</param>
    /// <param name="grabbing">パネルを掴んでいるか。</param>
    public HapticPulse? Update(PanelPointTarget pointed, bool pressed, bool grabbing)
    {
        var grabStarted = grabbing && !_grabbing;
        var grabEnded = !grabbing && _grabbing;
        var pointedChanged = pointed != _pointed && pointed.Part != PanelPart.None;

        _pointed = pointed;
        _grabbing = grabbing;

        (HapticPulse? pulse, LastReason) = (grabStarted, pressed, grabEnded, pointedChanged && !grabbing) switch
        {
            (true, _, _, _) => ((HapticPulse?)HapticPulse.Grab, "掴んだ"),
            (_, true, _, _) => (HapticPulse.Press, "押した"),
            (_, _, true, _) => (HapticPulse.Tick, "離した"),
            (_, _, _, true) => (HapticPulse.Point, $"指す部品が変わった（{pointed.Part} {pointed.Index}）"),
            _ => (null, null),
        };

        return pulse;
    }

    /// <summary>パネルが隠れた。次に出たとき、最初に指した部品で鳴らす。</summary>
    public void Reset()
    {
        _pointed = PanelPointTarget.None;
        _grabbing = false;
    }
}
