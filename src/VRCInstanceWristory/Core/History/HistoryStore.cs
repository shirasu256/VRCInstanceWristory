using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core.History;

/// <summary>
/// 表示対象の訪問履歴（仕様3.1・3.2節）。
///
/// 保持の規則は2026-09-18のユーザー指定で変更した。行ごとに入室から60分ではなく、
/// 「対象種別のインスタンスを離れてから60分」で全行をまとめて捨てる。
/// 対象に滞在している間と、離れてから60分に達するまでは、どれだけ古い行も残す。
/// 期限の判定（退出時刻の収集と期限切れの実行）は <see cref="HistoryEngine"/> が行い、
/// ここでは与えられた区切り時刻より前を捨てる操作だけを持つ。
///
/// 並びは入室成功時刻の昇順、同秒はセッション順、次にファイル内の出現順。
/// </summary>
public sealed class HistoryStore
{
    /// <summary>
    /// 対象インスタンスを離れてから、履歴をまとめて捨てるまでの既定の時間。
    /// 実際の時間は設定 <c>retentionMinutes</c> で変えられる（→<see cref="EngineOptions.Retention"/>・実装メモ5.39）。
    /// 滞在時間の棒はこの既定値で最大になり、設定では変わらない（→<see cref="Presentation.RowFormatter.StayFraction"/>）。
    /// </summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(60);

    /// <summary>設定で選べる保持時間の範囲（分）。</summary>
    public const int MinRetentionMinutes = 1;

    public const int MaxRetentionMinutes = 900;

    /// <summary>
    /// 残す行の上限（2026-10-01のユーザー指定→実装メモ5.108）。自動リセットを無効にしていても、これを超えた古い行から消す。
    /// 一覧は全行を1枚の下絵に描くので、際限なく増えるとメモリと描き直しの時間が行数に比例して伸び、最後は下絵を作れずに落ちる。
    /// </summary>
    public const int MaxRecords = 500;

    /// <summary>残す行の古さの上限（入室から→実装メモ5.108）。自動リセットを無効にしていても、これより前に入った行は消す。</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly Dictionary<string, VisitRecord> _byEventId = new(StringComparer.Ordinal);
    private readonly List<VisitRecord> _ordered = [];

    // 保存してあった訪問履歴（history.json）から戻した行（→実装メモ5.108）。同じ訪問をログから読めたら、そちらへ置き換える。
    // eventId はソースIDの台帳（チェックポイント）が読めないと変わるので、インスタンスと入室時刻の組でも引く。
    private readonly HashSet<VisitRecord> _restored = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string LocationKey, DateTime VisitedAtUtc), VisitRecord> _restoredByVisit = [];

    /// <summary>最も古い行の入室成功時刻。行がなければ null。</summary>
    public DateTime? EarliestVisitUtc => _ordered.Count > 0 ? _ordered[0].VisitedAtUtc : null;

    /// <summary>行か行の中身が変わるたびに進む番号。訪問履歴の保存（→実装メモ5.108）を、変わったときだけ行うのに使う。</summary>
    public long Version { get; private set; }

    /// <summary>
    /// 重複するeventIdは無視して false を返す（仕様5.3節）。
    /// 保存から戻した行と同じ訪問なら、ログから読んだこちらへ置き換えて true を返す（→実装メモ5.108）。
    /// </summary>
    public bool Add(VisitRecord record)
    {
        if (_byEventId.TryGetValue(record.EventId, out var existing))
        {
            if (!_restored.Contains(existing))
                return false;

            Replace(existing);
        }

        if (_restoredByVisit.TryGetValue((record.LocationKey, record.VisitedAtUtc), out var same))
            Replace(same);

        _byEventId.Add(record.EventId, record);
        Insert(record);
        return true;
    }

    // 保存から戻した行を、ログから読んだ行へ置き換えるために外す。
    private void Replace(VisitRecord restored)
    {
        Detach(restored);
        _ordered.Remove(restored);
        _byEventId.Remove(restored.EventId);
    }

    /// <summary>
    /// 保存してあった行を戻す（→実装メモ5.108）。既にある eventId・同じ訪問の行は戻さない。戻した件数を返す。
    /// 戻した行は、同じ訪問をログから読めたときに <see cref="Add"/> で置き換わる。
    /// </summary>
    public int Restore(IEnumerable<VisitRecord> records)
    {
        var added = 0;

        foreach (var record in records)
        {
            if (_byEventId.ContainsKey(record.EventId) || _restoredByVisit.ContainsKey((record.LocationKey, record.VisitedAtUtc)))
                continue;

            if (_ordered.Exists(r => r.VisitedAtUtc == record.VisitedAtUtc && string.Equals(r.LocationKey, record.LocationKey, StringComparison.Ordinal)))
                continue;

            _byEventId.Add(record.EventId, record);
            _restored.Add(record);
            _restoredByVisit[(record.LocationKey, record.VisitedAtUtc)] = record;
            Insert(record);
            added++;
        }

        return added;
    }

    /// <summary>保存から戻したまま、ログから読み直せていない行（古い順）。</summary>
    public IEnumerable<VisitRecord> RestoredRecords => _ordered.Where(_restored.Contains);

    private void Insert(VisitRecord record)
    {
        var index = _ordered.BinarySearch(record, VisitOrder.Instance);
        _ordered.Insert(index < 0 ? ~index : index, record);
        Version++;
    }

    private void Detach(VisitRecord record)
    {
        if (!_restored.Remove(record))
            return;

        var key = (record.LocationKey, record.VisitedAtUtc);
        if (_restoredByVisit.TryGetValue(key, out var r) && ReferenceEquals(r, record))
            _restoredByVisit.Remove(key);
    }

    /// <summary>
    /// 既にある行へ退出時刻を書き戻す（2026-09-20のユーザー指定の「入室 - 退出」表示）。
    /// 重複eventIdの追加では元の行が残るため、退出は追加ではなくこの操作で反映する。
    /// 対象の行がない（期限切れで捨てた後など）場合は false を返し、何もしない。
    /// </summary>
    public bool SetLeave(string eventId, DateTime leftAtUtc)
    {
        if (!_byEventId.TryGetValue(eventId, out var record))
            return false;

        if (record.LeftAtUtc == leftAtUtc)
            return false;

        record.LeftAtUtc = leftAtUtc;
        Version++;
        return true;
    }

    /// <summary>
    /// 既にある行へ「クラッシュで終わった」印を付ける（2026-09-21のユーザー指定→5.30節）。
    /// 退出時刻を赤くし、次の行との間にクラッシュの帯を入れる判断に使う。
    /// 既に付いていれば false を返す。
    /// </summary>
    public bool SetCrashed(string eventId)
    {
        if (!_byEventId.TryGetValue(eventId, out var record) || record.EndedByCrash)
            return false;

        record.EndedByCrash = true;
        Version++;
        return true;
    }

    /// <summary>
    /// 既にある行へ「離れたあとクラッシュした」印を付ける（2026-10-05のユーザー指定→実装メモ5.129）。
    /// 対象外のインスタンスにいる間のクラッシュで使う。<paramref name="excludedBefore"/> なら、
    /// クラッシュの前に対象外へ入っていたので <see cref="VisitRecord.ExcludedAfter"/> も立てる。
    /// 変化がなければ false を返す。
    /// </summary>
    public bool SetCrashedAfter(string eventId, bool excludedBefore)
    {
        if (!_byEventId.TryGetValue(eventId, out var record))
            return false;

        if (record.CrashedAfter && (record.ExcludedAfter || !excludedBefore))
            return false;

        record.CrashedAfter = true;
        record.ExcludedAfter |= excludedBefore;
        Version++;
        return true;
    }

    /// <summary>その時刻より前に入室した行のうち、いちばん新しいもの。なければ null。</summary>
    public VisitRecord? LastVisitBefore(DateTime beforeUtc)
    {
        for (var i = _ordered.Count - 1; i >= 0; i--)
        {
            if (_ordered[i].VisitedAtUtc < beforeUtc)
                return _ordered[i];
        }

        return null;
    }

    /// <summary>
    /// 既にある行へ「離れたあと対象外のインスタンスへ移った」印を付ける
    /// （2026-09-26のユーザー指定→実装メモ5.38）。既に付いていれば false を返す。
    /// </summary>
    public bool SetExcludedAfter(string eventId)
    {
        if (!_byEventId.TryGetValue(eventId, out var record) || record.ExcludedAfter)
            return false;

        record.ExcludedAfter = true;
        Version++;
        return true;
    }

    /// <summary>
    /// 既にある行へ「退出時にいた人数」を書き戻す（2026-09-20のユーザー指定）。
    /// 変化がなければ false を返す。
    /// </summary>
    public bool SetPeopleCount(string eventId, int count)
    {
        if (!_byEventId.TryGetValue(eventId, out var record))
            return false;

        if (record.PeopleCount == count)
            return false;

        record.PeopleCount = count;
        Version++;
        return true;
    }

    /// <summary>
    /// 既にある行へ「一緒にいた人」を1人ぶん書き足す・書き換える（2026-09-26のユーザー指定→実装メモ5.46）。
    /// 同じ userId の人は1件にまとめる。変化がなければ false。
    /// </summary>
    public bool SetCompanion(string eventId, Companion companion)
    {
        if (!_byEventId.TryGetValue(eventId, out var record))
            return false;

        var list = record.Companions;
        var index = list.FindIndex(c => string.Equals(c.UserId, companion.UserId, StringComparison.Ordinal));

        if (index < 0)
        {
            list.Add(companion);
            Version++;
            return true;
        }

        if (list[index] == companion)
            return false;

        list[index] = companion;
        Version++;
        return true;
    }

    /// <summary>
    /// 既にある行へ写真を1枚書き足す（2026-09-26のユーザー指定→実装メモ5.47）。
    /// ログを読み直しても同じ写真を二重に数えないよう、保存先が同じものは足さない。
    /// </summary>
    public bool AddPhoto(string eventId, VisitPhoto photo)
    {
        if (!_byEventId.TryGetValue(eventId, out var record))
            return false;

        if (record.Photos.Exists(p => string.Equals(p.Path, photo.Path, StringComparison.OrdinalIgnoreCase)))
            return false;

        record.Photos.Add(photo);
        Version++;
        return true;
    }

    public VisitRecord? Find(string eventId)
        => _byEventId.TryGetValue(eventId, out var r) ? r : null;

    /// <summary>
    /// 表示する履歴を古い順に返す。上限は now（未来の行は出さない）。
    /// 下限は消去の区切り時刻。実体を捨てる <see cref="PruneBefore"/> は1秒ごとなので、
    /// その間に期限が来た行をここで隠し、期限ちょうどに消えるようにする。
    /// </summary>
    public IReadOnlyList<VisitRecord> GetVisible(DateTime nowUtc, DateTime cutoffUtc)
    {
        var result = new List<VisitRecord>(_ordered.Count);

        foreach (var r in _ordered)
        {
            if (r.VisitedAtUtc >= cutoffUtc && r.VisitedAtUtc <= nowUtc)
                result.Add(r);
        }

        return result;
    }

    /// <summary>
    /// いま残っている行のインスタンス（<see cref="VisitRecord.LocationKey"/>）。
    /// 消えた行に付けていた目印を捨てるのに使う（→実装メモ5.32）。
    /// </summary>
    public IEnumerable<string> LocationKeys() => _ordered.Select(r => r.LocationKey);

    /// <summary>いま持っている行（古い順）。ワールドごとの目印を行へ当てはめるのに使う（→実装メモ5.44）。</summary>
    public IReadOnlyList<VisitRecord> Records => _ordered;

    /// <summary>指定時刻より後の最初の入室成功時刻。退出後に対象へ戻ったかの判定に使う。</summary>
    public DateTime? FirstVisitAfter(DateTime afterUtc)
    {
        foreach (var r in _ordered)
        {
            if (r.VisitedAtUtc > afterUtc)
                return r.VisitedAtUtc;
        }

        return null;
    }

    /// <summary>区切り時刻より前の行を捨てる。回数はここでは変えない（仕様3.3節）。</summary>
    public int PruneBefore(DateTime cutoffUtc)
    {
        var removed = 0;

        for (var i = _ordered.Count - 1; i >= 0; i--)
        {
            var r = _ordered[i];
            if (r.VisitedAtUtc >= cutoffUtc)
                continue;

            _ordered.RemoveAt(i);
            _byEventId.Remove(r.EventId);
            Detach(r);
            removed++;
        }

        if (removed > 0)
            Version++;

        return removed;
    }

    /// <summary>
    /// 1つのソース由来の行をすべて捨てる。短縮・差し替えからの再構築で、
    /// 旧派生レコードを残したまま再追加しないために使う（仕様4.3節）。
    /// </summary>
    public int RemoveBySource(string sourceSessionId)
    {
        var removed = 0;

        for (var i = _ordered.Count - 1; i >= 0; i--)
        {
            var r = _ordered[i];
            if (!string.Equals(r.SourceSessionId, sourceSessionId, StringComparison.Ordinal))
                continue;

            _ordered.RemoveAt(i);
            _byEventId.Remove(r.EventId);
            Detach(r);
            removed++;
        }

        if (removed > 0)
            Version++;

        return removed;
    }

    public void Clear()
    {
        if (_ordered.Count > 0)
            Version++;

        _ordered.Clear();
        _byEventId.Clear();
        _restored.Clear();
        _restoredByVisit.Clear();
    }

    private sealed class VisitOrder : IComparer<VisitRecord>
    {
        public static readonly VisitOrder Instance = new();

        public int Compare(VisitRecord? x, VisitRecord? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            var c = x.VisitedAtUtc.CompareTo(y.VisitedAtUtc);
            if (c != 0)
                return c;

            c = x.SessionOrder.CompareTo(y.SessionOrder);
            if (c != 0)
                return c;

            c = x.SuccessByteOffset.CompareTo(y.SuccessByteOffset);
            if (c != 0)
                return c;

            return string.CompareOrdinal(x.EventId, y.EventId);
        }
    }
}
