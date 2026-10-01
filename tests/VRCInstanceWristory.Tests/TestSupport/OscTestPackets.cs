using System.Text;

namespace VRCInstanceWristory.Tests;

/// <summary>検証用に OSC のメッセージを作る（VRChat が送るものと同じ形）。</summary>
public static class OscTestPackets
{
    /// <summary>メッセージを1つ作る。引数は真偽1つだけ。</summary>
    public static byte[] Message(string address, bool value)
    {
        var bytes = new List<byte>();
        AppendString(bytes, address);
        AppendString(bytes, value ? ",T" : ",F");
        return [.. bytes];
    }

    /// <summary>0 で終わり、4バイト境界まで詰めた文字列。</summary>
    private static void AppendString(List<byte> bytes, string text)
    {
        bytes.AddRange(Encoding.UTF8.GetBytes(text));
        var pad = 4 - (Encoding.UTF8.GetByteCount(text) % 4);
        bytes.AddRange(new byte[pad]);
    }
}
