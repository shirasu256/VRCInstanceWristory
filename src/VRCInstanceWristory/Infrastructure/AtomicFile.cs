using System.Text;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// ファイルを「一時ファイルに書いてディスクまで書き出し、置き換える」で保存する。
/// 書いている途中で電源が落ちたりWindowsがクラッシュしたりしても、古い中身か新しい中身のどちらかが残り、
/// 途中で切れたファイル・0バイトのファイルにはならない（→実装メモ5.59）。
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
        => Write(path, stream =>
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            stream.Write(bytes);
        });

    /// <summary>書けなければ例外を投げる（一時ファイルは消す）。置き換えるまで元のファイルには触れない。</summary>
    public static void Write(string path, Action<Stream> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";

        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // 一時ファイルの後始末に失敗しても、次回の保存で上書きする。
            }

            throw;
        }
    }
}
