using System.Globalization;

namespace VRCInstanceWristory.Core.Counting;

/// <summary>走査で見つけたログファイルと、既知のソースとの照合の結果（→<see cref="LogSourceRegistry.Match"/>）。</summary>
public enum LogSourceMatchKind
{
    /// <summary>同じ名前の既知のソースがない。新しいソースとして登録する。</summary>
    New,

    /// <summary>既知のソースと同じファイル（追記で伸びたものを含む）。</summary>
    Same,

    /// <summary>同じ名前だが先頭が違う（ファイルが差し替えられた）。旧ソースを捨て、新しいソースとして登録する。</summary>
    Replaced,
}

/// <summary>照合の結果。<see cref="Entry"/> は <see cref="LogSourceMatchKind.Same"/> ならそのソース、
/// <see cref="LogSourceMatchKind.Replaced"/> なら差し替えられた旧ソース、<see cref="LogSourceMatchKind.New"/> なら null。</summary>
public readonly record struct LogSourceMatch(LogSourceMatchKind Kind, LogSourceEntry? Entry)
{
    public static readonly LogSourceMatch New = new(LogSourceMatchKind.New, null);
}

/// <summary>既知のログソースの台帳。カウント期間の判定にはファイル削除後の記録も使う。</summary>
public sealed class LogSourceRegistry
{
    private readonly Dictionary<string, LogSourceEntry> _byId = new(StringComparer.Ordinal);
    private int _nextId = 1;

    public IReadOnlyCollection<LogSourceEntry> Entries => _byId.Values;

    public void Add(LogSourceEntry entry)
    {
        _byId[entry.SourceSessionId] = entry;

        // 既存のIDより大きい番号から採番し、再起動後も番号が重ならないようにする。
        ReserveId(entry.SourceSessionId);
    }

    /// <summary>
    /// 台帳にはないが、どこかで使われているID（保存してあった訪問履歴の行→実装メモ5.108）より大きい番号から採番する。
    /// 台帳から捨てた記録のIDを新しいソースへ使い回すと、保存してあった行と eventId が重なる。
    /// </summary>
    public void ReserveId(string sourceSessionId)
    {
        var digits = sourceSessionId.AsSpan();
        var start = 0;
        while (start < digits.Length && !char.IsAsciiDigit(digits[start]))
            start++;

        if (start < digits.Length && int.TryParse(digits[start..], CultureInfo.InvariantCulture, out var n) && n >= _nextId)
            _nextId = n + 1;
    }

    /// <summary>IDで記録を引く。捨てた記録・知らないIDなら null。</summary>
    public LogSourceEntry? Find(string sourceSessionId)
        => _byId.GetValueOrDefault(sourceSessionId);

    /// <summary>
    /// 既知のソースを、ファイル名・作成時刻・先頭バイト列の一致で探す。
    /// 先頭が変わっていれば差し替え（<see cref="LogSourceMatchKind.Replaced"/>）として、旧ソースを添えて返す。
    ///
    /// 差し替えのあとは、同じ名前のソースが台帳に2つ残る（旧ソースは回数の判定のために残す）。
    /// 名前が同じものをすべて見て、先頭が合うものがあればそれを返す。以前は最初に見つけた1つで判断していたため、
    /// 旧ソースに当たって毎回「新しいソース」になり、走査のたびにソースが増えていた。
    /// </summary>
    public LogSourceMatch Match(string fileName, DateTime? createdUtc, string prefixHash, int prefixLength)
    {
        LogSourceEntry? replaced = null;

        foreach (var e in _byId.Values)
        {
            if (!string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (e.PrefixHash is null || e.PrefixLength == 0)
                return new LogSourceMatch(LogSourceMatchKind.Same, e); // 先頭情報がまだない記録（削除済みログなど）

            // 比較できる長さで照合する。
            if (e.PrefixLength == prefixLength && string.Equals(e.PrefixHash, prefixHash, StringComparison.Ordinal))
                return new LogSourceMatch(LogSourceMatchKind.Same, e);

            // 長さが違う場合は、記録側が短いときだけ「成長した同一ファイル」として追認できる。
            if (e.PrefixLength < prefixLength && e.CreatedUtc is not null && createdUtc is not null
                && Math.Abs((e.CreatedUtc.Value - createdUtc.Value).TotalSeconds) < 2)
            {
                return new LogSourceMatch(LogSourceMatchKind.Same, e);
            }

            // 差し替えの候補。前に捨てた旧ソース（ファイルなし）より、いま読んでいるソースを捨てる。
            if (replaced is null || replaced.FileMissing || !e.FileMissing)
                replaced = e;
        }

        return replaced is null ? LogSourceMatch.New : new LogSourceMatch(LogSourceMatchKind.Replaced, replaced);
    }

    /// <summary>
    /// ファイル名で既知のソースを探す（先頭を読めなかったときに、対応づけを保つのに使う）。
    /// 差し替えで同じ名前が2つあるときは、いま読んでいる（ファイルのある）ほうを返す。
    /// </summary>
    public LogSourceEntry? FindByFileName(string fileName)
    {
        LogSourceEntry? found = null;

        foreach (var e in _byId.Values)
        {
            if (!string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (found is null || found.FileMissing || !e.FileMissing)
                found = e;
        }

        return found;
    }

    /// <summary>名前空間つきの新しいソースID。検証用の再生と本番でIDが混ざらないようにする。</summary>
    public string AllocateId(string namespacePrefix)
        => namespacePrefix + _nextId++.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>起動時刻の昇順に並べたセッション一覧。</summary>
    public List<LogSessionInfo> OrderedSessions()
        => _byId.Values
            .Select(e => e.Session)
            .OrderBy(s => s.StartUtc)
            .ThenBy(s => s.SourceSessionId, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// もう要らない記録を捨てる。台帳はVRChatを起動するたびに1件増え、すべてチェックポイントへ保存するので、
    /// 捨てないと際限なく伸びる。
    ///
    /// 捨てるのは、ファイルがなく（<see cref="LogSourceEntry.FileMissing"/>）、いまのカウント期間の起点
    /// （<paramref name="periodStartUtc"/>）より前に始まった記録。ただし起点の直前の1件は残す
    /// （起点をそこに決めた根拠＝前回の終わりの記録なので、判定を読み直したときに同じ結果になるように）。
    /// 起点のセッション（<paramref name="anchorSessionId"/>）そのものは数えない。起点の時刻を決めたあとで
    /// プロセスの開始時刻が分かると、そのセッションの開始が起点より数秒前に見え、直前の1件と取り違えるため。
    /// ファイルが残っているものは、読み直しに要るので捨てない。
    /// </summary>
    /// <returns>捨てた件数。</returns>
    public int PruneMissingBefore(DateTime periodStartUtc, string anchorSessionId)
    {
        var old = _byId.Values
            .Where(e => e.Session.StartUtc < periodStartUtc
                && !string.Equals(e.SourceSessionId, anchorSessionId, StringComparison.Ordinal))
            .OrderBy(e => e.Session.StartUtc)
            .ThenBy(e => e.SourceSessionId, StringComparer.Ordinal)
            .ToList();

        if (old.Count <= 1)
            return 0;

        var removed = 0;

        // 最後の1件（起点の直前）は残す。
        foreach (var e in old.Take(old.Count - 1))
        {
            if (e.FileMissing && _byId.Remove(e.SourceSessionId))
                removed++;
        }

        return removed;
    }
}
