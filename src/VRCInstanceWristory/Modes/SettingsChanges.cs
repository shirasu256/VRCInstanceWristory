using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Modes;

/// <summary>設定を変えた画面。変えた側へは値を送り返さない（→実装メモ5.40）。</summary>
public enum SettingsEditor
{
    Desktop,
    Dashboard,
}

/// <summary>
/// デスクトップのウィンドウ・SteamVRのダッシュボードで変えた設定の反映と保存（→実装メモ5.39・5.40）。
///
/// 見た目の値（幅・不透明度・角度・スクロールの速さ）は押したその場でVR内へ反映して、
/// 見比べながら合わせられるようにする。保持時間と記録する種類は、値が落ち着いてから（最後の変更から
/// <see cref="SettleDelay"/> 後に）エンジンへ渡す。−を押し続けて途中の短い値を通り過ぎただけで
/// 履歴が消えたり、種類を続けて切り替えるたびにログを読み直したりしないため。
/// 設定ファイルへの保存も同じく落ち着いてから、変えた項目だけを書き戻す。
/// </summary>
public sealed class SettingsChanges(AppSettings settings, string settingsPath, IDiagnostics log, IClock? clock = null)
{
    /// <summary>最後の変更からエンジンへ渡して保存するまでの時間。</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(0.8);

    /// <summary>その場でVR内へ反映する項目。</summary>
    private const SettingsField VrFields = SettingsField.OverlayWidth | SettingsField.BackgroundOpacity | SettingsField.ViewAngle | SettingsField.WristSide | SettingsField.ReturnAction;

    private readonly IClock _clock = clock ?? SystemClock.Instance;

    private SettingsField _pending;
    private DateTime _settleAtUtc;

    /// <summary>まだエンジンへ渡していない・保存していない項目。</summary>
    public SettingsField Pending => _pending;

    private bool? _lastSaveOk;

    /// <summary>
    /// 前に受け取ってからの保存の結果を1回だけ受け取る（状態の段の「履歴保存不能」→実装メモ5.85）。
    /// 保存していなければ null。
    /// </summary>
    public bool? TakeSaveResult()
    {
        var result = _lastSaveOk;
        _lastSaveOk = null;
        return result;
    }

    public void Apply(DesktopSettings values, SettingsField fields, OverlayRuntime? runtime)
    {
        values.ApplyTo(settings, fields);

        if ((fields & VrFields) != 0)
            runtime?.ApplySettings();

        _pending |= fields;
        _settleAtUtc = _clock.UtcNow + SettleDelay;
    }

    /// <summary>落ち着いた変更をエンジンへ渡し、設定ファイルへ保存する。<paramref name="force"/> なら待たない。</summary>
    public void Flush(HistoryEngine engine, bool force)
    {
        if (_pending == SettingsField.None || (!force && _clock.UtcNow < _settleAtUtc))
            return;

        var fields = _pending;
        _pending = SettingsField.None;

        if (fields.HasFlag(SettingsField.RetentionMinutes))
            engine.SetRetention(settings.Retention);

        if (fields.HasFlag(SettingsField.TargetAccessTypes))
            engine.SetTargetTypes(settings.TargetTypes);

        if (fields.HasFlag(SettingsField.LoadingScreen))
            engine.SetShowDuringLoadingScreen(settings.ShowPanelDuringLoading);

        if (fields.HasFlag(SettingsField.AutoReset))
            engine.SetAutoReset(settings.AutoResetEnabled);

        if (fields.HasFlag(SettingsField.TargetPause))
            engine.SetStopCountdownInTarget(settings.StopCountdownInTarget);

        var saved = settings.TrySaveFields(settingsPath, fields, log, "設定を保存できません", out _);
        _lastSaveOk = saved;

        if (saved)
            log.Notice($"設定を保存しました: {Describe(fields)}");
    }

    private string Describe(SettingsField fields)
    {
        var parts = new List<string>();

        if (fields.HasFlag(SettingsField.RetentionMinutes))
            parts.Add($"履歴リセットまで {settings.RetentionMinutes}分");

        if (fields.HasFlag(SettingsField.TargetAccessTypes))
            parts.Add($"記録する種類 {string.Join(" / ", settings.TargetAccessTypes)}");

        if (fields.HasFlag(SettingsField.OverlayWidth))
            parts.Add($"幅 {settings.OverlayWidthMeters * 100f:0.0}cm");

        if (fields.HasFlag(SettingsField.BackgroundOpacity))
            parts.Add($"背景の不透明度 {settings.BackgroundOpacity * 100f:0}%");

        if (fields.HasFlag(SettingsField.ViewAngle))
            parts.Add($"見えなくなる角度 {settings.ViewAngleLimitDegrees:0}° / {settings.ViewAngleFadeSeconds:0.00}秒");

        if (fields.HasFlag(SettingsField.ScrollSpeed))
            parts.Add($"スクロール {settings.ScrollRowsPerSecond:0.0}行/秒");

        if (fields.HasFlag(SettingsField.DesktopWindow))
            parts.Add($"常に手前 {(settings.DesktopWindowTopMost ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.WristSide))
            parts.Add($"パネルを付ける手首 {WristSides.DisplayName(settings.Wrist)}");

        if (fields.HasFlag(SettingsField.ReturnAction))
            parts.Add($"行のボタン {ReturnActions.ButtonLabel(settings.OpenAction)}");

        if (fields.HasFlag(SettingsField.PhotoViewer))
            parts.Add($"写真を開くアプリ {PhotoViewers.DisplayName(settings.Viewer)}{(settings.PhotoViewerPath is { } path && settings.Viewer == PhotoViewerKind.Custom ? $"（{path}）" : string.Empty)}");

        if (fields.HasFlag(SettingsField.GroupDisplay))
            parts.Add($"グループ名の後ろにIDも出す {(settings.ShowGroupIdWithName ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.LoadingScreen))
            parts.Add($"ロード画面中のパネル表示 {(settings.ShowPanelDuringLoading ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.VrOverlay))
            parts.Add($"VRオーバーレイ機能 {(settings.VrOverlayEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.AutoReset))
            parts.Add($"履歴の自動リセット {(settings.AutoResetEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.Vibration))
            parts.Add($"コントローラーの振動 {(settings.ControllerVibrationEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.PanelGrab))
            parts.Add($"手首パネルの移動 {(settings.PanelGrabEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.ExternalReset))
            parts.Add($"外部からの履歴リセット {(settings.ExternalResetEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.UpdateCheck))
            parts.Add($"新しい版の自動確認 {(settings.UpdateCheckEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.TriggerMenu))
            parts.Add($"トリガーで操作メニュー {(settings.TriggerMenuEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.ResetWarning))
            parts.Add($"リセット予告 {(settings.ResetWarningEnabled ? "オン" : "オフ")}（{ResetWarningPositions.DisplayName(settings.WarningPosition)}・{settings.ResetWarningScale * 100f:0}%・不透明度 {settings.ResetWarningOpacity * 100f:0}%・{settings.ResetWarningBlinkCount}回・{settings.ResetWarningLeadMinutes}分前・AFKから復帰時に再表示 {(settings.ResetWarningReshowAfterAfk ? "オン" : "オフ")}・マイクアイコン位置 横 {settings.ResetWarningMicOffsetXCm:+0.0;-0.0;0.0}cm 縦 {settings.ResetWarningMicOffsetYCm:+0.0;-0.0;0.0}cm 奥行き {settings.ResetWarningMicOffsetZCm:+0;-0;0}cm）");

        if (fields.HasFlag(SettingsField.AfkPause))
            parts.Add($"AFK中のカウントダウン停止 {(settings.PauseCountdownWhileAfk ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.AfkDetection))
            parts.Add($"VRChatのAFKの検知 {(settings.AfkDetectionEnabled ? "オン" : "オフ")}");

        if (fields.HasFlag(SettingsField.TargetPause))
            parts.Add($"滞在中のカウントダウン停止 {(settings.StopCountdownInTarget ? "オン" : "オフ")}");

        return string.Join("、", parts);
    }
}
