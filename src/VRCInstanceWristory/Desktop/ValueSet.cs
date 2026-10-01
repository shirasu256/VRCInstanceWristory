using System.Collections;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// 中身で比べる読み取り専用の集合。record の既定の比較は集合を参照で比べてしまうので、
/// record に集合を持たせるときはこれで包む（<see cref="DesktopSettings.TargetTypes"/>）。
/// 作るときに写すので、元の集合をあとで変えても影響しない。
/// </summary>
internal sealed class ValueSet<T> : IReadOnlySet<T>, IEquatable<ValueSet<T>>
{
    private readonly HashSet<T> _items;
    private readonly int _hash;

    private ValueSet(IEnumerable<T> items)
    {
        _items = [.. items];

        // 並び順によらない値にする。
        foreach (var item in _items)
            _hash ^= item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);
    }

    /// <summary>包んだ集合。すでに包んであればそのまま返す。</summary>
    public static ValueSet<T> Of(IEnumerable<T> items) => items as ValueSet<T> ?? new ValueSet<T>(items);

    public int Count => _items.Count;

    public bool Contains(T item) => _items.Contains(item);

    public bool IsProperSubsetOf(IEnumerable<T> other) => _items.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => _items.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T> other) => _items.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => _items.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<T> other) => _items.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => _items.SetEquals(other);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ValueSet<T>? other) => other is not null && _hash == other._hash && _items.SetEquals(other._items);

    public override bool Equals(object? obj) => obj is ValueSet<T> other && Equals(other);

    public override int GetHashCode() => _hash;
}
