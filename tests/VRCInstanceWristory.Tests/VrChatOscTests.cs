using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Infrastructure.Osc;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// VRChat の AFK を OSC で受け取る仕組み（OSC のメッセージ・OSCQuery・mDNS→実装メモ5.89）。
///
/// 本物の VRChat に見つけられないよう、mDNS では知らせずに（<c>advertise: false</c>）確かめる。
/// </summary>
public class VrChatOscTests
{
    // ---------------------------------------------------------------- OSC

    [Fact]
    public void OSCのAFKのメッセージとバンドルを読む()
    {
        var on = OscTestPackets.Message(VrChatOscListener.AfkAddress, true);
        Assert.Equal((VrChatOscListener.AfkAddress, (object?)true), Assert.Single(OscPacket.Parse(on)));

        // バンドル（#bundle・タイムタグ・長さつきの要素）。
        var off = OscTestPackets.Message(VrChatOscListener.AfkAddress, false);
        var other = OscTestPackets.Message("/avatar/parameters/Voice", true);
        var bundle = new List<byte>();
        bundle.AddRange(Encoding.ASCII.GetBytes("#bundle\0"));
        bundle.AddRange(new byte[8]);

        foreach (var element in new[] { other, off })
        {
            bundle.AddRange([0, 0, (byte)(element.Length >> 8), (byte)element.Length]);
            bundle.AddRange(element);
        }

        var messages = OscPacket.Parse(bundle.ToArray());
        Assert.Equal(2, messages.Count);
        Assert.Equal(false, messages[1].Value);

        // 壊れたもの・知らないものは捨てる。
        Assert.Empty(OscPacket.Parse([1, 2, 3]));
        Assert.Empty(OscPacket.Parse(Encoding.ASCII.GetBytes("abcd\0\0\0\0")));
    }

    [Fact]
    public void 受け口はAFKのアドレスだけを見る()
    {
        using var listener = new VrChatOscListener(NullDiagnostics.Instance, advertise: false);

        listener.Handle(OscTestPackets.Message("/avatar/parameters/Voice", true));
        Assert.False(listener.Afk);

        listener.Handle(OscTestPackets.Message(VrChatOscListener.AfkAddress, true));
        Assert.True(listener.Afk);

        listener.Handle(OscTestPackets.Message(VrChatOscListener.AfkAddress, false));
        Assert.False(listener.Afk);

        listener.Handle(OscTestPackets.Message(VrChatOscListener.AfkAddress, true));
        listener.ResetState();
        Assert.False(listener.Afk);
    }

    /// <summary>
    /// 本物の VRChat に見つけられないよう、mDNS では知らせずに、ループバックの UDP と HTTP だけで確かめる。
    /// </summary>
    [Fact]
    public async Task ループバックのUDPで受け取りHTTPでOSCQueryに答える()
    {
        using var listener = new VrChatOscListener(NullDiagnostics.Instance, advertise: false);
        Assert.True(listener.Start());
        Assert.False(listener.Advertising);
        Assert.NotEqual(0, listener.OscPort);
        Assert.NotEqual(0, listener.QueryPort);

        using (var udp = new UdpClient())
            udp.Send(OscTestPackets.Message(VrChatOscListener.AfkAddress, true), new IPEndPoint(IPAddress.Loopback, listener.OscPort));

        Assert.True(await Eventually.TrueAsync(() => listener.Afk));

        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{listener.QueryPort}/"),
            Timeout = TimeSpan.FromSeconds(10),
        };

        var hostInfo = JsonNode.Parse(await http.GetStringAsync("?HOST_INFO"))!;
        Assert.Equal(listener.OscPort, hostInfo["OSC_PORT"]!.GetValue<int>());
        Assert.Equal("UDP", hostInfo["OSC_TRANSPORT"]!.GetValue<string>());

        var root = JsonNode.Parse(await http.GetStringAsync("/"))!;
        Assert.Equal(VrChatOscListener.AfkAddress, root["CONTENTS"]!["avatar"]!["CONTENTS"]!["parameters"]!["CONTENTS"]!["AFK"]!["FULL_PATH"]!.GetValue<string>());
    }

    [Fact]
    public void OSCQueryは知らないアドレスには404で答える()
    {
        var service = new OscQueryService("Test", 9100, 9200);

        Assert.StartsWith("200", service.Answer("GET /?HOST_INFO HTTP/1.1").Status);
        Assert.StartsWith("200", service.Answer("GET /avatar/parameters/AFK HTTP/1.1").Status);
        Assert.StartsWith("200", service.Answer("GET /avatar/parameters/ HTTP/1.1").Status);
        Assert.StartsWith("404", service.Answer("GET /chatbox HTTP/1.1").Status);
        Assert.StartsWith("405", service.Answer("POST / HTTP/1.1").Status);
    }

    // ---------------------------------------------------------------- mDNS

    private static byte[] Query(string name, ushort type, bool unicast = false)
    {
        var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        bytes.AddRange(DnsMessage.EncodeName(name));
        bytes.AddRange([(byte)(type >> 8), (byte)type, (byte)(unicast ? 0x80 : 0), 1]);
        return [.. bytes];
    }

    [Fact]
    public void mDNSの問い合わせにOSCQueryとOSCのサービスで答える()
    {
        var service = new OscQueryService("VRCInstanceWristory-1", 9100, 9200);

        var questions = DnsMessage.ParseQuestions(Query("_oscjson._tcp.local", DnsRecord.Ptr, unicast: true))!;
        var question = Assert.Single(questions);
        Assert.Equal("_oscjson._tcp.local", question.Name);
        Assert.True(question.UnicastResponse);

        // PTR は答えの欄、SRV・TXT・A は追加の欄（古い vrc-oscquery-lib は SRV と A を追加の欄からしか読まない→実装メモ5.124）。
        var answers = DnsMessage.ReadAnswers(service.Answer(questions, ttl: 120));
        Assert.Contains(answers, a => a.Type == DnsRecord.Ptr && a.Name == "_oscjson._tcp.local" && !a.Additional);
        Assert.Contains(answers, a => a.Type == DnsRecord.Srv && a.Name == service.QueryInstance && a.Port == 9200 && a.Additional);
        Assert.Contains(answers, a => a.Type == DnsRecord.Txt && a.Name == service.QueryInstance && a.Additional);
        Assert.Contains(answers, a => a.Type == DnsRecord.A && a.Name == service.HostName && a.Additional);
        Assert.DoesNotContain(answers, a => a.Type != DnsRecord.Ptr && !a.Additional);

        var osc = DnsMessage.ReadAnswers(service.Answer(DnsMessage.ParseQuestions(Query("_osc._udp.local", DnsRecord.Ptr))!, ttl: 120));
        Assert.Contains(osc, a => a.Type == DnsRecord.Srv && a.Name == service.OscInstance && a.Port == 9100 && a.Additional);

        // サービスの名前（SRV）を聞かれても、答えの欄にサービスの種類の PTR を入れる（古い vrc-oscquery-lib はそれがないと読まない）。
        var instance = DnsMessage.ReadAnswers(service.Answer(DnsMessage.ParseQuestions(Query(service.QueryInstance, DnsRecord.Srv))!, ttl: 120));
        Assert.Contains(instance, a => a.Type == DnsRecord.Ptr && a.Name == "_oscjson._tcp.local" && !a.Additional);
        Assert.Contains(instance, a => a.Type == DnsRecord.Srv && a.Additional);

        // ホストの A だけを聞かれたら、A を答えの欄に入れる。
        var host = DnsMessage.ReadAnswers(service.Answer(DnsMessage.ParseQuestions(Query(service.HostName, DnsRecord.A))!, ttl: 120));
        Assert.Contains(host, a => a.Type == DnsRecord.A && !a.Additional);

        // 自分と関係のない問い合わせには答えない。
        Assert.Null(service.Answer(DnsMessage.ParseQuestions(Query("_googlecast._tcp.local", DnsRecord.Ptr))!, ttl: 120));

        // 知らせる記録には2つのサービスが入り、PTR は答えの欄に2つ、SRV は追加の欄に2つ、A は1つだけ。
        var all = DnsMessage.ReadAnswers(service.Records(ttl: 120));
        Assert.Equal(2, all.Count(a => a.Type == DnsRecord.Ptr && !a.Additional));
        Assert.Equal(2, all.Count(a => a.Type == DnsRecord.Srv && a.Additional));
        Assert.Single(all, a => a.Type == DnsRecord.A);
    }

    [Fact]
    public void mDNSの応答や壊れたものは問い合わせとして読まない()
    {
        var service = new OscQueryService("VRCInstanceWristory-1", 9100, 9200);

        Assert.Null(DnsMessage.ParseQuestions(service.Records(ttl: 120)));
        Assert.Null(DnsMessage.ParseQuestions([0, 1, 2]));

        // 名前の圧縮（ポインター）も読む。
        var query = Query("_oscjson._tcp.local", DnsRecord.Ptr).ToList();
        query[5] = 2;
        query.AddRange([0xC0, 12, 0, (byte)DnsRecord.Srv, 0, 1]);
        var questions = DnsMessage.ParseQuestions(query.ToArray())!;
        Assert.Equal(2, questions.Count);
        Assert.Equal("_oscjson._tcp.local", questions[1].Name);
    }
}
