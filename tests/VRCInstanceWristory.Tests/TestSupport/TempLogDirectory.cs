using System.Text;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用のログフォルダー。使い終わったら削除する。</summary>
public sealed class TempLogDirectory : IDisposable
{
    public TempLogDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrciw-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteSession(DateTime sessionStartLocal, string content)
    {
        var name = $"output_log_{sessionStartLocal:yyyy-MM-dd_HH-mm-ss}.txt";
        var full = System.IO.Path.Combine(Path, name);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    public static void Append(string file, string content)
        => File.AppendAllText(file, content, new UTF8Encoding(false));

    public static void AppendBytes(string file, byte[] bytes)
    {
        using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 後始末に失敗してもテスト結果には影響しない。
        }
    }
}
