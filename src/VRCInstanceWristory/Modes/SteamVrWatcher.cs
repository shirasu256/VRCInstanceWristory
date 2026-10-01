using System.Diagnostics;
using VRCInstanceWristory.Core;

namespace VRCInstanceWristory.Modes;

/// <summary>SteamVR のサーバー（<c>vrserver.exe</c>）の1回の起動。</summary>
public readonly record struct VrServerProcess(int Pid, DateTime StartUtc);

/// <summary>
/// SteamVRにつながっていない間、SteamVR が起動したかを見張る（2026-09-26のユーザー指定→実装メモ5.51）。
///
/// ログオン時にタスクトレイへ入れて起動しておくと、そのあとで SteamVR を起動しても、それまでは
/// ウィンドウの「SteamVRへ接続」を押さない限りつながらなかった。そこで、SteamVR のサーバーが見えたら自分からつなぐ
/// （「SteamVRへ接続」のボタンは 2026-09-27 のユーザー指定で外した→実装メモ5.65）。
///
/// SteamVR が動いていないときに OpenVR を初期化すると SteamVR そのものが起動してしまうので、
/// <b>サーバーが動いているのを確かめてからだけ</b>つなぐ。一度つないだサーバー（＝終了を知らせてきたもの）には
/// つなぎ直さない。SteamVR を終了すると、サーバーは少しのあいだ残っていることがあるため。
/// つなげなかったときは、同じサーバーにも少し間を置いて試し直す（起動の途中でまだ受け付けないことがある）。
///
/// SteamVR の画面を合成するコンポジター（<c>vrcompositor.exe</c>）も見る（2026-09-29のユーザー指摘→実装メモ5.88）。
/// GPU の資源が尽きているとコンポジターは起動に失敗して終わるが、サーバーは残り続ける。サーバーだけを見ていると、
/// つないだあとにコンポジターが落ちても「正常動作中」のままになっていた。コンポジターがいない間はつなぎに行かない
/// （オーバーレイはコンポジターが受け持つので、つないでも何も出せない）。
/// </summary>
/// <param name="findServer">いま動いている SteamVR のサーバー（通常は <see cref="FindServer"/>）。</param>
/// <param name="findCompositor">コンポジターが動いているか（通常は <see cref="FindCompositor"/>）。</param>
public sealed class SteamVrWatcher(Func<VrServerProcess?> findServer, Func<bool> findCompositor, IClock? clock = null)
{
    /// <summary>サーバーを探す間隔。</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);

    /// <summary>つなげなかったとき、同じサーバーに試し直すまでの間。</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// サーバーが起動してから、コンポジターがいないのを「停止」と見なすまでの時間。
    /// SteamVR の起動の途中は、サーバーが先に立ち上がってからコンポジターが起動する（実機では1〜2秒）。
    /// </summary>
    public static readonly TimeSpan CompositorStartGrace = TimeSpan.FromSeconds(30);

    /// <summary>つないでいる間、コンポジターが見えないことがこれだけ続いたら、落ちたと見なす。SteamVR の終了の途中の一瞬を拾わないため。</summary>
    public static readonly TimeSpan CompositorLostDelay = TimeSpan.FromSeconds(5);

    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly Func<bool> _findCompositor = findCompositor;

    private bool _compositorSeen = true;
    private DateTime _nextCompositorCheck = DateTime.MinValue;
    private DateTime? _compositorMissingSince;
    private bool _compositorLost;

    private DateTime _nextCheck = DateTime.MinValue;
    private VrServerProcess? _connectedTo;
    private VrServerProcess? _failedOn;
    private DateTime _retryAt = DateTime.MinValue;
    private VrServerProcess? _candidate;

    /// <summary>直近に探したとき、SteamVR のサーバーが動いていたか（→実装メモ5.82）。</summary>
    public bool ServerSeen => _candidate is not null;

    /// <summary>直近に見つけた SteamVR のサーバーのプロセス番号（権限の違いを調べるのに使う→実装メモ5.85）。</summary>
    public int? ServerPid => _candidate?.Pid;

    /// <summary>
    /// SteamVR が終了を知らせてきたあと、そのサーバーがまだ残っているか（→実装メモ5.85）。
    /// この間は「接続待機中」ではなく「終了中」と出す（つなぎ直さないので、待っても何も起きない）。
    /// </summary>
    public bool WaitingForExit => _candidate is { } server && server == _connectedTo;

    /// <summary>
    /// サーバーは動いているのに、コンポジターがいない（起動に失敗した・落ちた→実装メモ5.88）。
    /// サーバーが起動したばかりのうち（<see cref="CompositorStartGrace"/>）は、起動の途中なので出さない。
    /// ただし、つないでいる間に落ちたと分かったとき（<see cref="CompositorLost"/>）は待たない（実例では起動から6秒で落ちた）。
    /// </summary>
    public bool CompositorDown
        => _candidate is { } server && !_compositorSeen
           && (_compositorLost || _clock.UtcNow - server.StartUtc >= CompositorStartGrace);

    /// <summary>
    /// つないでいる間に呼ぶ。コンポジターが見えないことが <see cref="CompositorLostDelay"/> 続いたら true（→実装メモ5.88）。
    /// 見張りの間隔ごとに確かめる。true を返したら、呼び出し側は切る（コンポジターと一緒にオーバーレイも消えている）。
    /// </summary>
    public bool CompositorLost()
    {
        var now = _clock.UtcNow;

        if (now < _nextCompositorCheck)
            return false;

        _nextCompositorCheck = now + CheckInterval;
        _compositorSeen = _findCompositor();
        _compositorLost &= !_compositorSeen;

        if (_compositorSeen)
        {
            _compositorMissingSince = null;
            return false;
        }

        _compositorMissingSince ??= now;
        _compositorLost = now - _compositorMissingSince.Value >= CompositorLostDelay;
        return _compositorLost;
    }

    /// <summary>いまつなぎに行ってよいか。見張りの間隔ごとにサーバーを探す。</summary>
    public bool ShouldConnect()
    {
        var now = _clock.UtcNow;

        if (now < _nextCheck)
            return false;

        _nextCheck = now + CheckInterval;
        _candidate = findServer();

        if (_candidate is not { } server)
            return false;

        // コンポジターがいなければ、つないでも何も出せない（→実装メモ5.88）。
        _compositorSeen = _findCompositor();
        _compositorLost &= !_compositorSeen;

        if (!_compositorSeen)
            return false;

        // 前につないで終わったサーバーには戻らない（終了の途中で残っているだけかもしれない）。
        if (server == _connectedTo)
            return false;

        if (server == _failedOn && now < _retryAt)
            return false;

        return true;
    }

    /// <summary>
    /// アプリを起動した時点でつなぎに行ってよいか（2026-09-27のユーザー指定→実装メモ5.57）。
    ///
    /// SteamVR のサーバーが動いているときだけ true。動いていなければ、このアプリを開いただけで SteamVR まで
    /// 立ち上げないよう、つながずに待つ（あとは <see cref="ShouldConnect"/> の見張りがつなぐ）。
    /// 見つけたサーバーは覚えておき、つなげなかったとき（<see cref="Failed"/>）は間を置いてから試し直す。
    /// </summary>
    public bool ShouldConnectAtStartup()
    {
        _nextCheck = _clock.UtcNow + CheckInterval;
        _candidate = findServer();

        if (_candidate is null)
            return false;

        // コンポジターがまだ・もういないときも、つながずに待つ（→実装メモ5.88）。
        _compositorSeen = _findCompositor();
        _compositorLost &= !_compositorSeen;
        return _compositorSeen;
    }

    /// <summary>
    /// つないだ。いまのサーバーを覚えて、同じサーバーへつなぎ直さないようにする。
    /// 起動した時点でつないだときは見張りがまだ探していないので、ここで探し直す。
    /// </summary>
    public void Connected()
    {
        _connectedTo = findServer() ?? _candidate;
        _failedOn = null;
        _compositorSeen = true;
        _compositorLost = false;
        _compositorMissingSince = null;
        _nextCompositorCheck = _clock.UtcNow + CheckInterval;
    }

    /// <summary>
    /// つないでいたのに、こちらから切った（GPU のリセットで D3D11 デバイスが失われた→実装メモ5.85、コンポジターが落ちた→5.88）。
    /// SteamVR は動き続けているので、同じサーバーへも間を置いてつなぎ直す。
    /// </summary>
    public void Disconnected()
    {
        _connectedTo = null;
        Failed();
    }

    /// <summary>つなげなかった。少し間を置いてから試し直す。</summary>
    public void Failed()
    {
        _failedOn = _candidate;
        _retryAt = _clock.UtcNow + RetryInterval;
    }

    /// <summary>次の <see cref="ShouldConnect"/> ですぐに確かめる（2つ目の起動があったときなど→実装メモ5.52）。</summary>
    public void Poke()
    {
        _nextCheck = DateTime.MinValue;
        _retryAt = DateTime.MinValue;
    }

    /// <summary>SteamVR のコンポジター（<c>vrcompositor.exe</c>）が動いているか。確かめられなかったときは動いている扱い。</summary>
    public static bool FindCompositor()
    {
        try
        {
            var processes = Process.GetProcessesByName("vrcompositor");

            foreach (var process in processes)
                process.Dispose();

            return processes.Length > 0;
        }
        catch (Exception)
        {
            // 一覧を取れないのは一時的なことが多い。「いない」とすると、つないだままのSteamVRから切ってしまうので、いる扱いにする。
            // 3秒ごとに呼ばれるので、記録もしない。
            return true;
        }
    }

    /// <summary>いま動いている SteamVR のサーバー。なければ null。</summary>
    public static VrServerProcess? FindServer()
    {
        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName("vrserver");
        }
        catch (Exception)
        {
            // 一覧を取れないときは「いない」として、つなぎに行かない（間違って SteamVR を起動させない）。次の見張りで試し直す。
            return null;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    return new VrServerProcess(process.Id, process.StartTime.ToUniversalTime());
                }
                catch (Exception)
                {
                    // 起動の途中や終了の途中で読めないことがある。次のものを見る。
                }
            }

            return null;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
