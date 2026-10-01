using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// オン・オフを切り替える部品1つぶんの決まり（<see cref="SettingsView"/>）。見せる値（<see cref="Get"/>）を反転して
/// <see cref="With"/> で写し、<see cref="Field"/> として送る。
/// </summary>
/// <param name="GreysOut">押せない間は枠と字を薄い色で描く。持たない部品は、まとまりごと上に薄い覆いを掛けて見せる（→実装メモ5.71）。</param>
internal sealed record CheckSpec(
    SettingsView.HitKind Kind,
    string Label,
    SettingsField Field,
    Func<DesktopSettings, bool> Get,
    Func<DesktopSettings, bool, DesktopSettings> With,
    bool GreysOut = false);

/// <summary>オン・オフの部品の一覧（記録する種類のチェックは種類ごとなので別→<see cref="SettingsView"/>）。</summary>
internal static class SettingsChecks
{
    private static readonly CheckSpec[] All =
    [
        new(SettingsView.HitKind.TopMost, "常に手前に表示する", SettingsField.DesktopWindow,
            s => s.TopMost, (s, v) => s with { TopMost = v }),
        new(SettingsView.HitKind.GroupIdWithName, "グループ名設定済みのグループも Group ID を表示する", SettingsField.GroupDisplay,
            s => s.ShowGroupIdWithName, (s, v) => s with { ShowGroupIdWithName = v }),

        // 結果（SteamVRの登録）は主ループが確かめてから返してくる（→SetStartupState）。SteamVR につながっていない間（null）は押せない。
        new(SettingsView.HitKind.LaunchWithSteamVr, "SteamVR の開始時に自動起動する", SettingsField.LaunchWithSteamVr,
            s => s.LaunchWithSteamVr == true, (s, v) => s with { LaunchWithSteamVr = v }, GreysOut: true),
        new(SettingsView.HitKind.LaunchAtLogon, "Windows のログオン時に起動する", SettingsField.LaunchAtLogon,
            s => s.LaunchAtLogon, (s, v) => s with { LaunchAtLogon = v }),
        new(SettingsView.HitKind.ShowDuringLoading, "ロード画面中もパネルを表示する", SettingsField.LoadingScreen,
            s => s.ShowDuringLoading, (s, v) => s with { ShowDuringLoading = v }),
        new(SettingsView.HitKind.VrOverlay, "VR オーバーレイ機能を有効にする", SettingsField.VrOverlay,
            s => s.VrOverlayEnabled, (s, v) => s with { VrOverlayEnabled = v }),
        new(SettingsView.HitKind.Vibration, "コントローラーの振動を有効にする", SettingsField.Vibration,
            s => s.VibrationEnabled, (s, v) => s with { VibrationEnabled = v }, GreysOut: true),
        new(SettingsView.HitKind.PanelGrab, "手首パネルの移動を有効にする", SettingsField.PanelGrab,
            s => s.PanelGrabEnabled, (s, v) => s with { PanelGrabEnabled = v }),
        new(SettingsView.HitKind.AutoReset, "履歴の自動リセットを有効にする", SettingsField.AutoReset,
            s => s.AutoResetEnabled, (s, v) => s with { AutoResetEnabled = v }),
        new(SettingsView.HitKind.ExternalReset, "外部からの履歴リセットコマンドを受け付ける", SettingsField.ExternalReset,
            s => s.ExternalResetEnabled, (s, v) => s with { ExternalResetEnabled = v }),
        new(SettingsView.HitKind.TriggerMenu, "トリガーで操作メニューを表示", SettingsField.TriggerMenu,
            s => s.TriggerMenuEnabled, (s, v) => s with { TriggerMenuEnabled = v }),
        new(SettingsView.HitKind.ResetWarning, "リセット予告アイコンを表示する", SettingsField.ResetWarning,
            s => s.ResetWarningEnabled, (s, v) => s with { ResetWarningEnabled = v }),

        // AFK を使う2つは、検知していなければオフに見せる（→実装メモ5.99）。押したときの検知の切り替えは SettingsView が足す。
        new(SettingsView.HitKind.WarningReshow, "AFKから復帰時に再表示する", SettingsField.ResetWarning,
            SettingsView.ReshowActive, (s, v) => s with { WarningReshowAfterAfk = v }, GreysOut: true),
        new(SettingsView.HitKind.AfkPause, "AFK中はカウントダウンを停止する", SettingsField.AfkPause,
            SettingsView.AfkPauseActive, (s, v) => s with { PauseCountdownWhileAfk = v }),

        new(SettingsView.HitKind.AfkDetection, "VRChatのAFKを検知する", SettingsField.AfkDetection,
            s => s.AfkDetectionEnabled, (s, v) => s with { AfkDetectionEnabled = v }),
        new(SettingsView.HitKind.TargetPause, "滞在中はカウントダウンを停止する", SettingsField.TargetPause,
            s => s.StopCountdownInTarget, (s, v) => s with { StopCountdownInTarget = v }),

        // インストーラーで入れた版でなければ確かめられないので、グレーアウトする（→実装メモ5.121）。
        new(SettingsView.HitKind.UpdateCheck, "アップデートを自動確認する", SettingsField.UpdateCheck,
            s => s.UpdateCheckEnabled, (s, v) => s with { UpdateCheckEnabled = v }, GreysOut: true),
    ];

    private static readonly Dictionary<SettingsView.HitKind, CheckSpec> ByKind = All.ToDictionary(c => c.Kind);

    /// <summary>その種類の部品がオン・オフの部品なら、その決まり。</summary>
    public static bool TryGet(SettingsView.HitKind kind, out CheckSpec check) => ByKind.TryGetValue(kind, out check!);
}
