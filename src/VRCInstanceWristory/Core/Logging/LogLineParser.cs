using System.Globalization;

namespace VRCInstanceWristory.Core.Logging;

/// <summary>
/// VRChatの出力ログ1行を解析する。書式は
/// <c>yyyy.MM.dd HH:mm:ss {level,-11}-  {message}</c>
/// （提供ログ output_log_2026-09-11_00-48-17.txt で確認）。
/// スタックトレース等の継続行は日時を持たない。
/// </summary>
public static class LogLineParser
{
    /// <summary>"yyyy.MM.dd HH:mm:ss " の長さ。</summary>
    private const int TimestampLength = 19;

    public static LogLine Parse(ReadOnlySpan<char> raw, long byteOffset, int lineNumber)
    {
        // 行末の空白・改行だけを除去する（仕様5.2: それ以外は勝手に除去しない）。
        var line = raw.TrimEnd(" \t\r\n".AsSpan());

        if (line.Length < TimestampLength || !LooksLikeTimestamp(line))
            return new LogLine(byteOffset, lineNumber, LogLineKind.Continuation, default, string.Empty, line.ToString());

        // レベルと区切り "-  " を探す。区切りはダッシュ＋空白2個で固定。
        var rest = line[TimestampLength..];
        if (rest.Length == 0 || rest[0] != ' ')
            return new LogLine(byteOffset, lineNumber, LogLineKind.Continuation, default, string.Empty, line.ToString());

        rest = rest[1..];
        var sep = rest.IndexOf("-  ".AsSpan());
        if (sep < 0)
            return new LogLine(byteOffset, lineNumber, LogLineKind.Continuation, default, string.Empty, line.ToString());

        var level = rest[..sep].TrimEnd();
        if (level.Length == 0 || !IsAsciiLetters(level))
            return new LogLine(byteOffset, lineNumber, LogLineKind.Continuation, default, string.Empty, line.ToString());

        var message = rest[(sep + 3)..];

        if (!DateTime.TryParseExact(
                line[..TimestampLength],
                "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timestamp))
        {
            return new LogLine(byteOffset, lineNumber, LogLineKind.BadTimestamp, default, level.ToString(), message.ToString());
        }

        return new LogLine(byteOffset, lineNumber, LogLineKind.Entry, timestamp, level.ToString(), message.ToString());
    }

    public static LogLine Undecodable(long byteOffset, int lineNumber)
        => new(byteOffset, lineNumber, LogLineKind.Undecodable, default, string.Empty, string.Empty);

    private static bool LooksLikeTimestamp(ReadOnlySpan<char> line)
    {
        // 2026.09.11 00:48:19
        return IsDigit(line[0]) && IsDigit(line[1]) && IsDigit(line[2]) && IsDigit(line[3])
            && line[4] == '.' && IsDigit(line[5]) && IsDigit(line[6])
            && line[7] == '.' && IsDigit(line[8]) && IsDigit(line[9])
            && line[10] == ' '
            && IsDigit(line[11]) && IsDigit(line[12]) && line[13] == ':'
            && IsDigit(line[14]) && IsDigit(line[15]) && line[16] == ':'
            && IsDigit(line[17]) && IsDigit(line[18]);
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetters(ReadOnlySpan<char> s)
    {
        foreach (var c in s)
        {
            if (c is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z'))
                return false;
        }
        return true;
    }
}
