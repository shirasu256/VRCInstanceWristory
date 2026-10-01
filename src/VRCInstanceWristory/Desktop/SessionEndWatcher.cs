using System.Runtime.InteropServices;
using VRCInstanceWristory.Infrastructure;
using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// Windowsのシャットダウン・再起動・サインアウトを受け取り、保存を終えてから終わらせる（→実装メモ5.59）。
///
/// 何もしないと、Windowsは <c>WM_ENDSESSION</c> を返した直後にこのプロセスを強制終了する。主ループは抜けず、
/// 終わり際の保存（落ち着く前の設定・チェックポイント）が行われない。
/// そこで <c>WM_ENDSESSION</c> を受けたら主ループへ終了を頼み、保存が済むまで（長くて <see cref="SaveTimeout"/>）返さない。
///
/// コンソールの <c>CTRL_SHUTDOWN_EVENT</c> は、user32.dll を読み込んだプロセス（ウィンドウ・D3D11を使うこのアプリ）には
/// 届かないので使えない。デスクトップのウィンドウを出さない設定でも受け取れるよう、見えない最上位のウィンドウを
/// 専用のスレッドで持つ（メッセージ専用のウィンドウ <c>HWND_MESSAGE</c> にはこの通知が来ない）。
/// デスクトップのウィンドウも同じ通知を受けるので、そちらからも <see cref="OnEndSession"/> を呼んで待たせる。
/// </summary>
public sealed class SessionEndWatcher : IDisposable
{
    /// <summary>保存を待つ上限。Windowsはおよそ5秒で「終了を妨げているアプリ」の画面を出すので、その手前にする。</summary>
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(4);

    private const string ClassName = "VRCInstanceWristory.SessionEnd";

    private readonly IDiagnostics _log;
    private readonly ManualResetEventSlim _saved = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly WndProc _wndProc;
    private Thread? _thread;
    private nint _hwnd;
    private volatile bool _requested;

    // _gate で守るもの。片付けたか・保存を待っているスレッドの数（待っている間は _saved を手放さない）。
    private readonly Lock _gate = new();
    private bool _disposed;
    private int _waiters;

    private SessionEndWatcher(IDiagnostics log)
    {
        _log = log;
        _wndProc = WindowProc;
    }

    /// <summary>Windowsが終わろうとしている。主ループはこれを見て抜け、保存してから片付ける。</summary>
    public bool Requested => _requested;

    /// <summary>見えないウィンドウを作って受け取り始める。作れなくても（通知を受け取れないだけで）アプリは続ける。</summary>
    public static SessionEndWatcher Start(IDiagnostics log)
    {
        var watcher = new SessionEndWatcher(log);

        watcher._thread = new Thread(watcher.Run)
        {
            Name = "SessionEndWatcher",
            IsBackground = true,
        };

        watcher._thread.Start();

        if (!watcher._started.Wait(TimeSpan.FromSeconds(5)) || watcher._hwnd == 0)
            log.Warn("Windowsの終了の通知を受け取る準備ができませんでした。シャットダウンの直前の変更は保存されないことがあります。");

        return watcher;
    }

    /// <summary>
    /// <c>WM_ENDSESSION</c> を受けたウィンドウのスレッドから呼ぶ。本当に終わるときは、主ループに終了を頼んで保存を待つ。
    /// </summary>
    public void OnEndSession(bool ending)
    {
        if (!ending)
            return;

        _requested = true;

        lock (_gate)
        {
            // もう片付けた（保存は済んでいる）。
            if (_disposed)
                return;

            _waiters++;
        }

        try
        {
            if (!_saved.Wait(SaveTimeout))
                _log.Warn("Windowsの終了までに保存が終わりませんでした。");
        }
        finally
        {
            lock (_gate)
            {
                _waiters--;

                // 片付けのあとに最後に待ち終えたスレッドが、合図を手放す。
                if (_disposed && _waiters == 0)
                    _saved.Dispose();
            }
        }
    }

    private void Run()
    {
        var instance = GetModuleHandleW(null);
        var registered = false;

        try
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = instance,
                lpszClassName = ClassName,
            };

            registered = RegisterClassExW(ref wc) != 0;

            if (!registered)
                return;

            // 見せない最上位のウィンドウ（ShowWindow しない）。シャットダウンの通知は最上位のウィンドウすべてに届く。
            _hwnd = CreateWindowExW(0, ClassName, ClassName, 0, 0, 0, 0, 0, 0, 0, instance, 0);

            if (_hwnd == 0)
                return;

            _started.Set();

            while (GetMessageW(out var msg, 0, 0, 0) is not (0 or -1))
            {
                TranslateMessage(msg);
                DispatchMessageW(msg);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Windowsの終了の通知でエラー: {ex}");
        }
        finally
        {
            // 例外で抜けたときはウィンドウが残っているので、壊してからクラスの登録を外す。
            if (_hwnd != 0 && IsWindow(_hwnd))
                DestroyWindow(_hwnd);

            if (registered)
                UnregisterClassW(ClassName, instance);

            _started.Set();
        }
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case WM_QUERYENDSESSION:
                    // 終了を止めない。保存は WM_ENDSESSION で行う。
                    return 1;

                case WM_ENDSESSION:
                    OnEndSession(wParam != 0);
                    return 0;

                case WM_CLOSE:
                    DestroyWindow(hwnd);
                    return 0;

                case WM_DESTROY:
                    PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Windowsの終了の通知でエラー: {ex}");
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    /// <summary>保存を終えた後に呼ぶ（主ループの終わりで、履歴エンジンより後に片付くよう置く）。待っている終了の通知を返す。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _saved.Set();
        }

        if (_hwnd != 0)
            PostMessageW(_hwnd, WM_CLOSE, 0, 0);

        // 見えないウィンドウのスレッドが終わっていれば、始まりの合図はもう誰も使わない。
        if (_thread is null || _thread.Join(TimeSpan.FromSeconds(1)))
            _started.Dispose();

        lock (_gate)
        {
            // 待っているスレッドがあれば、最後に待ち終えたスレッドが手放す（OnEndSession）。
            if (_waiters == 0)
                _saved.Dispose();
        }
    }
}
