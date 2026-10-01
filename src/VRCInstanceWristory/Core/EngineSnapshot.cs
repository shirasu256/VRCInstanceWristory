using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core;

/// <summary>表示側へ渡す確定状態。</summary>
public sealed class EngineSnapshot
{
    public required bool ClientRunning { get; init; }

    public required PresenceState Presence { get; init; }

    public required LogHealth Health { get; init; }

    public required IReadOnlyList<VisitRecord> History { get; init; }

    /// <summary>
    /// インスタンスに付けた目印（2026-09-22のユーザー指定→実装メモ5.32）。
    /// 鍵は <see cref="VisitRecord.LocationKey"/> で、同じインスタンスの行はすべて同じ印になる。
    /// </summary>
    public IReadOnlyDictionary<string, InstanceMark> Marks { get; init; } = new Dictionary<string, InstanceMark>();

    public string? CurrentEventId { get; init; }

    /// <summary>パネルを出すきっかけになるメインメニューのページを開いているか。表示条件。</summary>
    public required bool MenuPageOpen { get; init; }

    /// <summary>
    /// ロード画面を抜け、次に手首の角度で隠れたらパネルを閉じる状態か（2026-09-21のユーザー指定）。
    /// 表示そのものには使わない。VR側が角度で隠したときに <see cref="HistoryEngine.CloseByViewAngle"/>
    /// を呼ぶかどうかの判断に使う。
    /// </summary>
    public required bool AwaitingViewAngleClose { get; init; }

    /// <summary>
    /// ロード画面（退出から「世界が見えた」まで）の中か（検証用）。
    /// この間は <see cref="MenuPageOpen"/> を true にしているので、表示の判断には使わない（→5.31節）。
    /// </summary>
    public bool InLoadingScreen { get; init; }

    public required bool BaselineKnown { get; init; }

    /// <summary>チェックポイントの保存に失敗していないか。</summary>
    public required bool CheckpointHealthy { get; init; }

    /// <summary>状態の段に出すための、見つけた問題（→実装メモ5.85）。</summary>
    public EngineAlerts Alerts { get; init; } = EngineAlerts.None;

    public string? Diagnostic { get; init; }

    /// <summary>状態の世代番号。古い描画結果で再表示しないために使う。</summary>
    public required long Generation { get; init; }

    /// <summary>
    /// 履歴をまとめて消す予定の時刻（UTC）。対象インスタンスに滞在中は null（期限なし）。
    /// 見出しの残り時間（<see cref="RetentionRemaining"/>）・「カウントダウン停止中」（<see cref="CountdownStopped"/>）と、
    /// 主ループがリセットの予告を出すかの判断に使う。
    /// </summary>
    public DateTime? RetentionDeadlineUtc { get; init; }

    /// <summary>いまの保持時間（設定 <c>retentionMinutes</c>・既定60分→実装メモ5.39）。</summary>
    public TimeSpan Retention { get; init; } = HistoryStore.DefaultRetention;

    /// <summary>履歴の自動リセットを使っているか（→実装メモ5.71）。</summary>
    public bool AutoReset { get; init; } = true;

    /// <summary>
    /// 自動リセットを使っていて、いまは数えていない（対象インスタンスに滞在している・まだ一度も離れていない）か（→実装メモ5.73）。
    /// 見出しを「カウントダウン停止中:」にし、「延長」「リセット」を押せなくする。
    /// </summary>
    public bool CountdownStopped => AutoReset && (RetentionDeadlineUtc is null || RetentionPausedRemaining is not null);

    /// <summary>
    /// AFK の間カウントダウンを止めているとき、止めた時点の残り時間（→実装メモ5.89）。止めていなければ null。
    /// 止めている間は期限（<see cref="RetentionDeadlineUtc"/>）が時間とともに後ろへずれるので、残り時間はこちらを使う。
    /// </summary>
    public TimeSpan? RetentionPausedRemaining { get; init; }

    /// <summary>
    /// 履歴をまとめて消すまでの残り時間。対象インスタンスに滞在している間は上限（保持時間）で止める。
    /// 見出しのカウントダウンに使う。
    /// </summary>
    public TimeSpan RetentionRemaining(DateTime nowUtc)
    {
        if (RetentionDeadlineUtc is not { } deadline)
            return Retention;

        if (RetentionPausedRemaining is { } paused)
            return paused < TimeSpan.Zero ? TimeSpan.Zero : paused > Retention ? Retention : paused;

        var remaining = deadline - nowUtc;

        if (remaining < TimeSpan.Zero)
            return TimeSpan.Zero;

        return remaining > Retention ? Retention : remaining;
    }

    /// <summary>
    /// ログ・プロセス側の表示条件。
    /// 2026-09-13のユーザー指定で、「対象インスタンスに滞在中」から
    /// 「メインメニューの対象ページを開いている間」へ変更した（仕様6.3節からの変更点）。
    /// 記録する訪問は従来どおり対象3種別だけ。
    /// </summary>
    public bool ContentReady =>
        ClientRunning && Health == LogHealth.Ok && MenuPageOpen && History.Count > 0;
}
