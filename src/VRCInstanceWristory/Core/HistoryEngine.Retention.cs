using VRCInstanceWristory.Core.Counting;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core;

// 履歴の保持期限（退出・延長からの数え始め、自動リセット、AFK の停止、手でのリセットと取り消し）。
public sealed partial class HistoryEngine
{
    /// <summary>これより前の行は消去済み。一度消した行を戻さないため、後戻りさせない。</summary>
    private DateTime _historyCutoffUtc = DateTime.MinValue;

    /// <summary>
    /// 行の上限（<see cref="HistoryStore.MaxRecords"/>・<see cref="HistoryStore.MaxAge"/>→実装メモ5.108）で消した区切り。後戻りさせない。
    /// リセットではないので <see cref="_historyCutoffUtc"/> とは分ける（付けた目印を区切りで消さない・「直前のリセットを戻す」でも戻さない）。
    /// </summary>
    private DateTime _limitCutoffUtc = DateTime.MinValue;

    /// <summary>行を出す区切り。リセットの区切りと上限の区切りの新しいほう。</summary>
    private DateTime EffectiveCutoffUtc => _historyCutoffUtc > _limitCutoffUtc ? _historyCutoffUtc : _limitCutoffUtc;

    /// <summary>次に履歴をまとめて消す時刻。null は対象に滞在中で期限なし。</summary>
    private DateTime? _retentionDeadlineUtc;

    /// <summary>
    /// 「延長」を押した時刻（古い順）。押した時点をもう一度の退出として扱い、
    /// 退出時刻と同じ規則で期限を決める（→<see cref="CollectRetentionStarts"/>）。
    ///
    /// 以前は最後の1回だけを覚えていた。そのため元の期限（退出+60分）を過ぎてから押し直すと、
    /// 前の押下が取り消していたその期限が成立してしまい、押した瞬間に全行が消えていた
    /// （2026-09-25のユーザー報告→実装メモ5.36）。押した時刻はすべて残す。
    /// </summary>
    private readonly List<DateTime> _retentionResets = [];

    /// <summary>
    /// 直前のリセット（手・外部のコマンド・自動）の前の状態（→<see cref="UndoClearHistory"/>・実装メモ5.86）。
    /// 戻せるのは1回だけで、戻すか次のリセットで入れ替わる。保存はしない（アプリを終了すると戻せなくなる）。
    /// 行も控える。ログが消えた訪問（保存から戻した行など→実装メモ5.108）は、ログを読み直しても戻らないため。
    /// 自動リセットで回数を数え直したときは、その前の期間と数え直しの初めの回数（<see cref="Seeded"/>）も控える（→実装メモ5.110）。
    /// </summary>
    private sealed record ClearUndo(DateTime Cutoff, List<StoredEvent> Events, Dictionary<string, MarkEntry> Marks, List<VisitRecord> Records, CounterState Counter)
    {
        public Dictionary<string, int>? Seeded { get; init; }
    }

    private ClearUndo? _clearUndo;

    /// <summary>いまの保持時間（→<see cref="EngineOptions.Retention"/>）。</summary>
    private TimeSpan _retention;

    // 履歴の自動リセットを使うか（→実装メモ5.71）。
    private bool _autoReset;

    // 自動リセットを最後にオンへ戻した時刻。これより前の退出・延長からは期限を数えない（保存する→実装メモ5.71）。
    private DateTime _autoResetFromUtc = DateTime.MinValue;

    // 対象インスタンスに滞在している間はカウントダウンを止めるか（→実装メモ5.90）。
    private bool _stopInTarget;

    // 滞在中も数えるようにした時刻。これより前の入室は数え始めにしない（切り替えた途端に、過去の長い滞在でまとめて消えないように）。
    // 保存しない。起動したときから滞在中も数える設定なら、過去の入室もすべて数える。
    private DateTime _targetCountFromUtc = DateTime.MinValue;

    // AFK の間カウントダウンを止め始めた時刻（→実装メモ5.89）。止めていなければ null。保存しない（再起動すると止めていた時間は数えない）。
    private DateTime? _afkPausedSinceUtc;

    // 止めている間の残り時間（止めた時点の残り）。止めていないか、数えていなければ null。
    private TimeSpan? _pausedRemaining;

    /// <summary>いまの保持時間。</summary>
    public TimeSpan Retention => _retention;

    /// <summary>履歴の自動リセットを使っているか（→実装メモ5.71）。</summary>
    public bool AutoReset => _autoReset;

    /// <summary>
    /// 履歴の自動リセットを切り替える（2026-09-27のユーザー指定→実装メモ5.71）。
    /// オフの間は期限を数えないので、行は手で「リセット」するまで残る。オンに戻すと、その時点から期限を数え直す。
    /// オフの間に過ぎた期限でまとめて消えないよう、戻した時刻より前の退出・延長は数え始めにしない（<see cref="_autoResetFromUtc"/>）。
    /// 対象を離れているときに戻したなら、戻した時点をもう一度の退出として扱う（「延長」と同じ）。
    /// 戻した時刻は保存し、アプリを再起動しても、オフの間にたまった行がまとめて消えないようにする。
    /// </summary>
    public void SetAutoReset(bool enabled)
    {
        if (enabled == _autoReset)
            return;

        _autoReset = enabled;

        if (enabled)
        {
            var now = _clock.UtcNow;
            _autoResetFromUtc = now;

            if (!StoppedInTarget())
                _retentionResets.Add(now);

            _checkpointDirty = true;
            SaveCheckpointIfNeeded(force: true);
        }

        ApplyRetention(_clock.UtcNow);
        _log.Info(enabled ? "履歴の自動リセットを有効にしました。" : "履歴の自動リセットを無効にしました。");
        MarkDirty();
    }

    /// <summary>対象インスタンスに滞在している間はカウントダウンを止めるか（→実装メモ5.90）。</summary>
    public bool StopCountdownInTarget => _stopInTarget;

    /// <summary>
    /// 対象インスタンスに滞在している間もカウントダウンを数えるかを切り替える（2026-09-29のユーザー指定→実装メモ5.90）。
    ///
    /// 止めない（false）ときは、対象インスタンスへ入った時刻も数え始めに加える（退出・延長と同じ扱い）。滞在が保持時間を超えると、
    /// 手で「リセット」を押したときと同じく、いまの滞在の行だけを残して前の行を消し、その時点から数え直す。
    /// 止めない側へ切り替えたときは、切り替えた時点を数え始めにし、それより前の入室は数えない（過去の長い滞在で、切り替えた途端に消えないように）。
    /// </summary>
    public void SetStopCountdownInTarget(bool stop)
    {
        if (stop == _stopInTarget)
            return;

        _stopInTarget = stop;
        var now = _clock.UtcNow;

        if (!stop)
        {
            _targetCountFromUtc = now;

            if (_autoReset && InTargetNow())
            {
                _retentionResets.Add(now);
                _checkpointDirty = true;
            }
        }

        ApplyRetention(now);
        SaveCheckpointIfNeeded(force: false);
        _log.Info(stop ? "対象インスタンスに滞在している間はカウントダウンを止めます。" : "対象インスタンスに滞在している間もカウントダウンを数えます。");
        MarkDirty();
    }

    /// <summary>いま対象インスタンスに滞在していて、その間はカウントダウンを止める設定か。</summary>
    private bool StoppedInTarget() => _stopInTarget && InTargetNow();

    /// <summary>AFK の間カウントダウンを止めているか（→<see cref="SetAfkPaused"/>）。</summary>
    public bool AfkPaused => _afkPausedSinceUtc is not null;

    /// <summary>
    /// AFK の間カウントダウンを止める・再び数える（2026-09-29のユーザー指定→実装メモ5.89）。
    /// 主ループが「AFK中はカウントダウンを停止する」がオンで、VRChat が AFK を知らせている間 true を渡す。
    ///
    /// 止めている間は、いちばん新しい数え始め（退出・延長）を止めた時間だけ後ろへずらして数える。期限は来ず、残り時間は止めた時点のまま。
    /// 再び数えるとき、ずらした数え始めを「延長」と同じ記録（<see cref="_retentionResets"/>）として残す。
    /// こうすると、止める前の期限は取り消され（延長と同じ規則→<see cref="ComputeCutoff"/>）、アプリを再起動しても止めていた分は失われない。
    /// 止めている途中に再起動したときは、止めていた時間を数えない（止め始めを保存しない）。
    /// </summary>
    public void SetAfkPaused(bool paused)
    {
        if (paused == AfkPaused)
            return;

        var now = _clock.UtcNow;

        if (paused)
        {
            _afkPausedSinceUtc = now;
            ApplyRetention(now);
            _log.Info("AFK になったので、履歴リセットまでのカウントダウンを止めました。");
            return;
        }

        var starts = CollectRetentionStarts();
        var shifted = PausedStart(starts, now);
        _afkPausedSinceUtc = null;

        if (shifted is { } start)
        {
            // 止める前の数え始めから、ずらした数え始めまでを、保持時間より短い間隔（半分）で刻んで残す。
            // AFK が保持時間より長いと、ずらした数え始めだけでは止める前の期限を取り消せず、戻った瞬間に消えてしまう。
            ChainRetentionResets(starts[^1], start, TimeSpan.FromTicks(_retention.Ticks / 2));
            _retentionResets.Add(start);
            _retentionResets.Sort();
            _checkpointDirty = true;
        }

        ApplyRetention(now);
        SaveCheckpointIfNeeded(force: shifted is not null);

        _log.Info(_retentionDeadlineUtc is { } deadline
            ? $"AFK から戻ったので、カウントダウンを再開しました。{LocalClock(deadline)} に消去します。"
            : "AFK から戻ったので、カウントダウンを再開しました。");
    }

    /// <summary>
    /// 止めている間の数え始め。いちばん新しい数え始め L を、止めていた時間（L と止め始めの遅いほうから今まで）だけ後ろへずらしたもの。
    /// 止めていない・数えていない（対象に滞在中・自動リセットを使っていない）なら null。
    /// </summary>
    private DateTime? PausedStart(List<DateTime> starts, DateTime nowUtc)
    {
        if (_afkPausedSinceUtc is not { } since || !_autoReset || StoppedInTarget() || starts.Count == 0)
            return null;

        var last = starts[^1];
        var from = since > last ? since : last;
        return nowUtc > from ? last + (nowUtc - from) : last;
    }

    /// <summary>
    /// VR側の操作（見出しの「延長」ボタン）で、消去までのカウントを数え直す
    /// （2026-09-19のユーザー指定。ボタンの名前は「カウントリセット」「カウント延長」を経て2026-09-27に「延長」へ改めた）。
    /// 押した時点をもう一度の退出とみなし、そこから60分を数える。
    ///
    /// 退出時刻そのものは書き換えない。ログから読んだ事実を残したまま、期限だけを押した時点へ延ばす。
    /// 既に消えた行は戻らない（区切りは後戻りさせない）。
    ///
    /// 対象に滞在している間は期限がない（表示も60:00で固定）ので、押しても何も変えない（→5.17）。
    /// ここで押した時刻を残すと、滞在が60分を超えたところで「押した時点+60分」の期限が
    /// 成立して、滞在中の行まで消えてしまう。
    /// </summary>
    /// <returns>期限を数え直したか。表示側はこのときだけ残り時間の数字を光らせる（→実装メモ5.130）。</returns>
    public bool ResetRetention()
    {
        // 自動リセットを使っていない間は、延ばす期限がない（→実装メモ5.71）。
        if (!_autoReset)
            return false;

        if (StoppedInTarget())
        {
            _log.Info("消去までのカウントを延長しました（対象インスタンスに滞在中のため期限なし）。");
            return false;
        }

        var now = _clock.UtcNow;
        _retentionResets.Add(now);

        // 変わるのは期限（＝見出しの残り時間）だけで、行の内容は変わらない。
        // ここで世代番号を進めると同じ絵を描き直すことになる（見た目は変わらないのに無駄が出る）
        // （2026-09-19の実機確認）。行が実際に減ったときは ApplyRetention 側が進める。
        ApplyRetention(now);

        // 押した時刻はすぐに保存する。アプリを再起動しても延長が失われないようにする（2026-09-26のユーザー指定→実装メモ5.45）。
        _checkpointDirty = true;
        SaveCheckpointIfNeeded(force: true);

        _log.Info(_retentionDeadlineUtc is { } deadline
            ? $"消去までのカウントを延長しました。{LocalClock(deadline)} に消去します。"
            : "消去までのカウントを延長しました。");

        return true;
    }

    /// <summary>
    /// 見出しの「リセット」（確認を挟む）で、訪問履歴を今すぐまとめて消す（2026-09-27のユーザー指定→実装メモ5.65）。
    ///
    /// 保持期限が来たときと同じく、消去の区切りを進めて、それより前の行・目印・回数のイベント記録を捨てる。
    /// 区切りは保存し、アプリを再起動しても戻らない（→5.45）。延長を押した時刻もここで捨てる。
    /// 対象インスタンスに滞在している間は、いまの滞在の行だけを残す（いる場所の行まで消すと、
    /// 退出したときに行のないまま退出だけが届くことになる）。
    /// </summary>
    public void ClearHistory()
    {
        var now = _clock.UtcNow;
        var records = _history.Records;
        var cutoff = InTargetNow() && records.Count > 0 ? records[^1].VisitedAtUtc : now;

        if (cutoff <= _historyCutoffUtc)
        {
            _log.Info("訪問履歴のリセット: 消す行はありません。");
            return;
        }

        var undo = CaptureUndo();
        _historyCutoffUtc = cutoff;
        _retentionResets.Clear();
        _checkpointDirty = true;

        var removed = _history.PruneBefore(_historyCutoffUtc);
        KeepUndo(undo, removed);

        if (_marks.RemoveStale(_historyCutoffUtc, _history.LocationKeys()))
            SaveMarks();

        _counter.PruneEventsBefore(_historyCutoffUtc);
        ApplyRetention(now);
        MarkDirty();

        SaveCheckpointIfNeeded(force: true);
        SaveHistoryIfNeeded(force: true);
        _log.Warn($"訪問履歴をリセットしました（{removed}件を消去）。");
    }

    /// <summary>「直前のリセットを戻す」を押せるか（→<see cref="UndoClearHistory"/>）。</summary>
    public bool CanUndoClearHistory => _clearUndo is not null;

    /// <summary>
    /// 直前のリセットを戻す（2026-09-28のユーザー指定→実装メモ5.86）。戻すのは、見出しの「リセット」・外部からのコマンド・
    /// 自動リセット（保持期限）のうち、いちばん最近のもの1回だけ。
    ///
    /// 消去の区切りをリセットの前へ戻し、そのとき捨てた目印と回数のイベント記録を戻してから、ログを先頭から読み直して行を作り直す
    /// （記録する種類を変えたときと同じ経路→<see cref="SetTargetTypes"/>）。リセットのあとに付けた目印はそのまま残す。
    /// 区切りはふだん後戻りさせないが、これは利用者が戻すと決めたときだけの例外。
    ///
    /// 戻した行がすぐに期限でまた消えないよう、戻した時点から数え直す（自動リセットをオンへ戻したときと同じ扱い→<see cref="SetAutoReset"/>）。
    /// 対象インスタンスに滞在していなければ、戻した時点をもう一度の退出として扱う（「延長」と同じ）。
    /// </summary>
    public void UndoClearHistory()
    {
        if (_clearUndo is not { } undo)
            return;

        _clearUndo = null;

        var now = _clock.UtcNow;
        _historyCutoffUtc = undo.Cutoff;
        _autoResetFromUtc = now;

        if (!StoppedInTarget())
            _retentionResets.Add(now);

        _counter.RestoreEvents(undo.Events);

        // 自動リセットを戻すときは、回数もその前の期間へ戻す（数え直してから加えた回数は足し戻す→実装メモ5.110）。
        if (undo.Seeded is { } seeded)
            _counter.RestoreState(undo.Counter, seeded);

        var marksChanged = false;

        foreach (var (key, entry) in undo.Marks)
        {
            if (_marks.Get(key) == InstanceMark.None)
                marksChanged |= _marks.Set(key, entry.Mark, entry.MarkedAtUtc);
        }

        if (marksChanged)
            SaveMarks();

        foreach (var source in _sources.Values)
            CloseReader(source);

        _sources.Clear();
        _history.Clear();

        // ログを読めた訪問は、読み直した行へ置き換わる。
        var cutoff = EffectiveCutoffUtc;
        _history.Restore(undo.Records.Where(r => r.VisitedAtUtc >= cutoff && _targets.Contains(r.AccessType)));

        ScanDirectory(initial: true);
        _checkpointDirty = true;
        SaveCheckpointIfNeeded(force: true);
        SaveHistoryIfNeeded(force: true);

        _log.Warn($"直前のリセットを戻しました（{_history.Records.Count}件を表示）。");
        MarkDirty();
    }

    /// <summary>区切りを進める前の状態を控える。</summary>
    private ClearUndo CaptureUndo()
        => new(_historyCutoffUtc, [.. _counter.Events.Values], _marks.Entries(), [.. _history.Records], _counter.Capture());

    /// <summary>行が実際に消えたリセットだけを戻せるようにする（消える行のない区切りの進みで、戻せるリセットを失わない）。</summary>
    private void KeepUndo(ClearUndo undo, int removed)
    {
        if (removed > 0)
            _clearUndo = undo;
    }

    /// <summary>
    /// 保持時間を変える（2026-09-26のユーザー指定でデスクトップのウィンドウから変えられるようにした→実装メモ5.39）。
    ///
    /// 期限はいつも「最後の数え始め（退出か延長）+ 保持時間」なので、変えた時点で数え直すのではなく、
    /// 既に数えている残り時間がそのまま伸び縮みする。短くして期限を過ぎた場合は、その場で消える。
    /// 一度消した行は、長くしても戻らない（区切りは後戻りさせない）。
    /// </summary>
    public void SetRetention(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero || retention == _retention)
            return;

        _retention = retention;
        ApplyRetention(_clock.UtcNow);

        _log.Info($"履歴を消すまでの時間を{retention.TotalMinutes:0}分にしました。");

        // 見出しの残り時間が変わる（行が消えたときは ApplyRetention が既に進めている）。
        MarkDirty();
    }

    // ------------------------------------------------------------------ 保持期限

    /// <summary>
    /// 履歴の保持期限を適用する（2026-09-18のユーザー指定）。
    ///
    /// 対象種別のインスタンスに滞在している間は期限を設けず、離れた時点から60分を数える。
    /// 60分に達した時点で、それまでの行をまとめて捨てる。60分の途中で対象へ戻れば数え直す。
    /// 滞在が続く限り、どれだけ古い行も残す。
    ///
    /// VR側で「延長」を押した場合は、押した時点から60分を数え直す（→<see cref="ResetRetention"/>）。
    ///
    /// ここでいう60分は既定値で、実際には設定 <c>retentionMinutes</c> の時間（<see cref="_retention"/>）を使う（→実装メモ5.39）。
    /// </summary>
    private void ApplyRetention(DateTime nowUtc)
    {
        var starts = CollectRetentionStarts();

        // AFK の間カウントダウンを止めている（→実装メモ5.89・SetAfkPaused）。
        // 止めている間は、期限の判定を止めた時点（いちばん新しい数え始めがそれより後ならその時点）で固定する。
        // 止める前にまだ来ていなかった期限は、止めている間は来ない。
        _pausedRemaining = null;
        var pausedStart = PausedStart(starts, nowUtc);
        var judgeUtc = nowUtc;

        if (pausedStart is not null)
        {
            var last = starts[^1];
            judgeUtc = _afkPausedSinceUtc!.Value > last ? _afkPausedSinceUtc.Value : last;
            _pausedRemaining = last + _retention - judgeUtc;
        }

        var cutoff = AutoResetCutoff(starts, judgeUtc);

        // 一度消した行は戻さない。ソースの差し替えで退出の記録が消えても後戻りさせない。
        // 区切りは保存もして、アプリを再起動しても戻さない（→実装メモ5.45）。
        var reset = cutoff > _historyCutoffUtc;
        var undo = reset ? CaptureUndo() : null;

        if (reset)
        {
            _historyCutoffUtc = cutoff;
            _checkpointDirty = true;

            // 自動リセットが入ったので、回数をここから数え直す（2026-10-01のユーザー指定→実装メモ5.110）。
            // 起動時の読み直しで、回数を付ける前に数え直していれば（→RestoreFromLogs）、ここではもう数え直さない。
            if (!CountingStartedAt(cutoff))
                undo = undo! with { Seeded = StartCountingOver(cutoff) };
        }

        // 止めている間の期限は、ずらした数え始めから数えたもの（時間とともに後ろへずれる。残り時間は _pausedRemaining）。
        _retentionDeadlineUtc = pausedStart is { } shiftedStart ? shiftedStart + _retention : ComputeDeadline(starts);

        // 滞在中も数えていて期限が来た（上で滞在の前の行を消した）ら、その時点から数え直す（「延長」と同じ記録として残す→実装メモ5.90）。
        // 記録は期限の1ティック前に置く。延長の規則では次の数え始めが期限より前にあるときだけ前の期限を取り消すので、
        // ちょうど期限に置くと、退出したあとで前の期限が成り立ち、滞在していた行まで消えてしまう。
        // スリープ明けなどで何周期ぶんも過ぎていれば、1周期ずつ今より先になるまで進める（間を空けると同じ理由で取り消せない）。
        if (!_stopInTarget && InTargetNow() && pausedStart is null && _retentionDeadlineUtc is { } due && due <= nowUtc)
        {
            var last = ChainRetentionResets(starts[^1], nowUtc, _retention - TimeSpan.FromTicks(1));
            _retentionDeadlineUtc = last + _retention;
        }

        var removed = _history.PruneBefore(_historyCutoffUtc);

        if (undo is not null)
            KeepUndo(undo, removed);

        if (removed > 0)
        {
            _log.Info($"対象インスタンスを離れてから{_retention.TotalMinutes:0}分が過ぎたため、履歴{removed}件をまとめて消去しました。");
            MarkDirty();
        }

        // ログのリセットで目印も消す。付けてからリセットまでの間だけ残る（2026-09-27のユーザー指定→実装メモ5.54）。
        // 記録する種類を切り替えて行が入れ替わっただけ（区切りは進まない）なら消さない。
        if (reset && _marks.RemoveStale(_historyCutoffUtc, _history.LocationKeys()))
        {
            _log.Info("ログのリセットに合わせて、目印を外しました。");
            SaveMarks();
            MarkDirty();
        }

        ApplyHistoryLimit(nowUtc);
        PruneRetentionResets();

        // 表示が続く行の回数を失わないよう、履歴と同じ区切りで捨てる。
        _counter.PruneEventsBefore(EffectiveCutoffUtc);
    }

    /// <summary>
    /// 行の上限を当てる（2026-10-01のユーザー指定→実装メモ5.108）。自動リセットの有無によらず、
    /// 新しいほうから <see cref="HistoryStore.MaxRecords"/>（500件）を超えた行と、入室から <see cref="HistoryStore.MaxAge"/>（30日）を過ぎた行を消す。
    ///
    /// リセットとは扱いを分ける。「直前のリセットを戻す」の対象にせず（数件ずつ消えるたびに、戻せるリセットを失わない）、
    /// 予告のアイコンも出さない。目印は、消えた行のインスタンスのうち、残った行にないものだけを外す
    /// （リセットの区切りで外すと、残っている古いインスタンスの目印まで消えてしまう）。
    /// </summary>
    private void ApplyHistoryLimit(DateTime nowUtc)
    {
        var records = _history.Records;

        if (records.Count == 0)
            return;

        var limit = nowUtc - HistoryStore.MaxAge;

        if (records.Count > HistoryStore.MaxRecords && records[records.Count - HistoryStore.MaxRecords].VisitedAtUtc > limit)
            limit = records[records.Count - HistoryStore.MaxRecords].VisitedAtUtc;

        // いまの滞在の行は残す（リセットと同じ→ClearHistory。行のないまま退出だけが届くことにならないように）。
        if (InTargetNow() && limit > records[^1].VisitedAtUtc)
            limit = records[^1].VisitedAtUtc;

        if (limit <= _limitCutoffUtc)
            return;

        _limitCutoffUtc = limit;
        _checkpointDirty = true;

        var removed = _history.PruneBefore(EffectiveCutoffUtc);

        if (removed == 0)
            return;

        _log.Info($"訪問履歴の上限（{HistoryStore.MaxRecords}件・{HistoryStore.MaxAge.TotalDays:0}日）を超えたため、古い履歴{removed}件を消去しました。");
        MarkDirty();

        if (_marks.RemoveAbsent(_history.LocationKeys()))
        {
            SaveMarks();
            _log.Info("消えた履歴のインスタンスの目印を外しました。");
        }
    }

    /// <summary>
    /// 自動リセットの区切り（期限が来ていればその時刻。来ていなければ前の区切り以下の値）。自動リセットを使っていない間は期限を数えない
    /// （手で消したときの区切りだけが効く→実装メモ5.71）。<see cref="ApplyRetention"/> と、起動時に回数を付ける前の判定（→RestoreFromLogs）で同じものを使う。
    /// </summary>
    private DateTime AutoResetCutoff(List<DateTime> starts, DateTime judgeUtc)
    {
        if (!_autoReset)
            return DateTime.MinValue;

        var cutoff = ComputeCutoff(starts, judgeUtc);

        // 滞在中も数えているとき（→実装メモ5.90）は、いまの滞在の行を残す（手で「リセット」を押したときと同じ→ClearHistory）。
        // 行のないまま退出だけが届くことにならないように。
        var records = _history.Records;

        if (!_stopInTarget && InTargetNow() && records.Count > 0 && cutoff > records[^1].VisitedAtUtc)
            cutoff = records[^1].VisitedAtUtc;

        return cutoff;
    }

    /// <summary>
    /// 自動リセットが入ったので、回数を区切りから数え直す（2026-10-01のユーザー指定→実装メモ5.110）。
    /// VRChat を起動し直しても、次の自動リセットまでこの期間を引き継ぐ（→ResolveEpoch）。数え直しの初めの回数を返す。
    /// </summary>
    private Dictionary<string, int> StartCountingOver(DateTime cutoff)
    {
        var session = _activeSourceId ?? _registry.OrderedSessions().LastOrDefault()?.SourceSessionId ?? string.Empty;
        _checkpointDirty = true;
        _log.Info("履歴の自動リセットに合わせて、回数を数え直します。");
        return _counter.StartOver(session, cutoff);
    }

    /// <summary>その区切りの自動リセットで、もう数え直しているか。</summary>
    private bool CountingStartedAt(DateTime cutoff)
        => _counter.Anchor is { } anchor
           && anchor.StartUtc == cutoff
           && string.Equals(anchor.Reason, EpochReason.SinceAutoReset.Code(), StringComparison.Ordinal);

    /// <summary>刻んで足す「延長」の記録の上限。時計が大きく飛んだときに、記録が際限なく増えないようにする歯止め。</summary>
    private const int MaxChainedRetentionResets = 100_000;

    /// <summary>
    /// いちばん新しい数え始め <paramref name="from"/> から <paramref name="until"/> まで、途中の期限が成り立たないよう、
    /// <paramref name="step"/>（保持時間より短い）ごとに「延長」と同じ記録を刻む（→実装メモ5.89・5.90）。
    ///
    /// 延長の規則では、次の数え始めが期限より前にあるときだけ前の期限を取り消す（→<see cref="ComputeCutoff"/>）。
    /// 間を保持時間以上空けると取り消せないので、<paramref name="until"/> まで保持時間を残さずに刻む。
    /// 刻んだ最後の時刻を返す（刻まなければ <paramref name="from"/>）。
    /// </summary>
    private DateTime ChainRetentionResets(DateTime from, DateTime until, TimeSpan step)
    {
        var point = from;

        for (var i = 0; i < MaxChainedRetentionResets && until - point >= _retention; i++)
        {
            point += step;
            _retentionResets.Add(point);
            _checkpointDirty = true;
        }

        return point;
    }

    /// <summary>
    /// もう効き目のない「延長」の時刻を捨てる（→実装メモ5.45）。延長を押すたびに保存が伸び続けないようにする。
    ///
    /// 延長 R が取り消すのは、R より前の数え始め S の期限（S + 保持時間 &lt; R + 保持時間）である。
    /// R + 保持時間が消去の区切りより前なら、その期限が成り立っても消える行はもう残っていない
    /// （区切りより前の行は既に消えていて、区切りは後戻りしない）。
    /// 保持時間はあとから延ばせるので、設定で選べる最大（<see cref="HistoryStore.MaxRetentionMinutes"/>・900分）で見積もる。
    /// 残っている行や記録する種類によらずに決まるので、種類を切り替えて行が入れ替わっても安全に捨てられる。
    /// </summary>
    private void PruneRetentionResets()
    {
        if (_retentionResets.Count == 0)
            return;

        var longest = TimeSpan.FromMinutes(HistoryStore.MaxRetentionMinutes);
        var removed = _retentionResets.RemoveAll(r => r + longest <= EffectiveCutoffUtc);

        if (removed > 0)
            _checkpointDirty = true;
    }

    /// <summary>
    /// 次に履歴をまとめて消す時刻。対象に滞在中は null（期限なし）。
    /// 最後の退出か、それより後に押した「延長」の、新しいほうから60分。
    /// </summary>
    private DateTime? ComputeDeadline(List<DateTime> starts)
    {
        if (!_autoReset || StoppedInTarget() || starts.Count == 0)
            return null;

        return starts[^1] + _retention;
    }

    /// <summary>
    /// 期限切れの区切り時刻。これより前の行を捨てる。
    ///
    /// 数え始め（退出と「延長」→<see cref="CollectRetentionStarts"/>）ごとに「+60分」を期限とし、
    /// その期限より前に対象へ戻っているか、次の数え始めがあれば成立させない。
    /// 次の数え始めで取り消すのは「延長」のためで、押した時点でもう一度退出したものとして、
    /// 数えている途中の期限をそこで打ち切る。対象を離れ直すには一度対象へ戻る必要があるので、
    /// 退出どうしでは先に「対象へ戻った」ほうで取り消しが決まり、従来と結果は変わらない。
    /// </summary>
    private DateTime ComputeCutoff(List<DateTime> starts, DateTime nowUtc)
    {
        var cutoff = DateTime.MinValue;

        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var deadline = start + _retention;

            // まだ数えている途中。
            if (deadline > nowUtc)
                continue;

            // 期限前に対象へ戻っているので、この60分は取り消す。
            if (_history.FirstVisitAfter(start) is { } rejoin && rejoin < deadline)
                continue;

            // 期限前に数え直している（延長を押した）ので、この60分は取り消す。
            // 並びは古い順なので、すぐ次の数え始めだけを見れば足りる。
            if (i + 1 < starts.Count && starts[i + 1] < deadline)
                continue;

            if (deadline > cutoff)
                cutoff = deadline;
        }

        return cutoff;
    }

    /// <summary>
    /// 期限を数え始める時刻の列（古い順）。対象インスタンスを離れた時刻に、
    /// 「延長」を押した時刻を混ぜたもの。延長は押した時点でもう一度退出したものとして扱う（→5.17・5.36）。
    /// </summary>
    private List<DateTime> CollectRetentionStarts()
    {
        var starts = CollectTargetLeaves();
        starts.AddRange(_retentionResets);

        // 滞在中も数えるときは、対象インスタンスへ入った時刻も数え始めにする（→実装メモ5.90）。
        if (!_stopInTarget)
            starts.AddRange(_history.Records.Select(r => r.VisitedAtUtc).Where(v => v >= _targetCountFromUtc));

        // 自動リセットをオンへ戻す前の退出・延長からは数えない（→実装メモ5.71）。
        if (_autoResetFromUtc != DateTime.MinValue)
            starts.RemoveAll(s => s < _autoResetFromUtc);

        starts.Sort();
        return starts;
    }

    /// <summary>対象インスタンスを離れた時刻を全ソースから集める（古い順）。</summary>
    private List<DateTime> CollectTargetLeaves()
    {
        var leaves = new List<DateTime>();

        foreach (var source in _sources.Values)
        {
            leaves.AddRange(source.Tracker.TargetLeaves);

            if (source.Tracker.State != PresenceState.InTarget || IsLive(source))
                continue;

            // 対象に滞在したままログが途切れている（終了ログのない終了・クラッシュ）。
            // セッションの終わりを退出時刻として扱う。断定できる退出ログがあれば、そちらが既に入っている。
            var ended = source.Entry.Session.ProcessExitUtc
                ?? source.Entry.Session.QuitAtUtc
                ?? source.Tracker.LastEventAtUtc;

            if (ended is { } at)
                leaves.Add(at);
        }

        leaves.Sort();
        return leaves;
    }

    /// <summary>実行中のVRChatに対応するセッションで、いま対象インスタンスにいるか。</summary>
    private bool InTargetNow()
        => _activeSourceId is not null
           && _sources.TryGetValue(_activeSourceId, out var active)
           && active.Tracker.State == PresenceState.InTarget
           && IsLive(active);

    /// <summary>実行中のVRChatに対応するソースか。滞在中なら期限を数え始めない。</summary>
    private bool IsLive(OpenSource source)
        => _client is not null
           && string.Equals(source.Entry.SourceSessionId, _activeSourceId, StringComparison.Ordinal);
}
