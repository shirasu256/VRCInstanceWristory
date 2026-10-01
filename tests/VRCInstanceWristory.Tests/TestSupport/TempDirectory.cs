namespace VRCInstanceWristory.Tests;

/// <summary>
/// 検証用の一時フォルダー（<c>%TEMP%\vrciw-&lt;名前&gt;-&lt;GUID&gt;</c>）。作ってから渡し、使い終わったら中身ごと消す。
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory(string name)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vrciw-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 後始末に失敗してもテスト結果には影響しない。
        }
    }
}
