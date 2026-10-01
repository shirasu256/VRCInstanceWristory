namespace VRCInstanceWristory.Core.Counting;

/// <summary>
/// ログのファイルの先頭バイト列のハッシュ（<see cref="Infrastructure.LogDirectory.TryComputePrefixHash"/>）を、
/// ファイルの大きさと更新時刻が変わらない間は覚えておく。
///
/// フォルダーの走査は2秒ごとで、そのたびにすべてのログのファイルを開いて先頭を読み直すと、
/// 動いていない古いログの分だけ無駄な読み込みが続く。大きさか更新時刻が変われば（追記・差し替え）読み直す。
/// </summary>
public sealed class PrefixHashCache
{
    private readonly Dictionary<string, Cached> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>先頭のハッシュ。読めなければ false（覚えていた値も捨てる）。</summary>
    public bool TryGet(string path, out string hash, out int length)
    {
        var stamp = Stamp(path);

        if (stamp is { } known && _byPath.TryGetValue(path, out var cached) && cached.Stamp == known)
        {
            hash = cached.Hash;
            length = cached.Length;
            return true;
        }

        // 大きさと更新時刻は読む前に取る。読んでいる間に書き足されても、次の走査で大きさが変わって読み直す。
        if (!Infrastructure.LogDirectory.TryComputePrefixHash(path, out hash, out length))
        {
            _byPath.Remove(path);
            return false;
        }

        if (stamp is { } taken)
            _byPath[path] = new Cached(taken, hash, length);
        else
            _byPath.Remove(path);

        return true;
    }

    /// <summary>走査で見つからなかったファイルの値を捨てる。</summary>
    public void Retain(IEnumerable<string> paths)
    {
        var present = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        foreach (var path in _byPath.Keys.Where(p => !present.Contains(p)).ToList())
            _byPath.Remove(path);
    }

    private static FileStamp? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc, DateTime CreatedUtc);

    private sealed record Cached(FileStamp Stamp, string Hash, int Length);
}
