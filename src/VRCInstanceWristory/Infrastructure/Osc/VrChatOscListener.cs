using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace VRCInstanceWristory.Infrastructure.Osc;

/// <summary>
/// VRChat の OSC から AFK の状態を受け取る（2026-09-29のユーザー指定→実装メモ5.89）。
///
/// VRChat は OSC を有効にしていると、アバターのパラメーター（組み込みの <c>AFK</c> を含む）を OSC で送る。
/// 送り先は既定では UDP 9001 だが、ほかの OSC アプリと取り合いになるので、OSCQuery で受け口を知らせてもらう方式にする
/// （VRChat が推奨している方式。VRCMicOverlay なども同じ）。
///
/// | 部品 | 役目 |
/// | --- | --- |
/// | OSC（UDP・127.0.0.1 の空いているポート） | VRChat から <c>/avatar/parameters/AFK</c> を受け取る |
/// | OSCQuery（HTTP・127.0.0.1 の空いているポート） | 「どのアドレスが欲しいか」（<c>/avatar/parameters/AFK</c>）と OSC のポートを JSON で答える |
/// | mDNS（UDP 5353） | <c>_oscjson._tcp</c> と <c>_osc._udp</c> のサービスとして、上の2つのポートを知らせる |
///
/// VRChat は mDNS で OSCQuery のサービスを探し、見つけたサービスへ、そのサービスが欲しいと答えたアドレスを送る。
/// ライブラリ（VRChat.OSCQuery）は使わず、必要な分だけを書いた（依存を増やさないため）。
/// </summary>
public sealed class VrChatOscListener : IDisposable
{
    /// <summary>VRChat の組み込みのアバターパラメーター AFK（ヘッドセットを外した・End キーで AFK にした）。</summary>
    public const string AfkAddress = "/avatar/parameters/AFK";

    private readonly IDiagnostics _log;
    private readonly bool _advertise;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _threads = [];

    private Socket? _osc;
    private TcpListener? _query;
    private Socket? _mdns;
    private OscQueryService? _service;
    private volatile bool _afk;
    private bool _started;

    /// <param name="advertise">
    /// mDNS でサービスを知らせるか。自動検証では false にする（本物の VRChat に見つけられて、検証の受け口へ送られないように）。
    /// </param>
    public VrChatOscListener(IDiagnostics log, bool advertise = true)
    {
        _log = log;
        _advertise = advertise;
        _instanceName = $"{AppInfo.InternalName}-{Environment.ProcessId}";
    }

    /// <summary>mDNS・OSCQuery で名乗る名前。</summary>
    private readonly string _instanceName;

    /// <summary>VRChat が AFK を知らせているか。</summary>
    public bool Afk => _afk;

    /// <summary>OSC を受けているポート（始めていなければ 0）。</summary>
    public int OscPort { get; private set; }

    /// <summary>OSCQuery の HTTP のポート（始めていなければ 0）。</summary>
    public int QueryPort { get; private set; }

    /// <summary>mDNS でサービスを知らせているか（5353 を開けなかったときは false）。</summary>
    public bool Advertising { get; private set; }

    /// <summary>受け口を開く。開けなければ false（AFK は分からないまま）。</summary>
    public bool Start()
    {
        if (_started)
            return OscPort != 0;

        _started = true;

        try
        {
            _osc = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _osc.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            OscPort = ((IPEndPoint)_osc.LocalEndPoint!).Port;

            _query = new TcpListener(IPAddress.Loopback, 0);
            _query.Start();
            QueryPort = ((IPEndPoint)_query.LocalEndpoint).Port;
        }
        catch (SocketException ex)
        {
            _log.Warn($"OSC の受け口を開けません（AFK の状態は分かりません）: {ex.Message}");
            DisposeSockets();
            OscPort = 0;
            QueryPort = 0;
            return false;
        }

        _service = new OscQueryService(_instanceName, OscPort, QueryPort);

        StartThread("OSC", ReceiveOsc);
        StartThread("OSCQuery", ServeQuery);

        if (_advertise)
            Advertising = StartMdns();

        _log.Info($"OSC の受け口を開きました: OSC 127.0.0.1:{OscPort} / OSCQuery http://127.0.0.1:{QueryPort}/{(Advertising ? " / mDNS で公開中" : string.Empty)}");
        return true;
    }

    /// <summary>VRChat が終わったときなど、覚えている AFK の状態を忘れる。</summary>
    public void ResetState() => _afk = false;

    private void StartThread(string name, Action body)
    {
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException or InvalidOperationException)
            {
                // 閉じたとき。
            }
            catch (Exception ex)
            {
                _log.Warn($"{name} の受け口が止まりました: {ex.Message}");
            }
        })
        {
            IsBackground = true,
            Name = $"{AppInfo.InternalName} {name}",
        };

        _threads.Add(thread);
        thread.Start();
    }

    // ------------------------------------------------------------------ OSC

    private void ReceiveOsc()
    {
        var buffer = new byte[65536];

        while (!_stop.IsCancellationRequested && _osc is { } socket)
        {
            int length;

            try
            {
                length = socket.Receive(buffer);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue;
            }

            Handle(buffer.AsSpan(0, length));
        }
    }

    /// <summary>受け取った OSC のパケットを1つ処理する（受け口のスレッドから。自動検証からも直接渡す）。</summary>
    internal void Handle(ReadOnlySpan<byte> packet)
    {
        foreach (var (address, value) in OscPacket.Parse(packet))
        {
            if (!string.Equals(address, AfkAddress, StringComparison.Ordinal) || OscPacket.AsBool(value) is not { } afk)
                continue;

            if (_afk != afk)
                _log.Info(afk ? "VRChat が AFK になりました（OSC）。" : "VRChat の AFK が解除されました（OSC）。");

            _afk = afk;
        }
    }

    // ------------------------------------------------------------------ OSCQuery（HTTP）

    private void ServeQuery()
    {
        while (!_stop.IsCancellationRequested && _query is { } listener)
        {
            var client = listener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(_ => Respond(client));
        }
    }

    private void Respond(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                using var stream = client.GetStream();

                var request = ReadRequestHead(stream);
                var (status, body) = _service!.Answer(request);
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");

                stream.Write(head);
                stream.Write(bytes);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // 相手が先に切った。
        }
    }

    private static string ReadRequestHead(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);

            if (read <= 0)
                break;

            total += read;

            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }

        var text = Encoding.ASCII.GetString(buffer, 0, total);
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    // ------------------------------------------------------------------ mDNS

    private static readonly IPAddress MdnsGroup = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    private bool StartMdns()
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            var joined = 0;

            foreach (var address in LocalAddresses())
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MdnsGroup, address));
                    joined++;
                }
                catch (SocketException)
                {
                    // その口では入れない（無効な口など）。
                }
            }

            if (joined == 0)
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MdnsGroup));

            _mdns = socket;
        }
        catch (SocketException ex)
        {
            _log.Warn($"mDNS（UDP 5353）を開けません。VRChat に OSC の受け口を知らせられないので、AFK の状態は分かりません: {ex.Message}");
            return false;
        }

        StartThread("mDNS", ReceiveMdns);
        StartThread("mDNS announce", Announce);
        return true;
    }

    /// <summary>起動した直後に3回、その後は1分ごとに知らせる（VRChat は起動時と定期的に探しに来るが、先に知らせておく）。</summary>
    private void Announce()
    {
        var delays = new[] { 0, 1000, 2000 };

        foreach (var delay in delays)
        {
            if (_stop.Token.WaitHandle.WaitOne(delay))
                return;

            SendMulticast(_service!.Records(ttl: 120));
        }

        while (!_stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(60)))
            SendMulticast(_service!.Records(ttl: 120));
    }

    private void ReceiveMdns()
    {
        var buffer = new byte[9000];

        while (!_stop.IsCancellationRequested && _mdns is { } socket)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int length;

            try
            {
                length = socket.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                continue;
            }

            var questions = DnsMessage.ParseQuestions(buffer.AsSpan(0, length));

            if (questions is null || questions.Count == 0)
                continue;

            var answer = _service!.Answer(questions, ttl: 120);

            if (answer is null)
                continue;

            SendMulticast(answer);

            // 5353 以外から聞かれた（一度きりの問い合わせ）・ユニキャストで答えてほしいと言われたときは、聞いた相手へも直接返す。
            if (from is IPEndPoint source && (source.Port != MdnsPort || questions.Any(q => q.UnicastResponse)))
            {
                try
                {
                    socket.SendTo(answer, source);
                }
                catch (SocketException)
                {
                    // 相手がもういない。マルチキャストでは答えてある。
                }
            }
        }
    }

    private void SendMulticast(byte[] message)
    {
        if (_mdns is not { } socket)
            return;

        var target = new IPEndPoint(MdnsGroup, MdnsPort);
        var sent = false;

        foreach (var address in LocalAddresses())
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.SendTo(message, target);
                sent = true;
            }
            catch (SocketException)
            {
                // その口からは送れない（切れている口など）。ほかの口で送る。
            }
        }

        if (!sent)
        {
            try
            {
                socket.SendTo(message, target);
            }
            catch (SocketException)
            {
                // 送れなくても、VRChat が探しに来たときに答える（ReceiveMdns）。次の知らせでも試す。
            }
        }
    }

    /// <summary>このPCの IPv4 の口（ループバックを先頭に。VRChat は同じPCにいる）。</summary>
    private static List<IPAddress> LocalAddresses()
    {
        var addresses = new List<IPAddress> { IPAddress.Loopback };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast)
                    continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                        addresses.Add(unicast.Address);
                }
            }
        }
        catch (NetworkInformationException)
        {
            // 口の一覧を取れなければ、ループバックだけで知らせる（VRChat は同じPCにいる）。
        }

        return addresses;
    }

    // ------------------------------------------------------------------ 片付け

    private void DisposeSockets()
    {
        _osc?.Dispose();
        _query?.Stop();
        _mdns?.Dispose();
        _osc = null;
        _query = null;
        _mdns = null;
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
            return;

        // いなくなることを知らせる（TTL 0）。知らせなくても2分で忘れられる。
        if (_mdns is not null && _service is not null)
            SendMulticast(_service.Records(ttl: 0));

        _stop.Cancel();
        DisposeSockets();

        foreach (var thread in _threads)
            thread.Join(TimeSpan.FromMilliseconds(500));

        _stop.Dispose();
    }
}
