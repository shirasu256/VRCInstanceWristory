using VRCInstanceWristory.Core.Presentation;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 手首のパネルの代わり。<see cref="Modes.PanelPresenter"/> から渡されたものを、そのまま書き留める。
/// </summary>
public sealed class RecordingPanelTarget : IPanelTarget
{
    /// <summary>渡された行の並び（渡された順）。</summary>
    public List<IReadOnlyList<DisplayRow>> RowUpdates { get; } = [];

    /// <summary>行を渡された回数（＝パネル側が行を作り直した回数）。</summary>
    public int Updates => RowUpdates.Count;

    /// <summary>最後に渡された行。</summary>
    public IReadOnlyList<DisplayRow> LastRows => RowUpdates.Count > 0 ? RowUpdates[^1] : [];

    /// <summary>末尾へ送り直すよう頼まれた回数。</summary>
    public int TailResets { get; private set; }

    /// <summary>渡された残り時間（渡された順）。</summary>
    public List<TimeSpan> Countdowns { get; } = [];

    /// <summary>最後に渡された残り時間。</summary>
    public TimeSpan Remaining { get; private set; }

    /// <summary>最後に渡された数え始めの長さ（保持時間）。</summary>
    public TimeSpan Total { get; private set; }

    /// <summary>最後に知らされた、カウントダウンが止まっているか。</summary>
    public bool CountdownStopped { get; private set; }

    /// <summary>リセットの予告を出すよう頼まれた回数。</summary>
    public int Warnings { get; private set; }

    public void SetContent(IReadOnlyList<DisplayRow> rows) => RowUpdates.Add(rows);

    public void ResetScrollToTail() => TailResets++;

    public void SetCountdown(TimeSpan remaining, TimeSpan total)
    {
        Countdowns.Add(remaining);
        (Remaining, Total) = (remaining, total);
    }

    public void SetCountdownStopped(bool stopped) => CountdownStopped = stopped;

    public void ShowResetWarning() => Warnings++;
}
