using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// エンジンの状態をパネルへ反映する。スクロール位置の保持・復帰の規則（仕様8.2節）もここで扱う。
/// </summary>
public sealed class PanelPresenter(IPanelTarget target, LogTimeConverter time, IDiagnostics log, IClock? clock = null)
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    private long _lastGeneration = -1;
    private long _lastMinute = -1;
    private bool _needTailReset = true;
    private bool _detailsDirty = true;
    private readonly ResetWarningScheduler _resetWarning = new();

    /// <summary>予告のアイコンを出すか（設定「リセット予告アイコンを表示する」→実装メモ5.89）。オフなら受け手へ渡さない。</summary>
    public bool ResetWarningEnabled { get; set; } = true;

    /// <summary>予告の表示タイミング（リセットの何分前か→実装メモ5.89）。</summary>
    public TimeSpan ResetWarningLead { get; set; } = ResetWarningTrigger.DefaultLead;

    /// <summary>AFK の間に見逃した予告を、戻ってきたときに出し直すか（→実装メモ5.89）。</summary>
    public bool ResetWarningReshow { get; set; } = true;

    /// <summary>点滅1回ぶんの長さ（点滅の途中で AFK になったかを見る→実装メモ5.89）。</summary>
    public TimeSpan ResetWarningBlinkDuration { get; set; } = ResetWarningBlink.DurationFor(ResetWarningBlink.DefaultCount);

    /// <summary>利用者がいまそこにいないか（VRChat の AFK・SteamVR のダッシュボードを開いている→実装メモ5.89）。</summary>
    public bool Away { get; set; }

    /// <summary>利用者が付けたグループ名とその出し方（→実装メモ5.48）。変えたら <see cref="Refresh"/> を呼ぶ。</summary>
    public GroupNaming Groups { get; set; } = GroupNaming.None;
    private LogHealth _lastHealth = LogHealth.Initializing;
    private bool _lastCheckpointHealthy = true;

    /// <summary>行がなくてもパネルを出すか（設定「該当履歴が無い場合も表示する」→実装メモ5.128）。</summary>
    public bool ShowWhenEmpty { get; set; }

    public bool ContentReady { get; private set; }

    /// <summary>
    /// 次の <see cref="Apply"/> で行を作り直して渡し、末尾から見せる。
    /// 受け手が途中から加わったとき（SteamVRへつなぎ直したとき→実装メモ5.39）に使う。
    /// </summary>
    public void Resend()
    {
        _lastGeneration = -1;
        _needTailReset = true;
    }

    /// <summary>次の <see cref="Apply"/> で行を作り直す（グループ名の変更など、エンジンの外で行の見た目が変わったとき）。スクロールは動かさない。</summary>
    public void Refresh() => _lastGeneration = -1;

    public void Apply(EngineSnapshot snapshot)
    {
        ContentReady = ShowWhenEmpty ? snapshot.MenuReady : snapshot.ContentReady;

        var now = _clock.UtcNow;

        // 残り時間は毎フレーム変わり得るが、実際に描き直すのは値（MM:SS）が変わったフレームだけ。
        target.SetCountdown(snapshot.RetentionRemaining(now), snapshot.Retention);
        target.SetCountdownStopped(snapshot.CountdownStopped);

        // 残りが表示タイミング（既定3分）を切った瞬間に、VRChatのマイクのアイコンの横で知らせる（→実装メモ5.87・5.89）。
        // 消える行がないとき・VRChatが動いていないときは、知らせても意味がないので出さない。
        // AFK の間にカウントダウンを止めているときも期限はあるので、数えているものとして渡す（止めている間は残りが減らないので、またがない）。
        var countingDown = snapshot.AutoReset && snapshot.RetentionDeadlineUtc is not null;

        // 滞在中も数えているとき（→実装メモ5.90）、いまの滞在の行はリセットでも消えないので、それ以外の行があるときだけ知らせる。
        var relevant = snapshot.ClientRunning && snapshot.History.Any(r => !string.Equals(r.EventId, snapshot.CurrentEventId, StringComparison.Ordinal));

        if (ResetWarningEnabled
            && _resetWarning.Observe(now, countingDown, snapshot.RetentionRemaining(now), relevant,
                ResetWarningLead, Away, ResetWarningReshow, ResetWarningBlinkDuration))
        {
            target.ShowResetWarning();
        }

        // パネルを閉じている間・VRChatが終了している間に印を付けておき、
        // 次に開いたときは最新（末尾）から見せる。一時的な障害では位置を保持する。
        if (!snapshot.MenuPageOpen || !snapshot.ClientRunning)
            _needTailReset = true;

        // 行の中で時間とともに変わるのは、退出してからの「N分前」（→5.36）と滞在中の行の棒（→5.28）で、
        // どちらも分単位でしか動かない。時計の分が変わったときだけ作り直せば、
        // 行の下絵を描き直すのは1分に1回までで済む（内容が同じなら表示側が描き直さない）。
        var minute = RowFormatter.MinuteIndex(now);

        if (snapshot.Generation != _lastGeneration || minute != _lastMinute)
        {
            // 詳しい情報（一緒にいた人・写真など）は分では変わらないので、世代が変わったときだけ作り直す。
            _detailsDirty |= snapshot.Generation != _lastGeneration;
            _lastGeneration = snapshot.Generation;
            _lastMinute = minute;
            var rows = RowFormatter.Build(snapshot.History, snapshot.CurrentEventId, time, now, snapshot.Marks, Groups);
            target.SetContent(rows);

            if (_detailsDirty && target.WantsDetails)
            {
                target.SetDetails(RowDetails.Build(snapshot.History, snapshot.CurrentEventId, Groups));
                _detailsDirty = false;
            }
        }

        if (ContentReady && _needTailReset)
        {
            target.ResetScrollToTail();
            _needTailReset = false;
        }

        if (snapshot.Health != _lastHealth)
        {
            _lastHealth = snapshot.Health;
            log.Info($"ログ状態: {snapshot.Health}{(snapshot.Diagnostic is null ? string.Empty : " / " + snapshot.Diagnostic)}");
        }

        if (snapshot.CheckpointHealthy != _lastCheckpointHealthy)
        {
            _lastCheckpointHealthy = snapshot.CheckpointHealthy;
            if (!snapshot.CheckpointHealthy)
                log.Error("回数のチェックポイントを保存できていません。再起動時の復元に影響します。");
        }
    }
}
