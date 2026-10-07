using VRCInstanceWristory.Core.Presentation;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// 表示の受け手を束ねる。VR側（SteamVRにつながっている間だけ）とデスクトップのウィンドウへ同じ内容を渡す（→実装メモ5.39）。
/// </summary>
public sealed class PanelTargets : IPanelTarget
{
    public IPanelTarget? Vr { get; set; }

    public IPanelTarget? Desktop { get; set; }

    public void SetContent(IReadOnlyList<DisplayRow> rows)
    {
        Vr?.SetContent(rows);
        Desktop?.SetContent(rows);
    }

    public void ResetScrollToTail()
    {
        Vr?.ResetScrollToTail();
        Desktop?.ResetScrollToTail();
    }

    public void SetCountdown(TimeSpan remaining, TimeSpan total)
    {
        Vr?.SetCountdown(remaining, total);
        Desktop?.SetCountdown(remaining, total);
    }

    public void SetCountdownStopped(bool stopped)
    {
        Vr?.SetCountdownStopped(stopped);
        Desktop?.SetCountdownStopped(stopped);
    }

    // 「延長」はどちらで押しても、両方の数字を光らせる（→実装メモ5.130）。
    public void FlashCountdown()
    {
        Vr?.FlashCountdown();
        Desktop?.FlashCountdown();
    }

    // 予告のアイコンはVRChatのマイクのアイコンの横に出すもので、VR側だけが受け取る（→実装メモ5.87）。
    public void ShowResetWarning() => Vr?.ShowResetWarning();

    public bool WantsDetails => Desktop?.WantsDetails ?? false;

    public void SetDetails(IReadOnlyList<RowDetail> details) => Desktop?.SetDetails(details);
}
