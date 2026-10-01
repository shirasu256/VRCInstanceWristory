using System.Text;

namespace VRCInstanceWristory.Infrastructure.Osc;

/// <summary>OSC のパケット（メッセージとバンドル）を読む。</summary>
public static class OscPacket
{
    /// <summary>パケットの中のメッセージ（アドレスと最初の引数）。読めない部分は捨てる。</summary>
    public static List<(string Address, object? Value)> Parse(ReadOnlySpan<byte> packet)
    {
        var messages = new List<(string, object?)>();
        ParseInto(packet, messages, depth: 0);
        return messages;
    }

    private static void ParseInto(ReadOnlySpan<byte> packet, List<(string, object?)> messages, int depth)
    {
        if (packet.Length < 4 || depth > 8)
            return;

        var offset = 0;

        if (!TryReadString(packet, ref offset, out var head))
            return;

        if (head == "#bundle")
        {
            // タイムタグ（8バイト）のあとに、長さつきの要素が並ぶ。
            offset += 8;

            while (offset + 4 <= packet.Length)
            {
                var size = ReadInt(packet, offset);
                offset += 4;

                if (size <= 0 || offset + size > packet.Length)
                    return;

                ParseInto(packet.Slice(offset, size), messages, depth + 1);
                offset += size;
            }

            return;
        }

        if (!head.StartsWith('/'))
            return;

        object? value = null;

        if (TryReadString(packet, ref offset, out var tags) && tags.StartsWith(',') && tags.Length >= 2)
        {
            switch (tags[1])
            {
                case 'T':
                    value = true;
                    break;
                case 'F':
                    value = false;
                    break;
                case 'i' when offset + 4 <= packet.Length:
                    value = ReadInt(packet, offset);
                    break;
                case 'f' when offset + 4 <= packet.Length:
                    value = BitConverter.Int32BitsToSingle(ReadInt(packet, offset));
                    break;
            }
        }

        messages.Add((head, value));
    }

    /// <summary>真偽として読む（T/F のほか、0 以外の数も真）。読めなければ null。</summary>
    public static bool? AsBool(object? value) => value switch
    {
        bool b => b,
        int i => i != 0,
        float f => f != 0f,
        _ => null,
    };

    private static int ReadInt(ReadOnlySpan<byte> data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    /// <summary>0 で終わり、4バイト境界まで詰めた文字列を読む。</summary>
    private static bool TryReadString(ReadOnlySpan<byte> data, ref int offset, out string text)
    {
        text = string.Empty;
        var end = data[offset..].IndexOf((byte)0);

        if (end < 0)
            return false;

        text = Encoding.UTF8.GetString(data.Slice(offset, end));
        offset += (end + 4) & ~3;
        return offset <= data.Length;
    }
}
