namespace VRCInstanceWristory.Tests;

/// <summary>
/// 検証用の一時ファイルのパス（<c>%TEMP%\vrciw-&lt;名前&gt;-&lt;GUID&gt;&lt;拡張子&gt;</c>）。ファイルは作らない。
/// 使い終わったら、書いたファイルと <see cref="Infrastructure.AtomicFile"/> の一時ファイル（<c>.tmp</c>）を消す。
/// </summary>
public sealed class TempFile(string name, string extension = ".json") : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vrciw-{name}-{Guid.NewGuid():N}{extension}");

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
            File.Delete(Path + ".tmp");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 後始末に失敗してもテスト結果には影響しない。
        }
    }
}
