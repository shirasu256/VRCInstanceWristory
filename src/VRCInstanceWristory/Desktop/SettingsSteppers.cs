using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Infrastructure;
using static System.FormattableString;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 数値を −／＋ で変える部品（<see cref="SettingsView"/>）の名前。値は部品の番号で、
/// <see cref="SettingsView.TargetRect"/> と <see cref="SettingsView.StepperText"/> の <c>index</c> に渡す値と同じ。
/// 番号は足した順（並べる順は <see cref="SettingsSteppers.WarningOrder"/> などで別に決める）。
/// </summary>
public enum SettingsStepper
{
    /// <summary>リセットまでの時間（目立たせる部品→実装メモ5.58）。</summary>
    Retention = 0,

    PanelWidth = 1,
    BackgroundOpacity = 2,
    ViewAngle = 3,
    FadeSeconds = 4,
    ScrollSpeed = 5,

    /// <summary>予告通知の表示位置（‹ › で8方向を巡る→実装メモ5.89）。</summary>
    WarningPosition = 6,

    WarningScale = 7,
    WarningOpacity = 8,
    WarningBlinkCount = 9,
    WarningLead = 10,

    /// <summary>マイクアイコンの位置の合わせ（横・縦→実装メモ5.92、奥行き→5.95）。</summary>
    MicOffsetX = 11,

    MicOffsetY = 12,
    MicOffsetZ = 13,
}

/// <summary>数値の部品がどのまとまりに属するか（押せるかの決まりがまとまりごとに違う→<see cref="SettingsView"/>）。</summary>
internal enum StepperGroup
{
    /// <summary>履歴（自動リセットを無効にしている間は変えられない→実装メモ5.71）。</summary>
    History,

    /// <summary>手首パネル（VRオーバーレイ機能をオフにしている間は変えられない）。</summary>
    WristPanel,

    /// <summary>予告通知（「リセット予告アイコンを表示する」もオンのときだけ変えられる→実装メモ5.89）。</summary>
    ResetWarning,
}

/// <summary>
/// 数値を −／＋ で変える部品1つぶんの決まり。
/// 生の数値を打ち込ませず、人が読める単位で少しずつ動かして、その場でVR内の見え方を確かめられるようにする。
/// </summary>
/// <param name="Next">刻みが一定でない部品の、次の値（なければ <see cref="Step"/> 刻み）。</param>
/// <param name="Cycle">端がなく巡る部品（表示位置）。‹ › をいつでも押せる。</param>
internal sealed record StepperSpec(
    SettingsStepper Id,
    StepperGroup Group,
    string Label,
    float Min,
    float Max,
    float Step,
    SettingsField Field,
    Func<DesktopSettings, float> Get,
    Func<DesktopSettings, float, DesktopSettings> With,
    Func<float, string> Format,
    bool Prominent = false,
    Func<float, int, float>? Next = null,
    bool Cycle = false)
{
    /// <summary>−／＋ を1回押したときの次の値。</summary>
    public float NextValue(float current, int direction)
        => Next is { } next ? next(current, direction) : SettingsSteppers.StepValue(current, Step, Min, Max, direction);
}

/// <summary>
/// 数値の部品の一覧と、値の刻み（→実装メモ5.39・5.71・5.77・5.89）。
/// 数値の書式はどの言語の環境でも同じ（小数点は「.」）にする。
/// </summary>
public static class SettingsSteppers
{
    /// <summary>部品の一覧（<see cref="SettingsStepper"/> の番号の順）。</summary>
    internal static readonly IReadOnlyList<StepperSpec> All = Create();

    /// <summary>番号の部品。</summary>
    internal static StepperSpec Spec(SettingsStepper id) => All[(int)id];

    /// <summary>手首パネルのまとまりに並べる −／＋（上から）。</summary>
    internal static readonly SettingsStepper[] WristOrder =
    [
        SettingsStepper.PanelWidth,
        SettingsStepper.BackgroundOpacity,
        SettingsStepper.ViewAngle,
        SettingsStepper.FadeSeconds,
        SettingsStepper.ScrollSpeed,
    ];

    /// <summary>
    /// 予告通知の −／＋ を並べる順（上から）。マイクアイコンの位置の合わせ（横・縦→実装メモ5.92、奥行き→5.95）は
    /// 番号では最後に足したが、表示位置のすぐ下に置く。
    /// </summary>
    internal static readonly SettingsStepper[] WarningOrder =
    [
        SettingsStepper.WarningPosition,
        SettingsStepper.MicOffsetX,
        SettingsStepper.MicOffsetY,
        SettingsStepper.MicOffsetZ,
        SettingsStepper.WarningScale,
        SettingsStepper.WarningOpacity,
        SettingsStepper.WarningBlinkCount,
        SettingsStepper.WarningLead,
    ];

    private static StepperSpec[] Create()
    {
        StepperSpec[] specs =
        [
            new(SettingsStepper.Retention, StepperGroup.History, "リセットまでの時間", 5f, HistoryStore.MaxRetentionMinutes, 5f, SettingsField.RetentionMinutes,
                s => s.RetentionMinutes, (s, v) => s with { RetentionMinutes = (int)MathF.Round(v) }, v => Invariant($"{v:0} 分"), Prominent: true, Next: NextRetention),
            new(SettingsStepper.PanelWidth, StepperGroup.WristPanel, "パネルの幅", 0.06f, 0.30f, 0.005f, SettingsField.OverlayWidth,
                s => s.PanelWidthMeters, (s, v) => s with { PanelWidthMeters = v }, v => Invariant($"{v * 100f:0.0} cm")),
            new(SettingsStepper.BackgroundOpacity, StepperGroup.WristPanel, "背景の不透明度", 0.5f, 1f, 0.05f, SettingsField.BackgroundOpacity,
                s => s.BackgroundOpacity, (s, v) => s with { BackgroundOpacity = v }, v => Invariant($"{v * 100f:0} %"), Next: NextOpacity),
            // 2刻み（2026-09-29のユーザー指定→実装メモ5.89）。刻みにそろえるので下限は6°。
            new(SettingsStepper.ViewAngle, StepperGroup.WristPanel, "非表示にする角度", 6f, 90f, 2f, SettingsField.ViewAngle,
                s => s.ViewAngleLimitDegrees, (s, v) => s with { ViewAngleLimitDegrees = v }, v => Invariant($"{v:0}°")),
            new(SettingsStepper.FadeSeconds, StepperGroup.WristPanel, "フェードアウト時間", 0f, 2f, 0.05f, SettingsField.ViewAngle,
                s => s.ViewAngleFadeSeconds, (s, v) => s with { ViewAngleFadeSeconds = v }, v => Invariant($"{v:0.00} 秒")),
            new(SettingsStepper.ScrollSpeed, StepperGroup.WristPanel, "スクロールの速さ", 1f, 20f, 0.5f, SettingsField.ScrollSpeed,
                s => s.ScrollRowsPerSecond, (s, v) => s with { ScrollRowsPerSecond = v }, v => Invariant($"{v:0.0} 行/秒")),

            // 予告通知（2026-09-29のユーザー指定→実装メモ5.89）。表示位置は ‹ › で8方向を巡る。
            new(SettingsStepper.WarningPosition, StepperGroup.ResetWarning, "表示位置", 0f, ResetWarningPositions.Order.Length - 1, 1f, SettingsField.ResetWarning,
                s => Array.IndexOf(ResetWarningPositions.Order, s.WarningPosition),
                (s, v) => s with { WarningPosition = PositionAt(v) },
                v => ResetWarningPositions.DisplayName(PositionAt(v)),
                Next: (v, d) => Array.IndexOf(ResetWarningPositions.Order, ResetWarningPositions.Next(PositionAt(v), d)),
                Cycle: true),
            new(SettingsStepper.WarningScale, StepperGroup.ResetWarning, "表示サイズ", ResetWarningOptions.Scales[0], ResetWarningOptions.Scales[^1], 0.2f, SettingsField.ResetWarning,
                s => s.WarningScale, (s, v) => s with { WarningScale = v }, ResetWarningOptions.FormatPercent,
                Next: (v, d) => ResetWarningOptions.Next(ResetWarningOptions.Scales, v, d)),
            new(SettingsStepper.WarningOpacity, StepperGroup.ResetWarning, "不透明度", ResetWarningOptions.Opacities[0], ResetWarningOptions.Opacities[^1], 0.05f, SettingsField.ResetWarning,
                s => s.WarningOpacity, (s, v) => s with { WarningOpacity = v }, ResetWarningOptions.FormatPercent,
                Next: (v, d) => ResetWarningOptions.Next(ResetWarningOptions.Opacities, v, d)),
            new(SettingsStepper.WarningBlinkCount, StepperGroup.ResetWarning, "点滅回数", ResetWarningOptions.BlinkCounts[0], ResetWarningOptions.BlinkCounts[^1], 1f, SettingsField.ResetWarning,
                s => s.WarningBlinkCount, (s, v) => s with { WarningBlinkCount = (int)MathF.Round(v) }, v => Invariant($"{v:0} 回"),
                Next: (v, d) => ResetWarningOptions.Next([.. ResetWarningOptions.BlinkCounts.Select(c => (float)c)], v, d)),
            new(SettingsStepper.WarningLead, StepperGroup.ResetWarning, "表示タイミング", ResetWarningOptions.LeadMinutes[0], ResetWarningOptions.LeadMinutes[^1], 1f, SettingsField.ResetWarning,
                s => s.WarningLeadMinutes, (s, v) => s with { WarningLeadMinutes = (int)MathF.Round(v) }, v => Invariant($"{v:0} 分前"),
                Next: (v, d) => ResetWarningOptions.Next([.. ResetWarningOptions.LeadMinutes.Select(c => (float)c)], v, d)),

            // マイクアイコンの位置の合わせ（2026-09-30→実装メモ5.92）。変えている間は予告のアイコンを出し続けるので、VR の中で実物に合わせられる。
            new(SettingsStepper.MicOffsetX, StepperGroup.ResetWarning, "マイクアイコン位置（横）", -ResetWarningOptions.MicOffsetLimitCm, ResetWarningOptions.MicOffsetLimitCm, ResetWarningOptions.MicOffsetStepCm, SettingsField.ResetWarning,
                s => s.WarningMicOffsetXCm, (s, v) => s with { WarningMicOffsetXCm = v }, v => ResetWarningOptions.FormatMicOffset(v, vertical: false)),
            new(SettingsStepper.MicOffsetY, StepperGroup.ResetWarning, "マイクアイコン位置（縦）", -ResetWarningOptions.MicOffsetLimitCm, ResetWarningOptions.MicOffsetLimitCm, ResetWarningOptions.MicOffsetStepCm, SettingsField.ResetWarning,
                s => s.WarningMicOffsetYCm, (s, v) => s with { WarningMicOffsetYCm = v }, v => ResetWarningOptions.FormatMicOffset(v, vertical: true)),

            // 奥行き（2026-09-30→実装メモ5.95）。見える方向と見かけの大きさは保ったまま、距離だけを変える。
            new(SettingsStepper.MicOffsetZ, StepperGroup.ResetWarning, "マイクアイコン位置（奥行き）", -ResetWarningOptions.MicDepthLimitCm, ResetWarningOptions.MicDepthLimitCm, ResetWarningOptions.MicDepthStepCm, SettingsField.ResetWarning,
                s => s.WarningMicOffsetZCm, (s, v) => s with { WarningMicOffsetZCm = v }, ResetWarningOptions.FormatMicDepth),
        ];

        // 並びと番号がずれていれば、ここで気づく。
        for (var i = 0; i < specs.Length; i++)
        {
            if ((int)specs[i].Id != i)
                throw new InvalidOperationException($"数値の部品の並びが番号とずれています: {specs[i].Id}");
        }

        return specs;
    }

    /// <summary>表示位置の部品の値（並びの番号）から場所へ。</summary>
    private static ResetWarningPosition PositionAt(float index)
        => ResetWarningPositions.Order[Math.Clamp((int)MathF.Round(index), 0, ResetWarningPositions.Order.Length - 1)];

    /// <summary>
    /// −／＋ を1回押したときの次の値。刻みの倍数へそろえてから1つ動かす
    /// （設定ファイルに刻みから外れた値が書いてあっても、押すたびに半端な値のまま動かないように）。
    /// </summary>
    public static float StepValue(float value, float step, float min, float max, int direction)
    {
        var units = value / step;
        const float eps = 0.001f;

        var next = direction > 0
            ? (MathF.Floor(units + eps) + 1f) * step
            : (MathF.Ceiling(units - eps) - 1f) * step;

        // 刻みの誤差（0.005 の倍数など）を丸めて、表示と保存の値を揃える。
        next = (float)Math.Round(next, 4);
        return Math.Clamp(next, min, max);
    }

    /// <summary>
    /// リセットまでの時間の刻み（2026-09-27のユーザー指定→実装メモ5.71）。60分未満は5分、60分から10分、120分から30分、
    /// 300分から60分刻みで、最大900分。刻みの境目にそろった値へ動かす（設定ファイルの半端な値からも次の刻みへ）。
    /// </summary>
    public static float NextRetention(float value, int direction) => NextIn(RetentionSteps, value, direction);

    /// <summary>リセットまでの時間として選べる値（分・小さい順）。</summary>
    private static readonly float[] RetentionSteps =
    [
        .. Enumerable.Range(1, 11).Select(i => i * 5f),          // 5〜55
        .. Enumerable.Range(0, 6).Select(i => 60f + (i * 10f)),  // 60〜110
        .. Enumerable.Range(0, 6).Select(i => 120f + (i * 30f)), // 120〜270
        .. Enumerable.Range(0, 11).Select(i => 300f + (i * 60f)), // 300〜900
    ];

    /// <summary>
    /// 背景の不透明度の刻み（2026-09-27のユーザー指定→実装メモ5.77）。90%未満は5%、90%からは2%刻み。
    /// 濃い側は少しの違いで見え方が変わるので、細かく合わせられるようにした。
    /// </summary>
    public static float NextOpacity(float value, int direction) => NextIn(OpacitySteps, value, direction);

    /// <summary>
    /// 背景の不透明度として選べる値（小さい順）。下限は50%（2026-09-29のユーザー指定→実装メモ5.89。それまでは15%）。
    /// </summary>
    public static readonly IReadOnlyList<float> OpacitySteps =
    [
        .. Enumerable.Range(10, 8).Select(i => (float)Math.Round(i * 0.05, 2)),       // 0.50〜0.85
        .. Enumerable.Range(0, 6).Select(i => (float)Math.Round(0.90 + (i * 0.02), 2)), // 0.90〜1.00
    ];

    /// <summary>選べる値の並び（小さい順）の中で、<paramref name="value"/> から1つ動かした値。端では端の値。</summary>
    private static float NextIn(IReadOnlyList<float> steps, float value, int direction)
    {
        if (direction > 0)
            return steps.FirstOrDefault(v => v > value + 0.001f, steps[^1]);

        return steps.LastOrDefault(v => v < value - 0.001f, steps[0]);
    }
}
