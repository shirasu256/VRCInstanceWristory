namespace VRCInstanceWristory.Core.Marks;

/// <summary>
/// インスタンスに付ける目印（2026-09-22のユーザー指定→実装メモ5.32）。
///
/// 履歴の行を指してトリガーを引くと出るポップアップで選ぶ。付箋を貼るのと同じ感覚で、
/// 「また来たい」「確認した」「気をつける」といった覚え書きをインスタンスへ残す。
///
/// 絵文字（❤️✅⚠️）で指定を受けたが、実際の描画は色付きの絵文字ではなく、
/// パネルと同じ平面的な図形で描く（→<see cref="Vr.MarkPainter"/>）。
/// </summary>
public enum InstanceMark
{
    /// <summary>目印なし。</summary>
    None = 0,

    /// <summary>ハート。</summary>
    Heart = 1,

    /// <summary>チェック。</summary>
    Check = 2,

    /// <summary>注意。</summary>
    Warning = 3,
}

/// <summary>選べる目印の一覧。ポップアップの並び順もこれに従う。</summary>
public static class InstanceMarks
{
    /// <summary>ポップアップに並べる選択肢（左から順）。</summary>
    public static readonly IReadOnlyList<InstanceMark> Choices =
        [InstanceMark.Heart, InstanceMark.Check, InstanceMark.Warning];

    /// <summary>
    /// 選んだ目印を今の状態へ当てはめた結果。
    /// 既に付いている目印をもう一度選ぶと外れる（2026-09-22のユーザー指定）。
    /// </summary>
    public static InstanceMark Toggle(InstanceMark current, InstanceMark chosen)
        => current == chosen ? InstanceMark.None : chosen;
}
