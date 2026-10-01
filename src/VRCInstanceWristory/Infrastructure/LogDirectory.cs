using System.Globalization;
using System.Security.Cryptography;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>ログフォルダーの探索とファイル識別（仕様4.1・5.3節）。</summary>
public static class LogDirectory
{
    public const string FilePattern = "output_log_*.txt";

    private const string NamePrefix = "output_log_";
    private const string NameSuffix = ".txt";
    private const string NameTimeFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>
    /// 先頭バイト列のハッシュに使う長さ。ログの1行目以降を含む短い長さにして、
    /// 書き込み途中の短縮（同じ先頭）と、別ファイルへの差し替え（違う先頭）を区別できるようにする。
    /// </summary>
    public const int PrefixBytes = 256;

    /// <summary>現在のユーザーの LocalLow/VRChat/VRChat。ユーザー名はハードコードしない。</summary>
    public static string DefaultLogDirectory()
    {
        var localLow = Path.GetFullPath(Path.Combine(AppPaths.LocalAppData, "..", "LocalLow"));
        return Path.Combine(localLow, "VRChat", "VRChat");
    }

    /// <summary>直下のログファイルだけを返す（再帰検索しない）。</summary>
    public static List<string> EnumerateLogFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return Directory.EnumerateFiles(directory, FilePattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>ファイル名からログセッション開始時刻（ローカル）を読む。</summary>
    public static bool TryParseSessionStart(string fileName, out DateTime local)
    {
        local = default;
        var name = Path.GetFileName(fileName);

        if (!name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(NameSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = name[NamePrefix.Length..^NameSuffix.Length];
        return DateTime.TryParseExact(body, NameTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
    }

    /// <summary>先頭バイト列のハッシュ。ファイル全体のハッシュは追記で変わるため使わない。</summary>
    public static bool TryComputePrefixHash(string path, out string hash, out int length)
    {
        hash = string.Empty;
        length = 0;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[PrefixBytes];
            var read = stream.ReadAtLeast(buffer, PrefixBytes, throwOnEndOfStream: false);
            length = read;
            hash = Convert.ToHexStringLower(SHA256.HashData(buffer.AsSpan(0, read)));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static DateTime? TryGetCreationTimeUtc(string path)
    {
        try
        {
            return File.GetCreationTimeUtc(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static DateTime? TryGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
