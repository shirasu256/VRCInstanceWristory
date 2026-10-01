using System.Drawing;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// アプリのアイコン（2026-09-26のユーザー指定→実装メモ5.41）。ダッシュボードと同じ図を、
/// ウィンドウ（タスクバー・タスクトレイ）と実行ファイルにも使う。
/// </summary>
public class AppIconTests
{
    [Fact]
    public void 実行ファイル用のicoにすべての大きさをPNGで入れる()
    {
        using var tempFile = new TempFile("icon", ".ico");
        var path = tempFile.Path;

        AppIcon.WriteIco(path);
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        Assert.Equal(AppIcon.IcoSizes.Length, BitConverter.ToUInt16(bytes, 4));

        for (var i = 0; i < AppIcon.IcoSizes.Length; i++)
        {
            var entry = 6 + (16 * i);
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var offset = BitConverter.ToInt32(bytes, entry + 12);

            Assert.Equal(AppIcon.IcoSizes[i], width);

            // 中身はPNG（先頭の印）。
            Assert.Equal(0x89, bytes[offset]);
            Assert.Equal((byte)'P', bytes[offset + 1]);
        }
    }

    [Fact]
    public void 同梱のicoは今のアイコンの図から作ったもの()
    {
        // 図を変えて app.ico を作り直し忘れると、タスクバーとダッシュボードでアイコンが食い違う。
        using var tempFile = new TempFile("icon", ".ico");
        var path = tempFile.Path;

        AppIcon.WriteIco(path);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(Path.Combine(TestPaths.AppProject, "app.ico")));
    }

    [Fact]
    public void ウィンドウとタスクトレイ用のアイコンを作れる()
    {
        foreach (var size in new[] { 16, 20, 24, 32, 48 })
        {
            var handle = AppIcon.CreateHandle(size);
            Assert.NotEqual(0, handle);
            Assert.True(DestroyIcon(handle));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
