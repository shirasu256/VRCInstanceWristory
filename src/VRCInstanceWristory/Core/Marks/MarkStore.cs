namespace VRCInstanceWristory.Core.Marks;

/// <summary>目印1つ。<see cref="MarkedAtUtc"/> は付けた（付け替えた）時刻で、ログのリセットで消すかどうかに使う。</summary>
public readonly record struct MarkEntry(InstanceMark Mark, DateTime MarkedAtUtc);

/// <summary>
/// 目印の保管（2026-09-22のユーザー指定→実装メモ5.32）。
///
/// 目印は1回の訪問ではなく<b>インスタンスそのもの</b>に付く。鍵は
/// <see cref="Visits.VisitRecord.LocationKey"/>（ワールドID + インスタンス番号）で、
/// 同じインスタンスの行はすべて同じ目印になる。あとで同じインスタンスへ戻ったときも、
/// 新しい行へそのまま引き継がれる。
///
/// 付けた目印はアプリを終了しても残し（<see cref="MarkFile"/>）、<b>付けてから次のログのリセットで消す</b>
/// （2026-09-27のユーザー指定→実装メモ5.54・→<see cref="RemoveStale"/>）。
/// </summary>
public sealed class MarkStore
{
    private readonly Dictionary<string, MarkEntry> _entries = new(StringComparer.Ordinal);

    // 描画側へ渡した一覧。変えるたびに作り直すので、渡したものがあとから書き換わることはない。
    private IReadOnlyDictionary<string, InstanceMark>? _all;

    /// <summary>
    /// 描画側へ渡す一覧。鍵は <c>LocationKey</c>。その時点の写しで、あとの付け外しでは変わらない
    /// （変わらない間は同じ写しを返すので、毎フレーム呼んでも作り直さない）。
    /// </summary>
    public IReadOnlyDictionary<string, InstanceMark> All
        => _all ??= _entries.ToDictionary(p => p.Key, p => p.Value.Mark, StringComparer.Ordinal);

    public InstanceMark Get(string locationKey)
        => _entries.TryGetValue(locationKey, out var entry) ? entry.Mark : InstanceMark.None;

    /// <summary>
    /// 目印を選んだときの反映。同じ目印をもう一度選んだときは外す。
    /// 見た目が変わったとき（＝描き直しが要るとき）だけ true を返す。
    /// </summary>
    public bool Toggle(string locationKey, InstanceMark chosen, DateTime markedAtUtc = default)
        => Set(locationKey, InstanceMarks.Toggle(Get(locationKey), chosen), markedAtUtc);

    /// <summary>目印を指定の値にする。変化がなければ false。</summary>
    public bool Set(string locationKey, InstanceMark mark, DateTime markedAtUtc = default)
    {
        if (Get(locationKey) == mark)
            return false;

        if (mark == InstanceMark.None)
            _entries.Remove(locationKey);
        else
            _entries[locationKey] = new MarkEntry(mark, markedAtUtc);

        _all = null;
        return true;
    }

    /// <summary>
    /// ログのリセット（履歴の消去の区切りが <paramref name="cutoffUtc"/> へ進んだ）で、目印を消す（→実装メモ5.54）。
    ///
    /// <list type="bullet">
    /// <item>区切りより前に付けた目印。付けてから、そのあとのリセットで消える</item>
    /// <item>行が1つも残っていないインスタンスの目印（<paramref name="presentLocationKeys"/> にないもの）。
    /// 保持時間を短くして、付けたあとの時刻が区切りになったときも、行と一緒に消える</item>
    /// </list>
    ///
    /// 消したものがあれば true。
    /// </summary>
    public bool RemoveStale(DateTime cutoffUtc, IEnumerable<string> presentLocationKeys)
    {
        var present = new HashSet<string>(presentLocationKeys, StringComparer.Ordinal);
        return RemoveWhere((key, entry) => entry.MarkedAtUtc < cutoffUtc || !present.Contains(key));
    }

    /// <summary>
    /// 残っている行にないインスタンスの目印を消す。行の上限で古い行が消えたとき（→実装メモ5.108）に使う。消したものがあれば true。
    /// </summary>
    public bool RemoveAbsent(IEnumerable<string> presentLocationKeys)
    {
        var present = new HashSet<string>(presentLocationKeys, StringComparer.Ordinal);
        return RemoveWhere((key, _) => !present.Contains(key));
    }

    /// <summary>区切りより前に付けた目印だけを消す（起動時に、保存してあった区切りで確かめる）。消したものがあれば true。</summary>
    public bool RemoveMarkedBefore(DateTime cutoffUtc)
        => RemoveWhere((_, entry) => entry.MarkedAtUtc < cutoffUtc);

    /// <summary>保存用の写し。</summary>
    public Dictionary<string, MarkEntry> Entries() => new(_entries, StringComparer.Ordinal);

    /// <summary>保存してあった目印で置き換える。</summary>
    public void Load(IEnumerable<KeyValuePair<string, MarkEntry>> entries)
    {
        Clear();

        foreach (var (key, entry) in entries)
            Set(key, entry.Mark, entry.MarkedAtUtc);
    }

    public void Clear()
    {
        _entries.Clear();
        _all = null;
    }

    private bool RemoveWhere(Func<string, MarkEntry, bool> stale)
    {
        var keys = _entries.Where(p => stale(p.Key, p.Value)).Select(p => p.Key).ToList();

        foreach (var key in keys)
            _entries.Remove(key);

        if (keys.Count > 0)
            _all = null;

        return keys.Count > 0;
    }
}
