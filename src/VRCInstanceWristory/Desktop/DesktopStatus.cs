using VRCInstanceWristory.Core;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// ウィンドウの下に出す動作の状態。主ループが作って送る。
/// 段に出す値・色・詳しい文は <see cref="StatusText"/> がこれだけから決める（→実装メモ5.85）。
/// 時間の長さで出し分けるもの（「1分間だけ出す」など）は、主ループが <see cref="StatusText.Compose"/> で判断を済ませて入れる。
/// 同じ値なら描き直さないので、秒のように細かく変わる値は入れない。
/// 項目はすべて名前で入れる（既定の値は <see cref="Initial"/>＝起動した直後の状態）。
/// </summary>
public sealed record DesktopStatus
{
    /// <summary>起動した直後（VRChat・SteamVR とも未接続、ログは初期化中）。</summary>
    public static readonly DesktopStatus Initial = new();

    // ---- VRChat

    /// <summary>VRChat が動いている。</summary>
    public bool ClientRunning { get; init; }

    /// <summary>動いている VRChat のプロセスの数。</summary>
    public int ClientCount { get; init; }

    public bool ProcessListFailing { get; init; }

    public bool ProcessInfoUnreadable { get; init; }

    /// <summary>VRChat のクラッシュに気づいてから1分以内。</summary>
    public bool RecentCrash { get; init; }

    /// <summary>「ここへ戻る」（<c>vrchat://launch</c>）で VRChat を起動し直している途中。</summary>
    public bool Relaunching { get; init; }

    /// <summary>VR モードの VRChat が SteamVR 以外の経路（OpenXR）で動いている。</summary>
    public bool NonSteamVrRuntime { get; init; }

    // ---- VRChat ログ

    /// <summary>ログの追跡の状態。</summary>
    public LogHealth Health { get; init; } = LogHealth.Initializing;

    /// <summary>メインメニューのページが開いている（手首のパネルを出す場面）。</summary>
    public bool MenuPageOpen { get; init; }

    public LogFolderState LogFolder { get; init; }

    /// <summary>VRChat のログのフォルダー（詳しい文に出す）。</summary>
    public string? LogDirectory { get; init; }

    public LogReadFailure ReadFailure { get; init; }

    /// <summary>ログを読めないことが5分以上続いている。</summary>
    public bool ReadFailingLong { get; init; }

    /// <summary>対応するログを特定できないことが、ログのファイルが現れるのを待つ時間より長く続いている。</summary>
    public bool NoLogMatchLong { get; init; }

    /// <summary>ログが増えていない分数（10分未満なら0）。</summary>
    public int StalledMinutes { get; init; }

    public bool FormatSuspect { get; init; }

    public bool HistoryTruncated { get; init; }

    /// <summary>内部履歴を読めずにログから組み立て直してから1分以内。</summary>
    public bool CheckpointRebuilt { get; init; }

    public bool SaveFailing { get; init; }

    /// <summary>ログの時刻と PC の時計のずれ（分・5分未満なら0）。</summary>
    public int ClockSkewMinutes { get; init; }

    // ---- SteamVR

    /// <summary>SteamVR につながっている。</summary>
    public bool VrConnected { get; init; }

    /// <summary>SteamVR は動いているのにつなげなかった理由（→実装メモ5.82）。</summary>
    public VrConnectError VrError { get; init; } = VrConnectError.None;

    /// <summary><see cref="VrError"/> が <see cref="VrConnectError.InitOther"/> のときの名前（<c>EVRInitError</c>）。</summary>
    public string? VrErrorName { get; init; }

    /// <summary>SteamVR のサーバーが動いている（つながっていなくても）。</summary>
    public bool VrServerSeen { get; init; }

    /// <summary>SteamVR が終了を知らせてきて、サーバーがまだ残っている。</summary>
    public bool VrQuitting { get; init; }

    public VrRuntimeIssue VrIssues { get; init; }

    // ---- 手首パネル

    /// <summary>手首のパネルを出していない理由（出していれば <see cref="PanelHideReason.None"/>）。</summary>
    public PanelHideReason VrHideReason { get; init; } = PanelHideReason.ContentNotReady;

    /// <summary>設定の「VR オーバーレイ機能を有効にする」。</summary>
    public bool VrOverlayEnabled { get; init; } = true;

    public bool HeadsetStandby { get; init; }

    public bool OperatingHandMissing { get; init; }

    // ---- アップデート（状態の段の右端のリンク→実装メモ5.121）

    /// <summary>知らせる新しい版（"0.2.0" の形）。なければ null。</summary>
    public string? UpdateVersion { get; init; }

    /// <summary>落としている途中なら、その進み具合（0〜100）。落としていなければ null。</summary>
    public int? UpdateProgress { get; init; }
}
