using VRCInstanceWristory.Desktop;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// Windowsの再起動・シャットダウンへの備え（→実装メモ5.59）。Windowsの終了の通知を受けたら、保存が済むまで終了を待たせる。
///
/// 書いている途中で落ちても途中で切れたファイルを残さないことは <see cref="AtomicFileTests"/> で確かめる。
/// サインインの時刻から前回の終わりを絞る仕組みは、回数の60分の引き継ぎをやめたので外した（→実装メモ5.110）。
/// </summary>
public class WindowsEndTests
{
    // ------------------------------------------------------------------ Windowsの終了の通知

    [Fact]
    public void 終了の通知は保存が済むまで返さない()
    {
        var watcher = SessionEndWatcher.Start(new CollectingDiagnostics());
        // スレッドが後から触ることがあるので、閉じずに残す。
        var returned = new ManualResetEventSlim(false);

        // ウィンドウのスレッドが WM_ENDSESSION を受けた代わり。
        // 途中の確かめで落ちても検証の実行が終われるよう、裏のスレッドにする。
        var thread = new Thread(() =>
        {
            watcher.OnEndSession(ending: true);
            returned.Set();
        })
        {
            IsBackground = true,
        };

        try
        {
            thread.Start();

            // 主ループが終了の頼みに気づくまで。
            Assert.True(Eventually.True(() => watcher.Requested));

            // 保存が済むまでは返さない。
            Assert.False(returned.Wait(TimeSpan.FromMilliseconds(300)));

            // 主ループが履歴エンジンを片付けたあとに、この受け手が片付く。
            watcher.Dispose();
            Assert.True(returned.Wait(Eventually.DefaultTimeout));
        }
        finally
        {
            // 途中で落ちても、待たせたままのスレッドを残さない（2回目の Dispose は何もしない）。
            watcher.Dispose();
            thread.Join(Eventually.DefaultTimeout);
        }
    }

    [Fact]
    public void 終了が取り消された通知では何もしない()
    {
        using var watcher = SessionEndWatcher.Start(new CollectingDiagnostics());

        // WM_ENDSESSION の wParam が FALSE（ほかのアプリが終了を止めた）。
        watcher.OnEndSession(ending: false);

        Assert.False(watcher.Requested);
    }
}
