namespace VRCInstanceWristory.Core.Presentation;

/// <summary>表示の受け手。SteamVRを使わずに規則を検証できるよう抽象化する。</summary>
public interface IPanelTarget
{
    void SetContent(IReadOnlyList<DisplayRow> rows);

    void ResetScrollToTail();

    /// <summary>
    /// 見出しのカウントダウン（履歴を消すまでの残り時間）を更新する。<paramref name="total"/> は数え始めの長さ（保持時間）で、
    /// 100分以上なら残りが減っても `H:MM:SS` のまま出す（→<see cref="Countdown.Format"/>・実装メモ5.86）。
    /// </summary>
    void SetCountdown(TimeSpan remaining, TimeSpan total);

    /// <summary>カウントダウンが止まっているか（対象インスタンスに滞在中など→実装メモ5.73）。</summary>
    void SetCountdownStopped(bool stopped)
    {
    }

    /// <summary>履歴のリセットまで表示タイミングを切ったことを知らせる（→<see cref="ResetWarningScheduler"/>・実装メモ5.87・5.89）。</summary>
    void ShowResetWarning()
    {
    }

    /// <summary>行ごとの詳しい情報（→実装メモ5.42）を受け取るか。デスクトップのウィンドウだけが受け取る。</summary>
    bool WantsDetails => false;

    /// <summary>行ごとの詳しい情報（一緒にいた人・写真・launch URL など）。</summary>
    void SetDetails(IReadOnlyList<RowDetail> details)
    {
    }
}
