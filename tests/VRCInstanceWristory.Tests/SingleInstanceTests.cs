using VRCInstanceWristory.Desktop;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 二重起動を防ぐ（2026-09-26のユーザー指定→実装メモ5.52）。
/// 本物の Mutex の名前は使わない（検証の最中に動いているアプリがいても、それに触れない）。
/// </summary>
public class SingleInstanceTests
{
    [Fact]
    public void 起動中かどうかは名前付きのMutexで分かる()
    {
        // 本物の名前は使わない（検証の最中に動いているアプリがいても、それに触れない）。
        var name = $@"Local\VRCInstanceWristory.Tests.{Guid.NewGuid():N}";

        Assert.False(SingleInstance.IsRunning(name));

        using (new Mutex(initiallyOwned: true, name, out var createdNew))
        {
            Assert.True(createdNew);
            Assert.True(SingleInstance.IsRunning(name));
        }

        Assert.False(SingleInstance.IsRunning(name));
    }

    [Fact]
    public void ウィンドウクラスの名前と合図の名前は2つの起動で同じもの()
    {
        // 2つ目の起動は、この名前で1つ目のウィンドウを探し、この合図を送る。
        Assert.Equal("VRCInstanceWristory.DesktopWindow", DesktopWindow.ClassName);
        Assert.Equal("VRCInstanceWristory.Activate", SingleInstance.ActivateMessageName);
        Assert.NotEqual(SingleInstance.ShowWindowRequest, SingleInstance.FromSteamVrRequest);
    }
}
