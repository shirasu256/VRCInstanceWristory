using System.Net;
using System.Text.Json.Nodes;

namespace VRCInstanceWristory.Infrastructure.Osc;

/// <summary>
/// OSCQuery のサービス（→実装メモ5.89）。HTTP の答えと、mDNS の記録を作る。
/// </summary>
public sealed class OscQueryService(string instanceName, int oscPort, int queryPort)
{
    public const string QueryServiceType = "_oscjson._tcp.local";
    public const string OscServiceType = "_osc._udp.local";

    public string InstanceName => instanceName;

    public string HostName => $"{instanceName}.local";

    public string QueryInstance => $"{instanceName}.{QueryServiceType}";

    public string OscInstance => $"{instanceName}.{OscServiceType}";

    // ---------------------------------------------------------------- HTTP

    /// <summary>リクエストの1行目（<c>GET /?HOST_INFO HTTP/1.1</c>）への答え（状態と JSON）。</summary>
    public (string Status, string Body) Answer(string requestLine)
    {
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
            return ("405 Method Not Allowed", "{}");

        var target = Uri.UnescapeDataString(parts[1]);

        if (target.Contains("?HOST_INFO", StringComparison.OrdinalIgnoreCase))
            return ("200 OK", HostInfo().ToJsonString());

        var path = target.Split('?')[0];

        if (path.Length > 1)
            path = path.TrimEnd('/');

        return Node(path) is { } node ? ("200 OK", node.ToJsonString()) : ("404 Not Found", "{}");
    }

    public JsonObject HostInfo() => new()
    {
        ["NAME"] = instanceName,
        ["OSC_IP"] = "127.0.0.1",
        ["OSC_PORT"] = oscPort,
        ["OSC_TRANSPORT"] = "UDP",
        ["EXTENSIONS"] = new JsonObject
        {
            ["ACCESS"] = true,
            ["CLIPMODE"] = false,
            ["RANGE"] = false,
            ["TYPE"] = true,
            ["VALUE"] = false,
        },
    };

    /// <summary>アドレスの木のうち、<paramref name="path"/> の節。受け取りたいのは AFK だけ。</summary>
    public static JsonObject? Node(string path)
    {
        var afk = new JsonObject
        {
            ["FULL_PATH"] = VrChatOscListener.AfkAddress,
            ["ACCESS"] = 2, // 書き込みだけ（VRChat から受け取る）
            ["TYPE"] = "T",
            ["DESCRIPTION"] = "VRChat の AFK の状態",
        };

        JsonObject Container(string fullPath, string key, JsonObject child) => new()
        {
            ["FULL_PATH"] = fullPath,
            ["ACCESS"] = 0,
            ["CONTENTS"] = new JsonObject { [key] = child },
        };

        var parameters = Container("/avatar/parameters", "AFK", afk);

        return path switch
        {
            "/" or "" => Container("/", "avatar", Container("/avatar", "parameters", parameters)),
            "/avatar" => Container("/avatar", "parameters", parameters),
            "/avatar/parameters" => parameters,
            VrChatOscListener.AfkAddress => afk,
            _ => null,
        };
    }

    // ---------------------------------------------------------------- mDNS

    /// <summary>知らせる記録すべて（2つのサービスの PTR を答えの欄に、SRV・TXT とホストの A を追加の欄に→実装メモ5.124）。</summary>
    public byte[] Records(uint ttl) => Respond([QueryServiceType, OscServiceType], hostAsked: false, ttl);

    /// <summary>問い合わせに自分の名前が含まれていれば、その答え。含まれていなければ null。</summary>
    public byte[]? Answer(IReadOnlyList<DnsQuestion> questions, uint ttl)
    {
        var services = new List<string>();
        var hostAsked = false;

        foreach (var q in questions)
        {
            var any = q.Type == DnsRecord.Any;

            // サービスの種類・サービスの名前のどちらを聞かれても、そのサービスの PTR・SRV・TXT・A をまとめて返す
            // （古い vrc-oscquery-lib は、答えの欄にサービスの種類の PTR がないと読まない）。
            if (((q.Type == DnsRecord.Ptr || any) && Same(q.Name, QueryServiceType)) || Same(q.Name, QueryInstance))
                services.Add(QueryServiceType);
            else if (((q.Type == DnsRecord.Ptr || any) && Same(q.Name, OscServiceType)) || Same(q.Name, OscInstance))
                services.Add(OscServiceType);
            else if ((q.Type == DnsRecord.A || any) && Same(q.Name, HostName))
                hostAsked = true;
        }

        if (services.Count == 0 && !hostAsked)
            return null;

        return Respond([.. services.Distinct()], hostAsked, ttl);
    }

    /// <summary><paramref name="services"/> の PTR を答えの欄に、SRV・TXT と A を追加の欄に並べた応答。A だけを聞かれたら A を答えの欄に入れる。</summary>
    private byte[] Respond(IReadOnlyList<string> services, bool hostAsked, uint ttl)
    {
        var answers = new List<DnsRecord>();
        var additional = new List<DnsRecord>();

        foreach (var type in services)
        {
            var (instance, port) = type == QueryServiceType ? (QueryInstance, queryPort) : (OscInstance, oscPort);
            answers.Add(DnsRecord.Pointer(type, instance, ttl));
            additional.Add(DnsRecord.Service(instance, HostName, port, ttl));
            additional.Add(DnsRecord.Text(instance, "txtvers=1", ttl));
        }

        var address = DnsRecord.Address(HostName, IPAddress.Loopback, ttl);

        if (hostAsked && services.Count == 0)
            answers.Add(address);
        else
            additional.Add(address);

        return DnsMessage.Response(answers, additional);
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('.'), b, StringComparison.OrdinalIgnoreCase);
}
