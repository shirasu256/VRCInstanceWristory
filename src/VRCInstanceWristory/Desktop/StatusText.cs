using VRCInstanceWristory.Core;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 状態の段（ウィンドウの左下の「VRChat」「VRChat ログ」「SteamVR」「手首パネル」）の値・色・詳しい文
/// （2026-09-28のユーザー指定→実装メモ5.85）。
///
/// 決めるのは <see cref="DesktopStatus"/> だけからで、画面にも時刻にも依存しない（自動検証で全部の場合を確かめる）。
/// 同じ段に当てはまる状態がいくつあっても、出すのは上に書いた順で最初の1つ。
/// 色の使い分けは「赤＝パネルが出せない・読めない（手を打つ必要がある）、黄＝パネルは出るが一部が働かない・すぐに戻る見込み」。
/// 例外は、パネルの行と同じ赤に揃えたクラッシュ（「異常終了検知」）。
/// </summary>
public static class StatusText
{
    /// <summary>クラッシュに気づいてから「異常終了検知」を出す時間。</summary>
    public static readonly TimeSpan CrashNotice = TimeSpan.FromMinutes(1);

    /// <summary>内部履歴を組み立て直したことを出す時間。</summary>
    public static readonly TimeSpan CheckpointNotice = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 対応するログを特定できなくても「ログ待機中」とする時間。VRChat はプロセスが起動してからログのファイルを作るまでに間がある。
    /// 対応づけの幅（<c>processMatchAfterSeconds</c> の既定 180秒）に合わせる。
    /// </summary>
    public static readonly TimeSpan NoLogMatchGrace = TimeSpan.FromMinutes(3);

    /// <summary>ログを読めないことがこれより長く続いたら赤にする。</summary>
    public static readonly TimeSpan ReadFailureLong = TimeSpan.FromMinutes(5);

    /// <summary>ログが増えないことがこれより長く続いたら出す（実ログでは長くても3分ほどで次の行が書かれる）。</summary>
    public static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(10);

    /// <summary>ログの時刻と PC の時計のずれがこれ以上なら出す。</summary>
    public static readonly TimeSpan ClockSkewLimit = TimeSpan.FromMinutes(5);

    /// <summary>「ここへ戻る」で起動し直すのを待つ時間。過ぎたら「再起動中」をやめる。</summary>
    public static readonly TimeSpan RelaunchLimit = TimeSpan.FromMinutes(2);

    /// <summary>内部履歴の保存先（詳しい文に出す。エクスプローラーのアドレス欄にそのまま貼れる形）。</summary>
    private const string DataDirectory = @"%LOCALAPPDATA%\VRCInstanceWristory";

    /// <summary>
    /// エンジンの状態から、VRChat・ログの段の材料を作る。時間の長さで出し分けるものは、ここで <paramref name="nowUtc"/> と比べて決める。
    /// SteamVR・手首パネルの段の材料は、呼び出し側が <c>with</c> で足す。
    /// </summary>
    public static DesktopStatus Compose(EngineSnapshot snapshot, DateTime nowUtc, string? logDirectory)
    {
        var a = snapshot.Alerts;
        var health = snapshot.Health;

        var stalled = a.LastAppendUtc is { } appended && health == LogHealth.Ok ? nowUtc - appended : TimeSpan.Zero;
        var skew = a.ClockSkew is { } s && health == LogHealth.Ok ? s.Duration() : TimeSpan.Zero;

        return new DesktopStatus
        {
            ClientRunning = snapshot.ClientRunning,
            Health = health,
            MenuPageOpen = snapshot.MenuPageOpen,
            ClientCount = a.ClientCount,
            ProcessListFailing = a.ProcessListFailing,
            ProcessInfoUnreadable = a.ProcessInfoUnreadable,
            RecentCrash = a.CrashDetectedUtc is { } crash && nowUtc - crash < CrashNotice,
            LogFolder = a.LogFolder,
            LogDirectory = logDirectory,
            ReadFailure = a.ReadFailure,
            ReadFailingLong = health == LogHealth.ReadError && nowUtc - a.HealthSinceUtc >= ReadFailureLong,
            NoLogMatchLong = health == LogHealth.NoLogMatch && nowUtc - a.HealthSinceUtc >= NoLogMatchGrace,
            StalledMinutes = stalled >= StallLimit ? (int)stalled.TotalMinutes : 0,
            FormatSuspect = a.FormatSuspect,
            HistoryTruncated = a.HistoryTruncated,
            CheckpointRebuilt = a.CheckpointUnreadableUtc is { } rebuilt && nowUtc - rebuilt < CheckpointNotice,
            SaveFailing = a.SaveFailing,
            ClockSkewMinutes = skew >= ClockSkewLimit ? (int)Math.Round(skew.TotalMinutes) : 0,
        };
    }

    /// <summary>
    /// VR モードの VRChat が SteamVR 以外の経路で動いているか。
    /// ログの <c>StartVRSDK:</c> が OpenXR で、SteamVR のサーバーが動いていないとき（SteamVR の OpenXR で動いているならサーバーがある）。
    /// SteamVR 以外の経路のログは手元にないので、名前に「OpenXR」を含むかで見る。
    /// </summary>
    public static bool IsNonSteamVr(string? vrSdk, bool vrServerSeen)
        => !vrServerSeen && vrSdk is not null && vrSdk.Contains("OpenXR", StringComparison.OrdinalIgnoreCase);


    /// <summary>4つの項目（左上から「VRChat」「VRChat ログ」「SteamVR」「手首パネル」の順）。</summary>
    public static StatusItem[] Build(DesktopStatus status) =>
    [
        Client(status).ToItem("VRChat"),
        Log(status).ToItem("VRChat ログ"),
        Vr(status).ToItem("SteamVR"),
        Wrist(status).ToItem("手首パネル"),
    ];

    /// <summary>段の値・点の色・詳しい文（項目の名前は <see cref="Build"/> が付ける）。</summary>
    private readonly record struct StatusValue(string Value, StatusTone Tone = StatusTone.Idle, string? Detail = null)
    {
        public StatusItem ToItem(string label) => new(label, Value, Tone, Detail);
    }

    private const string MultipleClients = "複数の VRChat クライアントが同時に起動しています。";

    // ------------------------------------------------------------------ VRChat

    private static StatusValue Client(DesktopStatus s)
    {
        if (s.Relaunching)
            return new("再起動中", StatusTone.Idle, "「ここへ戻る」で VRChat を起動し直しています。");

        if (s.ClientCount > 1)
            return new("動作中・複数クライアント", StatusTone.Warning, MultipleClients + "どちらのログを追うか決められないため、訪問履歴の更新を止めています。VRChat を1つだけにしてください。");

        // クラッシュは、VRChat がまた起動したら（未接続以外になったら）すぐに譲る。
        if (s.RecentCrash && !s.ClientRunning)
            return new("異常終了検知", StatusTone.Error, "VRChat が終了の記録を残さずに終わりました（クラッシュ・強制終了など）。");

        if (s.ProcessListFailing)
            return new("プロセス取得失敗 (再試行中)", StatusTone.Warning, "VRChat のプロセスを確認できませんでした。自動で再試行します。");

        if (s.ProcessInfoUnreadable)
            return new("動作中・プロセス情報読取失敗", StatusTone.Warning, "VRChat のプロセスは見つかりましたが、起動時刻を読めません。自動で再試行します。続く場合は、VRChat と VRC Instance Wristory を起動し直してください。");

        if (!s.ClientRunning)
            return new("未接続");

        if (s.NonSteamVrRuntime)
            return new("動作中・SteamVR以外", StatusTone.Warning, "VRChat が SteamVR 以外の経路（OpenXR）で VR モードになっています。手首パネルは SteamVR のオーバーレイなので表示できません。");

        // 起動したが、まだログに今回の起動の記録がない（ログの段は「ログ待機中」）。
        if (s.Health == LogHealth.NoLogMatch && !s.NoLogMatchLong)
            return new("起動処理中");

        return new("正常動作中", StatusTone.Good);
    }

    // ------------------------------------------------------------------ VRChat ログ

    private static StatusValue Log(DesktopStatus s)
    {
        if (s.ClientCount > 1)
            return new("追跡停止・複数クライアント", StatusTone.Warning, MultipleClients + "ログの追跡を止めています。");

        if (s.LogFolder == LogFolderState.Missing)
        {
            var folder = s.LogDirectory is { Length: > 0 } dir ? $"（{dir}）" : string.Empty;

            return s.ClientRunning
                ? new("ログフォルダ到達失敗", StatusTone.Error, $"VRChat のログのフォルダー{folder}が見つかりません。設定ファイルで logDirectory を指定している場合は、その場所を確認してください。")
                : new("ログフォルダ無し", StatusTone.Idle, $"VRChat のログのフォルダー{folder}がまだありません。VRChat を一度起動すると作られます。");
        }

        switch (s.Health)
        {
            case LogHealth.Initializing:
                return new("初期化中");

            case LogHealth.ReadError:
                return ReadFailure(s.ReadFailure, s.ReadFailingLong ? StatusTone.Error : StatusTone.Warning);

            case LogHealth.Rebuilding when s.CheckpointRebuilt:
                return new("再構築中・内部履歴読込失敗", StatusTone.Warning, CheckpointRebuiltDetail);

            case LogHealth.Rebuilding:
                return new("再構築中");

            case LogHealth.NoLogMatch when s.NoLogMatchLong:
                return new("追跡失敗・ログ特定不能", StatusTone.Warning, "実行中の VRChat に対応するログのファイルが見つかりません。VRChat を起動し直してください。");

            case LogHealth.NoLogMatch:
                return new("ログ待機中");
        }

        if (!s.ClientRunning)
            return new(s.LogFolder == LogFolderState.Empty ? "ログファイル無し" : "待機中");

        if (s.FormatSuspect)
            return new("ログ解析不能", StatusTone.Error, "VRChat 側の更新でログの形式が変わった可能性があります。インスタンスの行を読み取れないため、訪問履歴を記録できません。このアプリの更新を待ってください。");

        if (s.CheckpointRebuilt)
            return new("追跡中・内部履歴再構築", StatusTone.Warning, CheckpointRebuiltDetail);

        if (s.SaveFailing)
            return new("追跡中・履歴保存不能", StatusTone.Warning, $"内部履歴を保存できないため、再起動すると訪問履歴や目印を復元できないことがあります。保存先（{DataDirectory}）の空き容量や権限を確認してください。");

        if (s.StalledMinutes > 0)
            return new("追跡中・長時間更新停止", StatusTone.Warning, $"VRChat のログが {s.StalledMinutes} 分間更新されていません。ログを正常に追跡できていない可能性があります。");

        if (s.ClockSkewMinutes > 0)
            return new("追跡中・時刻異常", StatusTone.Warning, $"ログの時刻と PC の時計が {s.ClockSkewMinutes} 分ずれています。滞在時間などが正しく計算されない可能性があります。VRChat の起動中に PC の時計や時間帯を変えた場合は、VRChat を起動し直してください。");

        if (s.HistoryTruncated)
            return new("追跡中・一部未読込", StatusTone.Warning, "60分以内の起動し直しが続いたため、古いログの一部を読み込んでいません。それより前の訪問は表示されません。");

        return new("正常追跡中", StatusTone.Good);
    }

    private const string CheckpointRebuiltDetail =
        "VRC Instance Wristory の内部履歴を読めなかったため、VRChat のログから組み立て直しました。ログから分からない情報（「延長」を押した時刻など）は失われています。";

    private static StatusValue ReadFailure(LogReadFailure failure, StatusTone tone) => failure switch
    {
        LogReadFailure.AccessDenied => new("読取失敗・アクセス拒否", tone, "VRChat のログを読む権限がありません。自動で再試行します。"),
        LogReadFailure.SharingViolation => new("読取失敗・他プロセス使用中", tone, "ほかのアプリが VRChat のログを開いたまま、読めないようにしています。自動で再試行します。"),
        LogReadFailure.DriveMissing => new("読取失敗・ドライブ未接続", tone, "VRChat のログがあるドライブが見つかりません。自動で再試行します。"),
        _ => new("読取失敗 (再試行中)", tone, "VRChat のログを読めません。自動で再試行します。"),
    };

    // ------------------------------------------------------------------ SteamVR

    private static StatusValue Vr(DesktopStatus s)
    {
        if (s.VrConnected)
            return VrIssue(s.VrIssues);

        if (s.VrQuitting)
            return new("終了中");

        if (s.VrError != VrConnectError.None)
            return VrConnect(s.VrError, s.VrErrorName);

        return new(s.VrServerSeen ? "動作中・接続待機中" : "未接続");
    }

    /// <summary>つながっているときの値。問題がいくつあっても、上の順で最初の1つだけを出す。</summary>
    private static StatusValue VrIssue(VrRuntimeIssue issues)
    {
        if (issues.HasFlag(VrRuntimeIssue.FrameErrors))
            return new("接続中・例外検知", StatusTone.Warning, $"手首パネルの処理で例外が繰り返し発生しています。続く場合は、このアプリを終了する前に記録（{DataDirectory}\\app.log）を保存して、開発者へ知らせてください。");

        if (issues.HasFlag(VrRuntimeIssue.TextureCreateFailed))
            return new("接続中・描画エラー (テクスチャ作成)", StatusTone.Error, "手首パネルの絵を置くテクスチャを作れません。SteamVR を再起動してください。");

        if (issues.HasFlag(VrRuntimeIssue.TextureRetrying))
            return new("接続中・描画エラー (再試行中)", StatusTone.Warning, "手首パネルの絵を SteamVR へ渡せません。再試行中です。手首パネルが古い内容のまま更新されないことがあります。");

        if (issues.HasFlag(VrRuntimeIssue.InputUnavailable))
            return new("接続中・操作不可 (コントローラー割り当て)", StatusTone.Warning, "コントローラーの割り当て（アクションマニフェスト）を読めません。手首パネルは表示しますが、操作はできません。");

        if (issues.HasFlag(VrRuntimeIssue.ControllerUnbound))
            return new("接続中・未知コントローラー", StatusTone.Warning, "このコントローラーには VRC Instance Wristory の操作の割り当てがありません。SteamVR のコントローラーの割り当て（バインディング）の画面で、VRC Instance Wristory の割り当てを作ってください。");

        if (issues.HasFlag(VrRuntimeIssue.DashboardUnavailable))
            return new("接続中・ダッシュボード未表示", StatusTone.Warning, "SteamVR のダッシュボードに VRC Instance Wristory の設定画面を出せません。設定はこのウィンドウから変えられます。");

        if (issues.HasFlag(VrRuntimeIssue.ApplicationUnregistered))
            return new("接続中・アプリ登録失敗", StatusTone.Warning, "SteamVR にこのアプリを登録できませんでした。「SteamVRと一緒に起動する」と、SteamVR でのコントローラーの割り当ての編集が使えません。");

        return new("正常動作中", StatusTone.Good);
    }

    /// <summary>SteamVR は動いているのにつなげないときの値。頭の「動作中・」は「SteamVR は動いているが、つながっていない」の意味。</summary>
    private static StatusValue VrConnect(VrConnectError error, string? name)
    {
        const string Retry = "資源が空けば、再起動しなくても自動でつながります。";

        switch (error)
        {
            case VrConnectError.TextureDeviceExhausted:
                return new("動作中・接続エラー (D3D11デバイス枯渇)", StatusTone.Error, "Direct3D 11 デバイスの新規作成に失敗しました。ほかのアプリ（Discord のクリップ機能・XSOverlay など）が GPU ドライバーの資源を使い切っている可能性があります。そのアプリか、SteamVR・PC を再起動してください。" + Retry);

            case VrConnectError.TextureDevice:
                return new("動作中・接続エラー (D3D11デバイス)", StatusTone.Error, "Direct3D 11 デバイスの新規作成に失敗しました。GPU のドライバーを更新するか、SteamVR を再起動してください。");

            case VrConnectError.Direct3DUnavailable:
                return new("動作中・接続エラー (D3D11使用不能)", StatusTone.Error, "Direct3D 11 を使えません。GPU のドライバーを入れ直してください。");

            case VrConnectError.AdapterNotFound:
                return new("動作中・接続エラー (GPU取得失敗)", StatusTone.Error, "SteamVR が使用中の GPU を取得できませんでした。SteamVR を再起動してください。");

            case VrConnectError.OpenVrApiMissing:
                return new("動作中・接続エラー (openvr_api.dll)", StatusTone.Error, "openvr_api.dll を読み込めません。VRC Instance Wristory のフォルダーを、配布の zip から展開し直してください。");

            case VrConnectError.HeadsetNotFound:
                return new("動作中・ヘッドセット未検出", StatusTone.Idle, "SteamVR がヘッドセットを見つけていません。ヘッドセットをつなぐと、自動でつながります。");

            case VrConnectError.RuntimeOutdated:
                return new("動作中・接続エラー (SteamVRバージョン)", StatusTone.Error, "SteamVR のバージョンが古く、VRC Instance Wristory と互換性がありません。SteamVR を更新してください。");

            case VrConnectError.InstallationBroken:
                return new("動作中・接続エラー (SteamVR整合性)", StatusTone.Error, "SteamVR のインストールが壊れている可能性があります。Steam のライブラリで SteamVR のプロパティを開き、「インストール済みファイル」の「ゲームファイルの整合性を確認」を実行してください。");

            case VrConnectError.Ipc:
                return new("動作中・アプリ間通信エラー (再試行中)", StatusTone.Error, "SteamVR と通信できません。自動で再試行します。続く場合は SteamVR を再起動してください。");

            case VrConnectError.PermissionMismatch:
                return new("動作中・権限レベル不一致", StatusTone.Error, "SteamVR が管理者として起動しているため、VRC Instance Wristory と通信できません。SteamVR を、管理者としてではなく起動し直してください。");

            case VrConnectError.DeviceBusy:
                return new("動作中・更新中", StatusTone.Idle, "ヘッドセットなどのファームウェアの更新・再起動が終わると、自動でつながります。");

            case VrConnectError.OverlayUnavailable:
                return new("動作中・接続エラー (オーバーレイ)", StatusTone.Error, "SteamVR のオーバーレイの機能を使えません。SteamVR を再起動してください。");

            case VrConnectError.CompositorDown:
                return new("動作中・コンポジター停止", StatusTone.Error, "SteamVR の画面を合成する処理（vrcompositor）が止まっているため、手首パネルを表示できません。GPU のメモリーが足りないときにも起きます。SteamVR の画面に出ているエラーを確かめて、SteamVR を再起動してください。");

            case VrConnectError.GpuReset:
                return new("動作中・GPUリセット (再接続中)", StatusTone.Warning, "GPU のドライバーがリセットされたため、つなぎ直しています。繰り返す場合は、GPU が不安定になっている可能性があります。");

            case VrConnectError.OverlayKeyInUse:
                return new("動作中・描画エラー (オーバーレイ名重複)", StatusTone.Error, "以前起動したこのアプリのオーバーレイが SteamVR に残っています。自動で再試行します。改善しない場合は SteamVR を再起動してください。");

            case VrConnectError.OverlayLimit:
                return new("動作中・描画エラー (オーバーレイ数上限)", StatusTone.Error, "SteamVR のオーバーレイの数が上限に達しています。ほかのオーバーレイのアプリを終了してください。自動で再試行します。");

            case VrConnectError.OverlayCreate:
                return new("動作中・描画エラー (オーバーレイ作成)", StatusTone.Error, "手首パネルのオーバーレイを作れません。SteamVR を再起動してください。");

            default:
                var shown = name ?? error.ToString();
                return new($"動作中・接続エラー (初期化 {shown})", StatusTone.Error, $"OpenVR を初期化できません（{shown}）。SteamVR を再起動してください。");
        }
    }

    // ------------------------------------------------------------------ 手首パネル

    private static StatusValue Wrist(DesktopStatus s)
    {
        if (!s.VrConnected)
            return new("SteamVR未接続");

        if (!s.VrOverlayEnabled)
            return new("非表示・VRオーバーレイ機能オフ", StatusTone.Quiet, "設定の「VR オーバーレイ機能を有効にする」がオフになっています。");

        if (s.HeadsetStandby)
            return new("非表示・ヘッドセット未装着", StatusTone.Quiet);

        switch (s.VrHideReason)
        {
            case PanelHideReason.None when s.OperatingHandMissing:
                return new("表示中・操作不可 (片手未接続)", StatusTone.Warning, "操作する手のコントローラーが見つかりません。手首パネルは表示しますが、指して操作することはできません。");

            case PanelHideReason.None:
                return new("表示中", StatusTone.Good);

            case PanelHideReason.ViewAngle:
                return new("非表示・基準角度未満", StatusTone.Quiet);

            case PanelHideReason.ControllerMissing:
                return new("非表示・コントローラー未接続", StatusTone.Warning, "パネルを付ける手のコントローラーが見つかりません。電源が入っているか確認してください。");

            case PanelHideReason.WristTrackingLost:
                return new("非表示・コントローラー追跡喪失", StatusTone.Warning);

            case PanelHideReason.TextureNotReady:
                return new("描画準備中");
        }

        // ログ側の条件が揃っていない（ContentNotReady）。
        if (s.ClientCount > 1)
            return new("非表示・複数クライアント", StatusTone.Warning);

        if (!s.ClientRunning)
            return new("非表示・VRChat未起動");

        if (s.Health != LogHealth.Ok)
            return new("非表示・ログ未追跡");

        return new(s.MenuPageOpen ? "非表示・訪問履歴無し" : "非表示・メインメニュー非表示", StatusTone.Quiet);
    }
}
