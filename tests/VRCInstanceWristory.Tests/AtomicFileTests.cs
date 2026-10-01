using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 途中で切れないファイルの保存（<see cref="AtomicFile"/>→実装メモ5.59）。
/// 設定・チェックポイント・目印は、書いている途中で電源が落ちても、古い中身か新しい中身のどちらかを残す。
/// </summary>
public class AtomicFileTests
{
    [Fact]
    public void 書いている途中で失敗しても元のファイルが残る()
    {
        using var tempFile = new TempFile("atomic-fail");
        var path = tempFile.Path;
        File.WriteAllText(path, "old");

        Assert.ThrowsAny<IOException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write("ne"u8);
            throw new IOException("電源断の代わり");
        }));

        Assert.Equal("old", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void 設定の保存は一時ファイルを残さない()
    {
        using var tempFile = new TempFile("atomic-settings");
        var path = tempFile.Path;

        var settings = new AppSettings { OverlayWidthMeters = 0.2f };
        settings.SaveFields(path, SettingsField.OverlayWidth);

        // 2回目は既存のファイルを置き換える。
        settings.OverlayWidthMeters = 0.25f;
        settings.SaveFields(path, SettingsField.OverlayWidth);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(0.25f, AppSettings.Load(path, new CollectingDiagnostics()).OverlayWidthMeters);

        // BOM を付けない（これまでの File.WriteAllText と同じ）。
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }
}
