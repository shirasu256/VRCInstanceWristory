using System.Numerics;
using System.Text.Json.Serialization;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// 設定（仕様10節）。無効な値は項目名をPC側へ知らせて既定値へ戻し、VR内にはエラーを出さない。
/// ファイルを直接書き換えたときの反映は再起動でよい（ウィンドウ・ダッシュボードで変えたものはその場で反映する）。
///
/// | ファイル | 中身 |
/// | --- | --- |
/// | <c>AppSettings.cs</c> | 項目と、それを解釈した値 |
/// | <c>AppSettings.Placement.cs</c> | 手首ごとのパネルの配置 |
/// | <c>AppSettings.Validate.cs</c> | 無効な値を既定値へ戻す |
/// | <c>AppSettings.File.cs</c> | 設定ファイルの読み書き（変えた項目だけを書き戻す） |
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>null なら現ユーザーの LocalLow/VRChat/VRChat。</summary>
    public string? LogDirectory { get; set; }

    /// <summary>
    /// 左手コントローラー基準の相対位置（m）。既定は利用者が実機で掴んで置いた配置
    /// （2026-09-27に焼き込んだ→実装メモ5.76。それまでは実機で測っていない開始値 [0, 0.02, 0.09]）。
    /// </summary>
    public float[] TranslationMeters { get; set; } = [-0.1942504f, -0.024928987f, 0.08906134f];

    /// <summary>
    /// 相対回転を度数で指定する（pitch = X軸まわり、yaw = Y軸まわり、roll = Z軸まわり）。
    /// 通常はこちらを使う。パネルを掴んで置き直すとこの値が書き戻される。
    /// 既定は位置と同じく利用者が実機で置いた配置（→実装メモ5.76。それまでは [-70, 0, 0]）。
    /// </summary>
    public float[] RotationEulerDegrees { get; set; } = [-48.22f, -170.77f, -98.8f];

    /// <summary>
    /// 上級者向け。クォータニオン（x, y, z, w）で直接指定したい場合だけ書く。
    /// 指定があるとこちらが優先される。
    /// </summary>
    public float[]? RotationQuaternion { get; set; }

    /// <summary>
    /// パネルを付ける手首（<c>left</c> / <c>right</c>・2026-09-26のユーザー指定→実装メモ5.49）。
    /// 右手首にすると、パネルは右コントローラーに付き、操作（レイ・スティック・グリップ・トリガー）は左手になる。
    /// </summary>
    public string WristSide { get; set; } = WristSides.SettingName(Core.WristSide.Left);

    /// <summary>
    /// 右手首に付けたときの相対位置（m）。右コントローラー基準。
    /// 書いていなければ、左手首の配置（<see cref="TranslationMeters"/>）を左右反転したものを使う（→<see cref="WristSides.Mirror(Vector3)"/>）。
    /// 右手首で掴んで置き直すと、ここへ書き戻される。
    /// </summary>
    public float[]? RightTranslationMeters { get; set; }

    /// <summary>右手首に付けたときの相対回転の度数 <c>[pitch, yaw, roll]</c>。書いていなければ左手首の配置を左右反転したもの。</summary>
    public float[]? RightRotationEulerDegrees { get; set; }

    public float OverlayWidthMeters { get; set; } = 0.14f;

    public float BackgroundOpacity { get; set; } = 0.9f;

    public float IdFontPixels { get; set; } = 44f;

    public float AuxFontPixels { get; set; } = 22f;

    public float ScrollDeadzone { get; set; } = 0.20f;

    public float ScrollRowsPerSecond { get; set; } = 6.5f;

    /// <summary>
    /// 右手で指した位置に出すカーソル（輪）のオーバーレイの幅（m）。輪の直径はこの9割。
    /// 2026-09-25のユーザー指定で、直径を半分にするため 0.012 から 0.006 へ下げた（輪は約5.4mm）。
    /// </summary>
    public float CursorSizeMeters { get; set; } = 0.006f;

    /// <summary>
    /// パネルを正面から見たときを0度として、上下左右いずれかがこの角度以上ずれたら隠す。
    /// 手首を返して見たときだけ出すための条件（2026-09-13のユーザー指定）。
    /// </summary>
    public float ViewAngleLimitDegrees { get; set; } = 30f;

    /// <summary>
    /// 視線角度の条件が切り替わってから、表示・非表示を渡しきるまでの時間（秒）。
    /// 0 にすると一瞬で切り替わる従来の動き（2026-09-19のユーザー指定で既定は0.2秒。2026-09-27に利用者の値を既定にした。いまは0.15秒→実装メモ5.69・5.70）。
    /// </summary>
    public float ViewAngleFadeSeconds { get; set; } = 0.15f;

    /// <summary>
    /// 対象インスタンスを離れてから履歴をまとめて消すまでの分数（既定60分）。
    /// 「延長」を押したときも、押した時点からこの分数を数える。
    /// 2026-09-26のユーザー指定で、デスクトップのウィンドウから変えられるようにした（→実装メモ5.39）。
    /// </summary>
    public int RetentionMinutes { get; set; } = (int)HistoryStore.DefaultRetention.TotalMinutes;

    /// <summary>
    /// 履歴の自動リセット（対象を離れてから <see cref="RetentionMinutes"/> で履歴をまとめて消す）を使うか（既定はオン→実装メモ5.71）。
    /// オフにすると期限を数えず、手で「リセット」を押すまで履歴を残す（アプリを再起動しても残る）。
    /// </summary>
    public bool AutoResetEnabled { get; set; } = true;

    /// <summary>
    /// VRオーバーレイ機能（手首のパネル）を使うか（既定はオン→実装メモ5.71）。
    /// オフにすると手首のパネルを出さない。SteamVRへのつなぎ方とダッシュボードの設定の画面は変えない（ダッシュボードから戻せるように）。
    /// </summary>
    public bool VrOverlayEnabled { get; set; } = true;

    /// <summary>
    /// 手首のパネルを操作したとき（指す部品が変わる・押す・掴む）に、操作する手のコントローラーを短く振動させるか
    /// （既定はオフ・2026-09-28のユーザー指定→実装メモ5.78）。
    /// </summary>
    public bool ControllerVibrationEnabled { get; set; }

    /// <summary>
    /// 手首のパネルをグリップで掴んで動かせるか（既定はオン・2026-09-28のユーザー指定→実装メモ5.78）。
    /// オフにすると、グリップを握ってもパネルは動かない（VRChat の操作でグリップを使うときの誤操作を防ぐ）。
    /// </summary>
    public bool PanelGrabEnabled { get; set; } = true;

    /// <summary>
    /// 手首のパネルの行を指してトリガーを引いたとき、操作メニュー（目印と「ブラウザで開く」／「ここへ戻る」のポップアップ）を出すか
    /// （既定はオン・2026-09-28のユーザー指定→実装メモ5.86）。オフにすると、行を指しても白くせず、トリガーでは何も開かない。
    /// 見出しの「延長」「リセット」はオフでもトリガーで押せる。デスクトップのウィンドウの右クリックは変えない。
    /// </summary>
    public bool TriggerMenuEnabled { get; set; } = true;

    /// <summary>
    /// 履歴の自動リセットの前に、VRChatのマイクのアイコンの横へ予告のアイコンを出すか（既定はオフ→実装メモ5.87・5.89・5.90）。
    /// </summary>
    public bool ResetWarningEnabled { get; set; }

    /// <summary>予告のアイコンを置く場所（マイクのアイコンから見た向き。<c>bottomLeft</c> など8方向・既定は左下→実装メモ5.89）。</summary>
    public string ResetWarningPosition { get; set; } = ResetWarningPositions.SettingName(ResetWarningPositions.Default);

    /// <summary>予告のアイコンの大きさ（マイクのアイコンと同じ大きさを1とした倍率。0.6 / 0.8 / 1 / 1.2 / 1.4）。</summary>
    public float ResetWarningScale { get; set; } = ResetWarningOptions.DefaultScale;

    /// <summary>予告のアイコンの不透明度（0.5〜1・既定1）。</summary>
    public float ResetWarningOpacity { get; set; } = ResetWarningOptions.DefaultOpacity;

    /// <summary>予告のアイコンの点滅回数（1 / 3 / 5 / 10 / 30・既定5）。</summary>
    public int ResetWarningBlinkCount { get; set; } = ResetWarningOptions.DefaultBlinkCount;

    /// <summary>リセットの何分前に予告するか（1 / 3 / 5・既定3）。</summary>
    public int ResetWarningLeadMinutes { get; set; } = ResetWarningOptions.DefaultLeadMinutes;

    /// <summary>
    /// マイクアイコンの位置の合わせ（横・cm・右が正→実装メモ5.92）。予告のアイコンを並べる基準（VRChatのマイクのアイコンの中心の想定）を動かす。
    /// </summary>
    public float ResetWarningMicOffsetXCm { get; set; }

    /// <summary>マイクアイコンの位置の合わせ（縦・cm・上が正→実装メモ5.92）。</summary>
    public float ResetWarningMicOffsetYCm { get; set; }

    /// <summary>マイクアイコンの位置の合わせ（奥行き・cm・奥が正→実装メモ5.95）。見える方向と見かけの大きさは保ったまま距離だけを変える。</summary>
    public float ResetWarningMicOffsetZCm { get; set; }

    /// <summary>
    /// VRChat の AFK の間（または SteamVR のダッシュボードを開いている間）に予告を見逃したとき、戻ってきた時点でまだリセットされていなければ
    /// もう一度出すか（既定はオン→実装メモ5.89）。AFK は VRChat の OSC（OSCQuery）で受け取る。
    /// </summary>
    public bool ResetWarningReshowAfterAfk { get; set; } = true;

    /// <summary>
    /// VRChat の AFK の間、履歴リセットまでのカウントダウンを止めるか（既定はオフ→実装メモ5.89）。AFK は VRChat の OSC（OSCQuery）で受け取る。
    /// </summary>
    public bool PauseCountdownWhileAfk { get; set; }

    /// <summary>
    /// VRChat の AFK を検知するか（既定はオフ・2026-09-30のユーザー指定→実装メモ5.98・5.99）。オンの間だけ OSC（OSCQuery・mDNS）の受け口を開く。
    /// mDNS は LAN 側のポート（UDP 5353）を開くので、初めて開くときに Windows のファイアウォールの許可を求められる。
    /// 起動しただけでその画面が出ないよう既定はオフにし、利用者がオンにしたその場で開く。オンにするのは、初期設定の画面の
    /// 「VRChatのAFKを検知する」か、AFK を使う2つの設定（「AFK中はカウントダウンを停止する」「AFKから復帰時に再表示する」）をオンにしたとき。
    /// 2つの設定は、これがオンのときだけ働く（<see cref="AfkPauseActive"/>・<see cref="ResetWarningReshowActive"/>）。
    /// </summary>
    public bool AfkDetectionEnabled { get; set; }

    /// <summary>
    /// 初回起動の案内と初期設定を終えたか（既定はオフ→実装メモ5.97・5.98）。オフの間は、デスクトップのウィンドウに中身の代わりに案内を出す。
    /// 初期設定の画面の「はじめる」を押したときにオンにして書き戻す（「わかった」だけでは書かないので、途中で終えると次もまた案内から出る）。
    /// 2026-09-30のユーザー指定で、専用の welcome.json をやめてここへ入れた（→実装メモ5.100）。
    /// </summary>
    public bool WelcomeCompleted { get; set; }

    /// <summary>AFK の間にカウントダウンを止めるか（値がオンでも、AFK を検知していなければ止めない→実装メモ5.99）。</summary>
    [JsonIgnore]
    public bool AfkPauseActive => PauseCountdownWhileAfk && AfkDetectionEnabled;

    /// <summary>AFK（またはダッシュボード）から戻ったときに予告を出し直すか（同上）。</summary>
    [JsonIgnore]
    public bool ResetWarningReshowActive => ResetWarningReshowAfterAfk && AfkDetectionEnabled;

    /// <summary>
    /// 記録する種類（訪問履歴に含めるインスタンスタイプ）のインスタンスに滞在している間は、履歴リセットまでのカウントダウンを止めるか
    /// （既定はオン→実装メモ5.90）。オフにすると滞在中も数え、保持時間を超えるといまの滞在の行だけを残して前の行を消す。
    /// </summary>
    public bool StopCountdownInTarget { get; set; } = true;

    /// <summary>
    /// 外部からの履歴リセットのコマンド（名前付きパイプへの <c>echo</c>）を受け付けるか（既定はオン・2026-09-28のユーザー指定→実装メモ5.83）。
    /// OyasumiVR の「コマンドの実行」から、睡眠モードが有効になったときに訪問履歴をリセットさせる用途を想定している。
    /// オフにすると、パイプを開かない（コマンドは「指定されたファイルが見つかりません」で終わる）。
    /// </summary>
    public bool ExternalResetEnabled { get; set; } = true;

    /// <summary>
    /// 新しい版を自動で確かめるか（既定はオン→実装メモ5.121）。確かめるときに GitHub へ問い合わせる。
    /// オフでも「今すぐ確認」では確かめる。インストーラーで入れた版でなければ、どちらも使えない。
    /// </summary>
    public bool UpdateCheckEnabled { get; set; } = true;

    /// <summary>アップデートの状態（→実装メモ5.121）。設定ではなく、主ループが <see cref="AppUpdater"/> から写して設定の画面へ渡す。</summary>
    [JsonIgnore]
    public UpdateStatus Update { get; set; } = UpdateStatus.Unavailable;

    /// <summary>
    /// 外部からのコマンドで最後に訪問履歴をリセットした時刻（UTC）。設定ファイルには書かず、<see cref="AppPaths.ExternalReset"/> に残す（→実装メモ5.83）。
    /// </summary>
    [JsonIgnore]
    public DateTime? ExternalResetLastRunUtc { get; set; }

    /// <summary>
    /// 「直前のリセットを戻す」を押せるか（→実装メモ5.86）。設定ではなく、主ループがエンジン（<c>HistoryEngine.CanUndoClearHistory</c>）から写して両方の設定の画面へ渡す。
    /// </summary>
    [JsonIgnore]
    public bool UndoResetAvailable { get; set; }

    /// <summary>
    /// 記録する種類（既定は Public / GroupPublic / GroupPlus / GroupOnly）。名前は <see cref="TargetAccessTypes.SettingName"/>。
    /// 2026-09-26のユーザー指定で、デスクトップのウィンドウから選べるようにした（→実装メモ5.39）。
    /// </summary>
    public string[] TargetAccessTypes { get; set; } =
        [.. Core.Locations.TargetAccessTypes.Selectable.Where(Core.Locations.TargetAccessTypes.Default.Contains).Select(Core.Locations.TargetAccessTypes.SettingName)];

    /// <summary>
    /// 行のボタンで何を開くか（<c>browser</c> / <c>vrchat</c>・2026-09-27のユーザー指定→実装メモ5.53）。
    /// <c>browser</c>（既定）はそのインスタンスのWebページを既定のブラウザで開き、ボタンは「ブラウザで開く」。
    /// <c>vrchat</c> は <c>vrchat://launch</c> で開き、ボタンは「ここへ戻る」。VRChat はクライアントを起動し直すので、
    /// FBT のキャリブレーションなどはやり直しになる。
    /// </summary>
    public string ReturnAction { get; set; } = ReturnActions.SettingName(Core.Locations.ReturnAction.Browser);

    /// <summary>
    /// 写真のサムネイルを押したときに開くアプリ（<c>default</c> / <c>custom</c>・2026-09-27のユーザー指定→実装メモ5.55・5.58）。
    /// <c>default</c>（既定）はその種類のファイルの既定のアプリ、<c>custom</c> は <see cref="PhotoViewerPath"/> の実行ファイル。
    /// </summary>
    public string PhotoViewer { get; set; } = PhotoViewers.SettingName(PhotoViewerKind.Default);

    /// <summary>写真を開くアプリの実行ファイル（<see cref="PhotoViewer"/> が <c>custom</c> のとき）。ウィンドウで選ぶ。</summary>
    public string? PhotoViewerPath { get; set; }

    /// <summary>
    /// 利用者が付けたグループ名（<c>grp_…</c> → 名前・2026-09-26のユーザー指定→実装メモ5.48）。
    /// VRChat API は使わない。デスクトップのウィンドウで行を選んで付けるか、ここへ直接書く。
    /// </summary>
    public Dictionary<string, string> GroupNames { get; set; } = new(StringComparer.Ordinal);

    /// <summary>名前を付けたグループも、名前の後ろに Group ID を並べて出すか（既定は名前だけ→実装メモ5.48）。</summary>
    public bool ShowGroupIdWithName { get; set; }

    /// <summary>
    /// ロード画面の間も手首のパネルを出すか（オンならメインメニューを開いているのと同じ扱い→実装メモ5.31・5.62）。
    /// 既定は利用者の値を焼き込んだもの（オン。5.69で一時オフにしていた→実装メモ5.70）。
    /// オフにすると、移動を始めた時点（<c>Destination set</c>）で閉じ、ロード画面が終わっても出し直さない。
    /// </summary>
    public bool ShowPanelDuringLoading { get; set; } = true;

    /// <summary>
    /// SteamVRと一緒に起動するようにSteamVRへ登録してあるか（→実装メモ5.50）。設定ファイルには書かない。
    /// 正本はSteamVR側の登録で、つながっている間だけ分かる（つながっていなければ null）。
    /// </summary>
    [JsonIgnore]
    public bool? LaunchWithSteamVr { get; set; }

    /// <summary>
    /// Windowsのログオン時に起動するよう登録してあるか（→実装メモ5.51）。設定ファイルには書かない。
    /// 正本は <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> の登録。
    /// </summary>
    [JsonIgnore]
    public bool LaunchAtLogon { get; set; }

    /// <summary>デスクトップにウィンドウを出すか。<c>--no-window</c> を付けて起動しても出さない（→実装メモ5.39）。</summary>
    public bool ShowDesktopWindow { get; set; } = true;

    /// <summary>デスクトップのウィンドウを常に手前に出すか。</summary>
    public bool DesktopWindowTopMost { get; set; }

    /// <summary>ログセッションとプロセスの対応に許す時刻差（秒）。検証環境で実測する。</summary>
    public double ProcessMatchBeforeSeconds { get; set; } = 60;

    public double ProcessMatchAfterSeconds { get; set; } = 180;

    /// <summary>保持時間（<see cref="RetentionMinutes"/>）。</summary>
    [JsonIgnore]
    public TimeSpan Retention => TimeSpan.FromMinutes(RetentionMinutes);

    /// <summary>記録する種類（<see cref="TargetAccessTypes"/> を解釈したもの）。知らない名前は含めない。</summary>
    [JsonIgnore]
    public IReadOnlySet<AccessType> TargetTypes => ParseTargetTypes(TargetAccessTypes);

    /// <summary>記録する種類を設定へ書く。並びはウィンドウと同じ（<see cref="Core.Locations.TargetAccessTypes.Selectable"/>）にそろえる。</summary>
    public void SetTargetTypes(IReadOnlySet<AccessType> types)
        => TargetAccessTypes = [.. Core.Locations.TargetAccessTypes.Selectable.Where(types.Contains).Select(Core.Locations.TargetAccessTypes.SettingName)];

    private static HashSet<AccessType> ParseTargetTypes(string[]? names)
    {
        var types = new HashSet<AccessType>();

        foreach (var name in names ?? [])
        {
            if (Core.Locations.TargetAccessTypes.ParseSettingName(name) is { } type)
                types.Add(type);
        }

        return types;
    }

    /// <summary>パネルを付ける手首（<see cref="WristSide"/> を解釈したもの）。知らない値なら左。</summary>
    [JsonIgnore]
    public Core.WristSide Wrist
    {
        get => WristSides.Parse(WristSide) ?? Core.WristSide.Left;
        set => WristSide = WristSides.SettingName(value);
    }

    /// <summary>予告のアイコンの場所（<see cref="ResetWarningPosition"/> を解釈したもの）。知らない値なら左下。</summary>
    [JsonIgnore]
    public Core.Presentation.ResetWarningPosition WarningPosition
    {
        get => ResetWarningPositions.Parse(ResetWarningPosition) ?? ResetWarningPositions.Default;
        set => ResetWarningPosition = ResetWarningPositions.SettingName(value);
    }

    /// <summary>予告の表示タイミング。</summary>
    [JsonIgnore]
    public TimeSpan ResetWarningLead => TimeSpan.FromMinutes(ResetWarningLeadMinutes);

    /// <summary>VRChat の OSC（AFK の状態）を受け取る必要があるか（→実装メモ5.89）。どちらも使わないなら受け口を開かない。</summary>
    [JsonIgnore]
    public bool NeedsAfkState => AfkDetectionEnabled;

    /// <summary>行のボタンで何を開くか（<see cref="ReturnAction"/> を解釈したもの）。知らない値ならブラウザ。</summary>
    [JsonIgnore]
    public Core.Locations.ReturnAction OpenAction
    {
        get => ReturnActions.Parse(ReturnAction) ?? Core.Locations.ReturnAction.Browser;
        set => ReturnAction = ReturnActions.SettingName(value);
    }

    /// <summary>写真を開くアプリ（<see cref="PhotoViewer"/> を解釈したもの）。知らない値なら既定のアプリ。</summary>
    [JsonIgnore]
    public PhotoViewerKind Viewer
    {
        get => PhotoViewers.Parse(PhotoViewer) ?? PhotoViewerKind.Default;
        set => PhotoViewer = PhotoViewers.SettingName(value);
    }

    /// <summary>グループ名とその出し方（表示側へ渡す写し）。</summary>
    [JsonIgnore]
    public GroupNaming GroupNaming => new(new Dictionary<string, string>(GroupNames, StringComparer.Ordinal), ShowGroupIdWithName);
}
