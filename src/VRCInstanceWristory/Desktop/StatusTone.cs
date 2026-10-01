namespace VRCInstanceWristory.Desktop;

/// <summary>状態の段の点の色（→実装メモ5.85）。</summary>
public enum StatusTone
{
    /// <summary>アクセント色。正常に動いている。</summary>
    Good,

    /// <summary>
    /// アクセント色を薄くしたもの。正常に動いていて、いまは手首のパネルを出す場面ではないだけ
    /// （手首の角度・メインメニューを閉じている、など）。
    /// </summary>
    Quiet,

    /// <summary>灰色。動いていない・待っている。</summary>
    Idle,

    /// <summary>黄。パネルは出るが一部が働かない・すぐに戻る見込みの問題。</summary>
    Warning,

    /// <summary>赤（クラッシュの行と同じ色）。パネルが出せない・読めない、利用者が手を打つ必要がある。</summary>
    Error,
}
