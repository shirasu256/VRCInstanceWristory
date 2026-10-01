using System.Text;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// ログの増分読み取り（仕様4.3節）。呼び出し側が途中で読むのをやめても（エンジンが StopAtUtc で break する）、
/// 渡し終えた行を返し直さず、やめた行から続きを返す。
/// </summary>
public class LogFileReaderTests
{
    private const string Line1 = "2026.09.11 00:57:08 Debug      -  one";
    private const string Line2 = "2026.09.11 00:57:09 Debug      -  two";
    private const string Line3 = "2026.09.11 00:57:10 Debug      -  three";
    private const string Line4 = "2026.09.11 00:57:11 Debug      -  four";

    [Fact]
    public void 途中でやめた行から続きを返す()
    {
        using var tempFile = WriteLog($"{Line1}\r\n{Line2}\r\n{Line3}\r\n");
        var path = tempFile.Path;

        using var reader = new LogFileReader(path);

        // 2行目を受け取ったところでやめる（2行目は処理していない）。
        foreach (var line in reader.ReadNewLines())
        {
            if (line.LineNumber == 2)
                break;
        }

        Assert.Equal(1, reader.LineNumber);
        Assert.Equal(Line1.Length + 2, reader.Position);

        // ファイルに追記がなくても、やめた行とその後ろを返す。
        var rest = reader.ReadNewLines().ToList();

        Assert.Equal(["two", "three"], rest.Select(l => l.Message));
        Assert.Equal([2, 3], rest.Select(l => l.LineNumber));
        Assert.Equal(Line1.Length + 2, rest[0].ByteOffset);
        Assert.Equal(2L * (Line1.Length + 2), rest[1].ByteOffset);
        Assert.Equal(3, reader.LineNumber);
        Assert.Equal(new FileInfo(path).Length, reader.Position);
    }

    [Fact]
    public void 途中でやめたあとに追記されても渡し終えた行を返し直さない()
    {
        using var tempFile = WriteLog($"{Line1}\n{Line2}\n{Line3}\n");
        var path = tempFile.Path;

        using var reader = new LogFileReader(path);

        foreach (var line in reader.ReadNewLines())
        {
            if (line.LineNumber == 3)
                break;
        }

        File.AppendAllText(path, Line4 + "\n");

        var rest = reader.ReadNewLines().ToList();

        Assert.Equal(["three", "four"], rest.Select(l => l.Message));
        Assert.Equal([3, 4], rest.Select(l => l.LineNumber));
        Assert.Equal(2L * (Line1.Length + 1), rest[0].ByteOffset);
        Assert.Equal(new FileInfo(path).Length, reader.Position);
    }

    [Fact]
    public void 行の途中までの追記は次回へ持ち越す()
    {
        using var tempFile = WriteLog($"{Line1}\n2026.09.11 00:57:09 Debug");
        var path = tempFile.Path;

        using var reader = new LogFileReader(path);

        Assert.Equal(["one"], reader.ReadNewLines().Select(l => l.Message));

        File.AppendAllText(path, "      -  two\n");

        var rest = reader.ReadNewLines().ToList();

        Assert.Equal(["two"], rest.Select(l => l.Message));
        Assert.Equal(Line1.Length + 1, rest[0].ByteOffset);
        Assert.Equal(2, reader.LineNumber);
    }

    /// <summary>本文を書いた一時ファイル。使い終わったら消す。</summary>
    private static TempFile WriteLog(string text)
    {
        var file = new TempFile("log", ".txt");
        File.WriteAllText(file.Path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return file;
    }
}
