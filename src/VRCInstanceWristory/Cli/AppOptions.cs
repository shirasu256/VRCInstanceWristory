using VRCInstanceWristory.Desktop;

namespace VRCInstanceWristory.Cli;

/// <summary>起動のしかた。</summary>
public enum AppMode
{
    /// <summary>通常動作（ログ監視 + SteamVR表示 + デスクトップのウィンドウ）。</summary>
    Live,

    /// <summary>見た目を SteamVR なしで画像にする（<c>--render-sample</c>）。</summary>
    RenderSample,

    /// <summary>実行ファイルのアイコン（app.ico）を作る（<c>--export-icon</c>→実装メモ5.41）。</summary>
    ExportIcon,
}

/// <summary><c>--render-sample</c> で描くもの。どれか1つ。</summary>
public enum RenderSampleTarget
{
    /// <summary>手首のパネル（既定）。</summary>
    Panel,

    /// <summary>デスクトップのウィンドウ全体（<c>--window</c>→実装メモ5.39）。</summary>
    Window,

    /// <summary>SteamVRのダッシュボードの設定の画面（<c>--dashboard</c>→実装メモ5.40）。</summary>
    Dashboard,

    /// <summary>履歴リセットの予告のアイコン（<c>--reset-warning</c>→実装メモ5.87）。</summary>
    ResetWarning,

    /// <summary>初回起動の案内の画面（<c>--welcome</c>→実装メモ5.97）。</summary>
    Welcome,

    /// <summary>「アプリを更新」を押したときに確かめる画面（<c>--update-confirm</c>→実装メモ5.123）。</summary>
    UpdateConfirm,
}

/// <summary><c>--render-sample</c> の見本の状態。</summary>
public sealed class RenderSampleOptions
{
    public RenderSampleTarget Target { get; set; } = RenderSampleTarget.Panel;

    /// <summary>パネルのスクロール量（px）。負なら末尾。</summary>
    public float ScrollOffset { get; set; } = -1f;

    /// <summary>行を指している状態と目印のポップアップを描く（<c>--mark-popup</c>→実装メモ5.32）。</summary>
    public bool MarkPopup { get; set; }

    /// <summary>見出しの「リセット」を押したあとの確認を描く（<c>--reset-confirm</c>→実装メモ5.65）。</summary>
    public bool ResetConfirm { get; set; }

    /// <summary>
    /// 残り時間の数字の強調を描く（<c>--countdown-glow</c>・<c>--countdown-warning</c>→実装メモ5.130）。
    /// 発光は「延長」を押した瞬間、警告は残り3分以下でいちばん赤い瞬間の絵にする。
    /// </summary>
    public bool CountdownGlow { get; set; }

    /// <inheritdoc cref="CountdownGlow"/>
    public bool CountdownWarning { get; set; }

    /// <summary>ウィンドウで、SteamVR へつなげない状態の段を描く（<c>--vr-error</c>→実装メモ5.82）。</summary>
    public bool VrError { get; set; }

    /// <summary>ウィンドウの右側に出すタブ（<c>--tab</c>→実装メモ5.42）。null なら「選んだ行」。</summary>
    public DesktopTab? Tab { get; set; }

    /// <summary>初回起動の案内で、「わかった」のあとの初期設定の画面を描く（<c>--setup</c>→実装メモ5.98）。</summary>
    public bool Setup { get; set; }
}

/// <summary>コマンドラインの引数を読んだ結果。</summary>
public sealed class AppOptions
{
    public AppMode Mode { get; set; } = AppMode.Live;

    public bool ShowHelp { get; set; }

    public bool Verbose { get; set; }

    /// <summary>設定ファイルの場所を上書きする。</summary>
    public string? SettingsPath { get; set; }

    /// <summary>ログフォルダーの上書き（設定ファイルより優先）。</summary>
    public string? LogDirectory { get; set; }

    /// <summary><c>--render-sample</c> の画像・<c>--export-icon</c> のアイコンの保存先。</summary>
    public string? OutputFile { get; set; }

    public RenderSampleOptions Sample { get; } = new();

    /// <summary>通常動作でデスクトップのウィンドウを出さない（→実装メモ5.39）。</summary>
    public bool NoWindow { get; set; }

    /// <summary>
    /// ウィンドウをタスクトレイに入れた状態で始める（→実装メモ5.51）。Windowsのログオン時の起動と、
    /// SteamVRと一緒の起動に付ける。SteamVRがまだ動いていなければ、SteamVRを起こさずに起動を待つ。
    /// </summary>
    public bool Minimized { get; set; }

    /// <summary>SteamVRが起動のついでに開いた（→実装メモ5.50）。SteamVRが終了したら一緒に終わる。</summary>
    public bool FromSteamVr { get; set; }
}
