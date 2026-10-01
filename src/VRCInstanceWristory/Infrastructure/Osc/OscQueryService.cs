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

    /// <summary>知らせる記録すべて（2つのサービスの PTR・SRV・TXT と、ホストの A）。</summary>
    public byte[] Records(uint ttl) => DnsMessage.Response(AllRecords(ttl));

    /// <summary>問い合わせに自分の名前が含まれていれば、その答え。含まれていなければ null。</summary>
    public byte[]? Answer(IReadOnlyList<DnsQuestion> questions, uint ttl)
    {
        var records = new List<DnsRecord>();

        foreach (var q in questions)
        {
            var any = q.Type == DnsRecord.Any;

            if ((q.Type == DnsRecord.Ptr || any) && Same(q.Name, QueryServiceType))
                records.AddRange(ServiceRecords(QueryServiceType, QueryInstance, queryPort, ttl));
            else if ((q.Type == DnsRecord.Ptr || any) && Same(q.Name, OscServiceType))
                records.AddRange(ServiceRecords(OscServiceType, OscInstance, oscPort, ttl));
            else if (Same(q.Name, QueryInstance))
                records.AddRange(ServiceRecords(QueryServiceType, QueryInstance, queryPort, ttl).Skip(1));
            else if (Same(q.Name, OscInstance))
                records.AddRange(ServiceRecords(OscServiceType, OscInstance, oscPort, ttl).Skip(1));
            else if ((q.Type == DnsRecord.A || any) && Same(q.Name, HostName))
                records.Add(DnsRecord.Address(HostName, IPAddress.Loopback, ttl));
        }

        if (records.Count == 0)
            return null;

        // 同じ記録を2度入れない（A は両方のサービスに付く）。
        return DnsMessage.Response([.. records.DistinctBy(r => (r.Name.ToLowerInvariant(), r.Type))]);
    }

    private List<DnsRecord> AllRecords(uint ttl)
    {
        var records = new List<DnsRecord>();
        records.AddRange(ServiceRecords(QueryServiceType, QueryInstance, queryPort, ttl));
        records.AddRange(ServiceRecords(OscServiceType, OscInstance, oscPort, ttl));
        return [.. records.DistinctBy(r => (r.Name.ToLowerInvariant(), r.Type))];
    }

    private IEnumerable<DnsRecord> ServiceRecords(string type, string instance, int port, uint ttl)
    {
        yield return DnsRecord.Pointer(type, instance, ttl);
        yield return DnsRecord.Service(instance, HostName, port, ttl);
        yield return DnsRecord.Text(instance, "txtvers=1", ttl);
        yield return DnsRecord.Address(HostName, IPAddress.Loopback, ttl);
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('.'), b, StringComparison.OrdinalIgnoreCase);
}
