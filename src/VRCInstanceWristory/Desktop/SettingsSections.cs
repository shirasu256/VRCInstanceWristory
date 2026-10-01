namespace VRCInstanceWristory.Desktop;

/// <summary>設定のまとまり。どれを並べるかは置く側が決める（→<see cref="SettingsView.Layout"/>）。</summary>
[Flags]
public enum SettingsSections
{
    None = 0,

    /// <summary>履歴（履歴の自動リセット・リセットまでの時間・直前のリセットを戻す）。</summary>
    History = 1,

    /// <summary>訪問履歴に含めるインスタンスタイプ（記録する種類）。</summary>
    TargetTypes = 2,

    /// <summary>起動（SteamVRと一緒に・Windowsのログオン時に→実装メモ5.50・5.51）。</summary>
    Startup = 4,

    /// <summary>手首のパネル（VR）。</summary>
    WristPanel = 8,

    /// <summary>インスタンス操作とグループ名（インスタンス操作の挙動→実装メモ5.53、グループ名の出し方→5.48・5.65）。</summary>
    Rows = 16,

    /// <summary>このウィンドウ（常に手前）。デスクトップのウィンドウだけに出す。</summary>
    Window = 32,

    /// <summary>
    /// 写真（写真を開くアプリ→実装メモ5.55）。写真はウィンドウの「選んだ行」から開くので、デスクトップのウィンドウだけに出す。
    /// 「このウィンドウ」とは 2026-09-27 のユーザー指定で分けた（→実装メモ5.58）。
    /// </summary>
    Photos = 64,

    /// <summary>VRオーバーレイ機能を使うか（→実装メモ5.71）。「VRオーバーレイ設定」タブの最上部。</summary>
    Overlay = 128,

    /// <summary>外部連携（外部からの履歴リセットのコマンド→実装メモ5.83）。「一般設定」タブの一番下。</summary>
    External = 256,

    /// <summary>履歴自動リセットの予告通知（予告のアイコン→実装メモ5.87・5.89）。「VRオーバーレイ設定」タブの手首パネルの下。</summary>
    ResetWarning = 512,

    /// <summary>
    /// ダッシュボードに出すもの（「写真」「このウィンドウ」「起動」「外部連携」を除くすべて）。
    /// 「起動」「外部連携」は 2026-09-29 のユーザー指定でダッシュボードから外した（ウィンドウには残す→実装メモ5.90）。
    /// </summary>
    Dashboard = History | TargetTypes | Overlay | WristPanel | ResetWarning | Rows,

    /// <summary>
    /// VRChat の AFK を検知するか（→実装メモ5.98）。初期設定の画面だけに出す（「一般設定」には出さない→5.99）。
    /// オンにするとその場で OSC の受け口を開き、Windows のファイアウォールの許可を求められることがある。
    /// 「一般設定」などでは、AFK を使う2つの設定をオンにしたときに同じように開く。
    /// </summary>
    Afk = 1024,

    /// <summary>初期設定の画面の「リセットまでの時間」だけの「履歴」（→実装メモ5.98）。</summary>
    SetupRetention = 2048,

    /// <summary>初期設定の画面の「パネル位置」だけの「手首パネル」（→実装メモ5.98）。</summary>
    SetupWrist = 4096,

    /// <summary>
    /// 初期設定の画面（初回起動の案内の「わかった」のあと→実装メモ5.98）。リセットまでの時間・訪問履歴に含めるインスタンスタイプ・起動・
    /// パネル位置・VRChatのAFKを検知する。2列で、左に「履歴」「訪問履歴に含めるインスタンスタイプ」、右に「手首パネル」「AFK 検知」「起動」を並べる。
    /// </summary>
    Setup = SetupRetention | TargetTypes | Startup | SetupWrist | Afk,

    All = Dashboard | Photos | Window | Startup | External,
}
