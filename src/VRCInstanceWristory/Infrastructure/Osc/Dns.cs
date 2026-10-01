using System.Net;
using System.Text;

namespace VRCInstanceWristory.Infrastructure.Osc;

/// <summary>mDNS の問い合わせの1つ。</summary>
public readonly record struct DnsQuestion(string Name, ushort Type, bool UnicastResponse);

/// <summary>mDNS の答えの記録1つ（名前・種類・中身）。</summary>
public sealed record DnsRecord(string Name, ushort Type, bool CacheFlush, uint Ttl, byte[] Data)
{
    public const ushort A = 1;
    public const ushort Ptr = 12;
    public const ushort Txt = 16;
    public const ushort Srv = 33;
    public const ushort Any = 255;

    public static DnsRecord Pointer(string name, string target, uint ttl) => new(name, Ptr, false, ttl, DnsMessage.EncodeName(target));

    public static DnsRecord Service(string name, string host, int port, uint ttl)
    {
        var target = DnsMessage.EncodeName(host);
        var data = new byte[6 + target.Length];
        data[4] = (byte)(port >> 8);
        data[5] = (byte)port;
        target.CopyTo(data, 6);
        return new(name, Srv, true, ttl, data);
    }

    public static DnsRecord Text(string name, string text, uint ttl)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return new(name, Txt, true, ttl, [(byte)bytes.Length, .. bytes]);
    }

    public static DnsRecord Address(string name, IPAddress address, uint ttl) => new(name, A, true, ttl, address.GetAddressBytes());
}

/// <summary>mDNS（DNS の形式）の問い合わせを読み、答えを作る。名前の圧縮は読むときだけ扱う。</summary>
public static class DnsMessage
{
    /// <summary>問い合わせの質問を読む。答え（応答のビットが立っている）や壊れたものは null。</summary>
    public static List<DnsQuestion>? ParseQuestions(ReadOnlySpan<byte> message)
    {
        if (message.Length < 12)
            return null;

        var flags = (message[2] << 8) | message[3];

        if ((flags & 0x8000) != 0)
            return null;

        var count = (message[4] << 8) | message[5];
        var offset = 12;
        var questions = new List<DnsQuestion>();

        for (var i = 0; i < count; i++)
        {
            if (!TryReadName(message, ref offset, out var name) || offset + 4 > message.Length)
                return questions;

            var type = (ushort)((message[offset] << 8) | message[offset + 1]);
            var cls = (message[offset + 2] << 8) | message[offset + 3];
            offset += 4;
            questions.Add(new DnsQuestion(name, type, (cls & 0x8000) != 0));
        }

        return questions;
    }

    private static bool TryReadName(ReadOnlySpan<byte> message, ref int offset, out string name)
    {
        var labels = new List<string>();
        var position = offset;
        var jumped = false;
        var guard = 0;

        while (true)
        {
            if (position >= message.Length || guard++ > 128)
            {
                name = string.Empty;
                return false;
            }

            var length = message[position];

            if (length == 0)
            {
                position++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (position + 1 >= message.Length)
                {
                    name = string.Empty;
                    return false;
                }

                var pointer = ((length & 0x3F) << 8) | message[position + 1];

                if (!jumped)
                    offset = position + 2;

                jumped = true;
                position = pointer;
                continue;
            }

            if (position + 1 + length > message.Length)
            {
                name = string.Empty;
                return false;
            }

            labels.Add(Encoding.UTF8.GetString(message.Slice(position + 1, length)));
            position += 1 + length;
        }

        if (!jumped)
            offset = position;

        name = string.Join('.', labels);
        return true;
    }

    public static byte[] EncodeName(string name)
    {
        var bytes = new List<byte>();

        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var encoded = Encoding.UTF8.GetBytes(label);
            bytes.Add((byte)Math.Min(encoded.Length, 63));
            bytes.AddRange(encoded.Take(63));
        }

        bytes.Add(0);
        return [.. bytes];
    }

    /// <summary>
    /// 応答（権威ある答え）。<paramref name="answers"/> を答えの欄に、<paramref name="additional"/> を追加の欄に並べる。
    ///
    /// DNS-SD の決まりどおり、サービスの PTR は答えの欄、SRV・TXT・A は追加の欄に入れる（→実装メモ5.124）。
    /// VRChat が使う vrc-oscquery-lib の 2024-03 より前の版は、SRV と A を追加の欄からしか読まない。
    /// 全部を答えの欄に入れていた間は、VRChat にこのアプリが見つけてもらえず、AFK が届かなかった。
    /// </summary>
    public static byte[] Response(IReadOnlyList<DnsRecord> answers, IReadOnlyList<DnsRecord>? additional = null)
    {
        additional ??= [];

        var bytes = new List<byte>
        {
            0, 0,       // ID（mDNS では 0）
            0x84, 0x00, // 応答・権威ある答え
            0, 0,       // 質問 0
            (byte)(answers.Count >> 8), (byte)answers.Count,
            0, 0,
            (byte)(additional.Count >> 8), (byte)additional.Count,
        };

        foreach (var record in answers.Concat(additional))
        {
            bytes.AddRange(EncodeName(record.Name));
            bytes.Add((byte)(record.Type >> 8));
            bytes.Add((byte)record.Type);

            var cls = record.CacheFlush ? 0x8001 : 0x0001;
            bytes.Add((byte)(cls >> 8));
            bytes.Add((byte)cls);

            bytes.Add((byte)(record.Ttl >> 24));
            bytes.Add((byte)(record.Ttl >> 16));
            bytes.Add((byte)(record.Ttl >> 8));
            bytes.Add((byte)record.Ttl);

            bytes.Add((byte)(record.Data.Length >> 8));
            bytes.Add((byte)record.Data.Length);
            bytes.AddRange(record.Data);
        }

        return [.. bytes];
    }

    /// <summary>応答に含まれる記録の名前・種類・ポートと、追加の欄にあるか（検証用。名前の圧縮は扱わない）。</summary>
    internal static List<(string Name, ushort Type, int Port, bool Additional)> ReadAnswers(ReadOnlySpan<byte> message)
    {
        var result = new List<(string, ushort, int, bool)>();
        var answers = (message[6] << 8) | message[7];
        var authority = (message[8] << 8) | message[9];
        var additional = (message[10] << 8) | message[11];
        var offset = 12;

        for (var i = 0; i < answers + authority + additional; i++)
        {
            if (!TryReadName(message, ref offset, out var name))
                break;

            var type = (ushort)((message[offset] << 8) | message[offset + 1]);
            var length = (message[offset + 8] << 8) | message[offset + 9];
            var data = offset + 10;
            var port = type == DnsRecord.Srv ? (message[data + 4] << 8) | message[data + 5] : 0;
            result.Add((name, type, port, i >= answers + authority));
            offset = data + length;
        }

        return result;
    }
}
