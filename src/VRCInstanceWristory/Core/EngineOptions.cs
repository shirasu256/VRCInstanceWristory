using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core;

/// <summary>
/// 履歴エンジンの設定。組み立てたあとは変えない（実行中に変わる設定は <see cref="HistoryEngine"/> の Set… で渡す）。
/// 検証で一部だけを変えるときは <c>with</c> で写しを作る。
/// </summary>
public sealed record EngineOptions
{
    public string LogDirectory { get; init; } = Infrastructure.LogDirectory.DefaultLogDirectory();

    public TimeSpan JoinWindow { get; init; } = VisitTracker.DefaultJoinWindow;

    public TimeSpan WorldNameWindow { get; init; } = VisitTracker.DefaultWorldNameWindow;

    /// <summary>ログセッション開始がプロセス開始より前にあってよい差（時計誤差の吸収）。</summary>
    public TimeSpan ProcessMatchBefore { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>プロセス開始からログファイルが現れるまでに許す差。検証環境で実測する。</summary>
    public TimeSpan ProcessMatchAfter { get; init; } = TimeSpan.FromSeconds(180);

    public TimeSpan TailInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan DirectoryScanInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 訪問履歴（history.json→実装メモ5.108）を書く間隔の下限。人の出入りでも行の中身は変わるので、変わるたびには書かない。
    /// VRChat の終了・リセット・アプリの終了のときは待たずに書く。
    /// </summary>
    public TimeSpan HistorySaveInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>検証用の再生（提供ログを読む受入試験）での上限時刻。これより後のログ行は読まない。本番では null。</summary>
    public DateTime? StopAtUtc { get; init; }

    /// <summary>
    /// ログの行を処理する直前に、その行の時刻（UTC）を知らせる。検証用の再生で、手で進める時計をログの時刻へ合わせるのに使う。
    /// 本番では null。
    /// </summary>
    public Action<DateTime>? OnLogTime { get; init; }

    /// <summary>ソースIDの名前空間。検証用の再生は本番と分ける（仕様5.3節）。</summary>
    public string SourceNamespace { get; init; } = "s";

    /// <summary>チェックポイントを保存するか。検証では保存しない。</summary>
    public bool PersistCheckpoint { get; init; } = true;

    /// <summary>検証用の再生で1ファイルだけを対象にする場合のファイル名。null ならフォルダー直下すべて。</summary>
    public string? OnlyFileName { get; init; }

    /// <summary>
    /// 対象インスタンスを離れてから履歴をまとめて消すまでの時間。「延長」を押したときも、押した時点からこの時間を数える。
    /// 設定 <c>retentionMinutes</c> から決まり、実行中は <see cref="HistoryEngine.SetRetention"/> で変える（→実装メモ5.39）。
    /// </summary>
    public TimeSpan Retention { get; init; } = HistoryStore.DefaultRetention;

    /// <summary>
    /// 履歴の自動リセットを使うか（設定 <c>autoResetEnabled</c>→実装メモ5.71）。false なら期限を数えず、
    /// 手で消す（<see cref="HistoryEngine.ClearHistory"/>）まで行を残す。実行中は <see cref="HistoryEngine.SetAutoReset"/> で変える。
    /// </summary>
    public bool AutoReset { get; init; } = true;

    /// <summary>
    /// 記録する種類。設定 <c>targetAccessTypes</c> から決まり、実行中は
    /// <see cref="HistoryEngine.SetTargetTypes"/> で変える（→実装メモ5.39）。
    /// </summary>
    public IReadOnlySet<AccessType> TargetTypes { get; init; } = TargetAccessTypes.Default;

    /// <summary>ロード画面の間もパネルを出すか（→実装メモ5.62）。<see cref="HistoryEngine.SetShowDuringLoadingScreen"/> で変える。</summary>
    public bool ShowDuringLoadingScreen { get; init; } = true;

    /// <summary>
    /// 対象インスタンスに滞在している間はカウントダウンを止めるか（既定は true→実装メモ5.90）。
    /// false なら滞在中も期限を数える。<see cref="HistoryEngine.SetStopCountdownInTarget"/> で変える。
    /// </summary>
    public bool StopCountdownInTarget { get; init; } = true;

    /// <summary>
    /// 対象セッションを新しいカウント期間の先頭（0開始）として扱う。
    /// 仕様11.1節の「明示的な0開始条件」での検証用の再生にだけ使い、本番では使わない。
    /// </summary>
    public bool AssumeEpochStart { get; init; }
}
