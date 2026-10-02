namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 設定ファイルへ書き戻す対象。変えた項目だけを指定して書き戻し、
/// それ以外はファイルの記述をそのまま残す。
/// </summary>
[Flags]
public enum SettingsField
{
    None = 0,

    /// <summary>translationMeters / rotationEulerDegrees / rotationQuaternion。</summary>
    Placement = 1 << 0,

    /// <summary>overlayWidthMeters。</summary>
    OverlayWidth = 1 << 1,

    /// <summary>retentionMinutes（デスクトップのウィンドウで変える→実装メモ5.39）。</summary>
    RetentionMinutes = 1 << 2,

    /// <summary>targetAccessTypes（同上）。</summary>
    TargetAccessTypes = 1 << 3,

    /// <summary>backgroundOpacity（同上）。</summary>
    BackgroundOpacity = 1 << 4,

    /// <summary>viewAngleLimitDegrees / viewAngleFadeSeconds（同上）。</summary>
    ViewAngle = 1 << 5,

    /// <summary>scrollRowsPerSecond（同上）。</summary>
    ScrollSpeed = 1 << 6,

    /// <summary>desktopWindowTopMost（同上）。</summary>
    DesktopWindow = 1 << 7,

    /// <summary>wristSide（パネルを付ける手首→実装メモ5.49）。</summary>
    WristSide = 1 << 8,

    /// <summary>returnAction（行のボタンで何を開くか→実装メモ5.53）。</summary>
    ReturnAction = 1 << 9,

    /// <summary>showGroupIdWithName（グループ名の出し方→実装メモ5.48）。</summary>
    GroupDisplay = 1 << 10,

    /// <summary>groupNames（利用者が付けたグループ名→実装メモ5.48）。</summary>
    GroupNames = 1 << 11,

    /// <summary>rightTranslationMeters / rightRotationEulerDegrees（右手首での配置→実装メモ5.49）。</summary>
    RightPlacement = 1 << 12,

    /// <summary>
    /// SteamVRと一緒に起動する（→実装メモ5.50）。設定ファイルには書かない。正本はSteamVRの登録で、
    /// 反映は主ループが SteamVR へ伝える。
    /// </summary>
    LaunchWithSteamVr = 1 << 13,

    /// <summary>
    /// Windowsのログオン時に起動する（→実装メモ5.51）。設定ファイルには書かない。正本はWindowsのスタートアップの登録。
    /// </summary>
    LaunchAtLogon = 1 << 14,

    /// <summary>photoViewer / photoViewerPath（写真を開くアプリ→実装メモ5.55）。</summary>
    PhotoViewer = 1 << 15,

    /// <summary>showPanelDuringLoading（ロード画面の間もパネルを出すか→実装メモ5.62）。</summary>
    LoadingScreen = 1 << 16,

    /// <summary>vrOverlayEnabled（VRオーバーレイ機能を使うか→実装メモ5.71）。</summary>
    VrOverlay = 1 << 17,

    /// <summary>autoResetEnabled（履歴の自動リセットを使うか→実装メモ5.71）。</summary>
    AutoReset = 1 << 18,

    /// <summary>controllerVibrationEnabled（パネル操作でコントローラーを振動させるか→実装メモ5.78）。</summary>
    Vibration = 1 << 19,

    /// <summary>panelGrabEnabled（手首パネルを掴んで移動できるか→実装メモ5.78）。</summary>
    PanelGrab = 1 << 20,

    /// <summary>externalResetEnabled（外部からの履歴リセットのコマンドを受け付けるか→実装メモ5.83）。</summary>
    ExternalReset = 1 << 21,

    /// <summary>triggerMenuEnabled（行を指してトリガーを引くと操作メニューを出すか→実装メモ5.86）。</summary>
    TriggerMenu = 1 << 22,

    /// <summary>
    /// resetWarning*（履歴自動リセットの予告通知→実装メモ5.87・5.89）。表示する・位置・大きさ・不透明度・点滅回数・表示タイミング・AFKから復帰時に再表示。
    /// </summary>
    ResetWarning = 1 << 23,

    /// <summary>pauseCountdownWhileAfk（AFK中はカウントダウンを停止する→実装メモ5.89）。</summary>
    AfkPause = 1 << 24,

    /// <summary>stopCountdownInTarget（訪問履歴に含めるインスタンスタイプに滞在中はカウントダウンを停止する→実装メモ5.90）。</summary>
    TargetPause = 1 << 25,

    /// <summary>afkDetectionEnabled（VRChat の AFK を検知する→実装メモ5.98）。オンにした時点で OSC の受け口を開く。</summary>
    AfkDetection = 1 << 26,

    /// <summary>welcomeCompleted（初回起動の案内と初期設定を終えた→実装メモ5.97・5.98・5.100）。</summary>
    Welcome = 1 << 27,

    /// <summary>updateCheckEnabled（新しい版を自動で確かめるか→実装メモ5.121）。</summary>
    UpdateCheck = 1 << 28,

    /// <summary>showPanelWhenEmpty（該当する履歴がないときも手首のパネルを出すか→実装メモ5.128）。</summary>
    EmptyHistory = 1 << 29,
}
