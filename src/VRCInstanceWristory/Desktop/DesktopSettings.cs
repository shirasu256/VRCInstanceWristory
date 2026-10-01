using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウで変えられる設定（2026-09-26のユーザー指定→実装メモ5.39）。
///
/// ウィンドウの側はこの写しを持って表示し、変えたら丸ごと主ループへ送る。
/// 実際に <see cref="AppSettings"/> を書き換えるのは主ループだけ（状態の更新を1本のスレッドに集める方針のまま）。
///
/// <see cref="LaunchWithSteamVr"/> と <see cref="LaunchAtLogon"/> は設定ファイルではなく、SteamVR と Windows の登録を
/// 写したもの（→実装メモ5.50・5.51）。SteamVRにつながっていない間は、SteamVR側の登録が分からないので null。
/// <see cref="ExternalResetLastRunUtc"/> と <see cref="UndoResetAvailable"/> も設定ではなく、主ループが画面へ知らせる状態。
///
/// 同じ値なら描き直さないので、比べ方は record の既定（項目ごと）のまま。記録する種類（<see cref="TargetTypes"/>）だけは、
/// 集合を中身で比べるよう <see cref="ValueSet{T}"/> に包んで持つ。
/// </summary>
public sealed record DesktopSettings(
    int RetentionMinutes,
    IReadOnlySet<AccessType> TargetTypes,
    float PanelWidthMeters,
    float BackgroundOpacity,
    float ViewAngleLimitDegrees,
    float ViewAngleFadeSeconds,
    float ScrollRowsPerSecond,
    bool TopMost,
    WristSide Wrist = WristSide.Left,
    ReturnAction ReturnAction = ReturnAction.Browser,
    bool ShowGroupIdWithName = false,
    bool? LaunchWithSteamVr = null,
    bool LaunchAtLogon = false,
    PhotoViewerKind PhotoViewer = PhotoViewerKind.Default,
    string? PhotoViewerPath = null,
    bool ShowDuringLoading = true,
    bool VrOverlayEnabled = true,
    bool AutoResetEnabled = true,
    bool VibrationEnabled = false,
    bool PanelGrabEnabled = true,
    bool ExternalResetEnabled = true,
    DateTime? ExternalResetLastRunUtc = null,
    bool TriggerMenuEnabled = true,
    bool UndoResetAvailable = false,
    bool ResetWarningEnabled = false,
    ResetWarningPosition WarningPosition = ResetWarningPositions.Default,
    float WarningScale = ResetWarningOptions.DefaultScale,
    float WarningOpacity = ResetWarningOptions.DefaultOpacity,
    int WarningBlinkCount = ResetWarningOptions.DefaultBlinkCount,
    int WarningLeadMinutes = ResetWarningOptions.DefaultLeadMinutes,
    bool WarningReshowAfterAfk = true,
    bool PauseCountdownWhileAfk = false,
    bool StopCountdownInTarget = true,
    float WarningMicOffsetXCm = 0f,
    float WarningMicOffsetYCm = 0f,
    float WarningMicOffsetZCm = 0f,
    bool AfkDetectionEnabled = false)
{
    private readonly IReadOnlySet<AccessType> _targetTypes = ValueSet<AccessType>.Of(TargetTypes);

    /// <summary>記録する種類。渡した集合を写して持ち、中身で比べる。</summary>
    public IReadOnlySet<AccessType> TargetTypes
    {
        get => _targetTypes;
        init => _targetTypes = ValueSet<AccessType>.Of(value);
    }

    public static DesktopSettings From(AppSettings settings) => new(
        settings.RetentionMinutes,
        settings.TargetTypes,
        settings.OverlayWidthMeters,
        settings.BackgroundOpacity,
        settings.ViewAngleLimitDegrees,
        settings.ViewAngleFadeSeconds,
        settings.ScrollRowsPerSecond,
        settings.DesktopWindowTopMost,
        settings.Wrist,
        settings.OpenAction,
        settings.ShowGroupIdWithName,
        settings.LaunchWithSteamVr,
        settings.LaunchAtLogon,
        settings.Viewer,
        settings.PhotoViewerPath,
        settings.ShowPanelDuringLoading,
        settings.VrOverlayEnabled,
        settings.AutoResetEnabled,
        settings.ControllerVibrationEnabled,
        settings.PanelGrabEnabled,
        settings.ExternalResetEnabled,
        settings.ExternalResetLastRunUtc,
        settings.TriggerMenuEnabled,
        settings.UndoResetAvailable,
        settings.ResetWarningEnabled,
        settings.WarningPosition,
        settings.ResetWarningScale,
        settings.ResetWarningOpacity,
        settings.ResetWarningBlinkCount,
        settings.ResetWarningLeadMinutes,
        settings.ResetWarningReshowAfterAfk,
        settings.PauseCountdownWhileAfk,
        settings.StopCountdownInTarget,
        settings.ResetWarningMicOffsetXCm,
        settings.ResetWarningMicOffsetYCm,
        settings.ResetWarningMicOffsetZCm,
        settings.AfkDetectionEnabled);

    /// <summary>設定ファイルに書かず、SteamVR・Windows の登録を変えて反映する項目。</summary>
    public const SettingsField StartupFields = SettingsField.LaunchWithSteamVr | SettingsField.LaunchAtLogon;

    /// <summary><paramref name="fields"/> に含まれる項目だけを設定へ写す。</summary>
    public void ApplyTo(AppSettings settings, SettingsField fields)
    {
        if (fields.HasFlag(SettingsField.RetentionMinutes))
            settings.RetentionMinutes = RetentionMinutes;

        if (fields.HasFlag(SettingsField.TargetAccessTypes))
            settings.SetTargetTypes(TargetTypes);

        if (fields.HasFlag(SettingsField.OverlayWidth))
            settings.OverlayWidthMeters = PanelWidthMeters;

        if (fields.HasFlag(SettingsField.BackgroundOpacity))
            settings.BackgroundOpacity = BackgroundOpacity;

        if (fields.HasFlag(SettingsField.ViewAngle))
        {
            settings.ViewAngleLimitDegrees = ViewAngleLimitDegrees;
            settings.ViewAngleFadeSeconds = ViewAngleFadeSeconds;
        }

        if (fields.HasFlag(SettingsField.ScrollSpeed))
            settings.ScrollRowsPerSecond = ScrollRowsPerSecond;

        if (fields.HasFlag(SettingsField.DesktopWindow))
            settings.DesktopWindowTopMost = TopMost;

        if (fields.HasFlag(SettingsField.WristSide))
            settings.Wrist = Wrist;

        if (fields.HasFlag(SettingsField.ReturnAction))
            settings.OpenAction = ReturnAction;

        if (fields.HasFlag(SettingsField.PhotoViewer))
        {
            settings.Viewer = PhotoViewer;
            settings.PhotoViewerPath = PhotoViewerPath;
        }

        if (fields.HasFlag(SettingsField.GroupDisplay))
            settings.ShowGroupIdWithName = ShowGroupIdWithName;

        if (fields.HasFlag(SettingsField.LoadingScreen))
            settings.ShowPanelDuringLoading = ShowDuringLoading;

        if (fields.HasFlag(SettingsField.VrOverlay))
            settings.VrOverlayEnabled = VrOverlayEnabled;

        if (fields.HasFlag(SettingsField.AutoReset))
            settings.AutoResetEnabled = AutoResetEnabled;

        if (fields.HasFlag(SettingsField.Vibration))
            settings.ControllerVibrationEnabled = VibrationEnabled;

        if (fields.HasFlag(SettingsField.PanelGrab))
            settings.PanelGrabEnabled = PanelGrabEnabled;

        if (fields.HasFlag(SettingsField.ExternalReset))
            settings.ExternalResetEnabled = ExternalResetEnabled;

        if (fields.HasFlag(SettingsField.TriggerMenu))
            settings.TriggerMenuEnabled = TriggerMenuEnabled;

        if (fields.HasFlag(SettingsField.ResetWarning))
        {
            settings.ResetWarningEnabled = ResetWarningEnabled;
            settings.WarningPosition = WarningPosition;
            settings.ResetWarningScale = WarningScale;
            settings.ResetWarningOpacity = WarningOpacity;
            settings.ResetWarningBlinkCount = WarningBlinkCount;
            settings.ResetWarningLeadMinutes = WarningLeadMinutes;
            settings.ResetWarningReshowAfterAfk = WarningReshowAfterAfk;
            settings.ResetWarningMicOffsetXCm = WarningMicOffsetXCm;
            settings.ResetWarningMicOffsetYCm = WarningMicOffsetYCm;
            settings.ResetWarningMicOffsetZCm = WarningMicOffsetZCm;
        }

        if (fields.HasFlag(SettingsField.AfkPause))
            settings.PauseCountdownWhileAfk = PauseCountdownWhileAfk;

        if (fields.HasFlag(SettingsField.TargetPause))
            settings.StopCountdownInTarget = StopCountdownInTarget;

        if (fields.HasFlag(SettingsField.AfkDetection))
            settings.AfkDetectionEnabled = AfkDetectionEnabled;

        // UndoResetAvailable は主ループがエンジンから写す（→実装メモ5.86）。

        // ExternalResetLastRunUtc は設定ではなく、主ループが外部からのコマンドでリセットしたときに書く（→実装メモ5.83）。

        // LaunchWithSteamVr / LaunchAtLogon は、主ループが SteamVR と Windows の登録を変えてから写す（StartupFields）。
    }
}
