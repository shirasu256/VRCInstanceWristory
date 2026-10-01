using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core.Counting;

/// <summary>
/// locationKeyごとの訪問回数（仕様3.3・3.4節）。
///
/// - 回数は履歴の保持期限と独立し、行が消えても減らさない。
/// - 同じイベントの再読込では加算しない。実際の再入室は別イベントとして加算する。
/// - 期間の基準を確定できない場合は回数を不明（null）とし、0や1を捏造しない。
/// </summary>
public sealed class VisitCounter
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoredEvent> _events = new(StringComparer.Ordinal);

    public string EpochId { get; private set; } = string.Empty;

    /// <summary>false の間に付ける回数はすべて「不明」になる。</summary>
    public bool BaselineKnown { get; private set; }

    public EpochAnchor? Anchor { get; private set; }

    public IReadOnlyDictionary<string, int> Counts => _counts;

    public IReadOnlyDictionary<string, StoredEvent> Events => _events;

    /// <summary>チェックポイントからの復元。</summary>
    public void Restore(
        string epochId,
        bool baselineKnown,
        EpochAnchor? anchor,
        IEnumerable<KeyValuePair<string, int>> counts,
        IEnumerable<StoredEvent> events)
    {
        EpochId = epochId;
        BaselineKnown = baselineKnown;
        Anchor = anchor;

        _counts.Clear();
        foreach (var kv in counts)
            _counts[kv.Key] = kv.Value;

        _events.Clear();
        foreach (var e in events)
            _events[e.EventId] = e;
    }

    /// <summary>
    /// カウント期間の判定結果を反映する。起点が変わったときだけ新しい期間を作り、回数を0に戻す。
    /// eventIdごとの保存済み回数は履歴的事実なので消さない（仕様3.3節の「振り直さない」）。
    /// </summary>
    /// <returns>期間が切り替わったら true。</returns>
    public bool ApplyResolution(EpochResolution resolution)
    {
        if (Anchor is not null
            && string.Equals(Anchor.SourceSessionId, resolution.StartSessionId, StringComparison.Ordinal)
            && EpochId.Length > 0)
        {
            // 同じ期間を継続する。期間内で基準の既知・不明は変えない。
            return false;
        }

        EpochId = Guid.NewGuid().ToString("N");
        BaselineKnown = resolution.BaselineKnown;
        Anchor = new EpochAnchor(resolution.StartSessionId, resolution.StartUtc, resolution.Reason);
        _counts.Clear();
        return true;
    }

    /// <summary>
    /// 履歴の自動リセットが入ったので、ここから数え直す（2026-10-01のユーザー指定→実装メモ5.110）。
    /// 新しい期間の起点は <paramref name="fromUtc"/>（区切り）で、それより後に入った訪問（リセットで残った行）だけを数え直しの初めの回数にする。
    /// 付けた回数（eventId ごとの記録）は書き換えない。
    /// </summary>
    /// <param name="sessionId">いまのセッション。VRChat を起動し直しても、この期間を引き継ぐ目印にする。</param>
    /// <returns>数え直しの初めの回数（「直前のリセットを戻す」で、そのあとの加算だけを足し戻すのに使う）。</returns>
    public Dictionary<string, int> StartOver(string sessionId, DateTime fromUtc)
    {
        EpochId = Guid.NewGuid().ToString("N");
        BaselineKnown = true;
        Anchor = new EpochAnchor(sessionId, fromUtc, EpochReason.SinceAutoReset.Code());
        _counts.Clear();

        foreach (var e in _events.Values.Where(e => e.VisitedAtUtc >= fromUtc))
            _counts[e.LocationKey] = _counts.GetValueOrDefault(e.LocationKey) + 1;

        return new Dictionary<string, int>(_counts, StringComparer.Ordinal);
    }

    /// <summary>いまの期間の写し（「直前のリセットを戻す」で、自動リセットの前の期間へ戻すのに控える）。</summary>
    public CounterState Capture() => new(EpochId, BaselineKnown, Anchor, new Dictionary<string, int>(_counts, StringComparer.Ordinal));

    /// <summary>
    /// 自動リセットの前の期間へ戻す（→実装メモ5.110）。数え直してから加えた回数（いまの回数 − 数え直しの初めの回数）は足し戻す。
    /// </summary>
    public void RestoreState(CounterState state, IReadOnlyDictionary<string, int> seeded)
    {
        var added = _counts.ToDictionary(p => p.Key, p => p.Value - seeded.GetValueOrDefault(p.Key), StringComparer.Ordinal);

        EpochId = state.EpochId;
        BaselineKnown = state.BaselineKnown;
        Anchor = state.Anchor;
        _counts.Clear();

        foreach (var (key, count) in state.Counts)
            _counts[key] = count;

        foreach (var (key, count) in added)
        {
            if (count > 0)
                _counts[key] = _counts.GetValueOrDefault(key) + count;
        }
    }

    /// <summary>基準が不明のまま運用を続ける場合の明示的な初期化（チェックポイントなしの起動など）。</summary>
    private void EnsureEpoch()
    {
        if (EpochId.Length == 0)
            EpochId = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 訪問へ回数を付ける。
    /// 保存済みのイベントなら回数を復元するだけで加算しない。
    /// チェックポイントの適用位置より前の成功行も、既に加算済みとして扱う。
    /// </summary>
    /// <param name="allowIncrement">
    /// false なら保存済みの回数を復元するだけで加算しない。
    /// 現在のカウント期間より前のセッションを再生するときに使う。
    /// </param>
    /// <returns>新しく加算したら true。</returns>
    public bool Apply(VisitRecord record, long? appliedOffsetForSource, bool allowIncrement = true)
    {
        EnsureEpoch();

        if (_events.TryGetValue(record.EventId, out var stored))
        {
            record.VisitOrdinal = stored.Ordinal;
            record.CounterEpochId = stored.EpochId;
            return false;
        }

        if (appliedOffsetForSource is { } applied && record.SuccessByteOffset < applied)
        {
            // 適用済みだが、回数の記録は残っていない（60分より前のイベント）。
            record.VisitOrdinal = null;
            record.CounterEpochId = EpochId;
            return false;
        }

        if (!allowIncrement)
        {
            // 別のカウント期間に属する再生。回数は不明として扱い、保存もしない。
            record.VisitOrdinal = null;
            record.CounterEpochId = null;
            return false;
        }

        int? ordinal = null;
        if (BaselineKnown)
        {
            var next = _counts.GetValueOrDefault(record.LocationKey) + 1;
            _counts[record.LocationKey] = next;
            ordinal = next;
        }

        record.VisitOrdinal = ordinal;
        record.CounterEpochId = EpochId;
        _events[record.EventId] = new StoredEvent(record.EventId, record.LocationKey, record.VisitedAtUtc, ordinal, EpochId);
        return true;
    }

    /// <summary>
    /// 捨てたイベント記録を戻す（「直前のリセットを戻す」→実装メモ5.86）。いま持っている記録は上書きしない。
    /// 回数そのものは、リセットで減らしていないので触らない。
    /// </summary>
    public void RestoreEvents(IEnumerable<StoredEvent> events)
    {
        foreach (var e in events)
            _events.TryAdd(e.EventId, e);
    }

    /// <summary>
    /// 区切り時刻より前のイベント記録を捨てる。回数そのものは減らさない。
    /// 区切りは履歴と同じ（対象インスタンスを離れてから60分）。表示が続く行の回数を失わないため、
    /// 履歴より先に捨てない。
    /// </summary>
    public int PruneEventsBefore(DateTime cutoffUtc)
    {
        var removed = 0;

        foreach (var key in _events.Where(e => e.Value.VisitedAtUtc < cutoffUtc).Select(e => e.Key).ToList())
        {
            _events.Remove(key);
            removed++;
        }

        return removed;
    }
}
