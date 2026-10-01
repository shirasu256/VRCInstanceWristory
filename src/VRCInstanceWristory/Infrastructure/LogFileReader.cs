using System.Text;
using VRCInstanceWristory.Core.Logging;

namespace VRCInstanceWristory.Infrastructure;

/// <summary>
/// ログファイルの増分読み取り（仕様4.3節）。
///
/// - 読み取り専用・FileShare.ReadWrite | Delete で開き、VRChatの書き込み・削除を妨げない。
/// - 改行（0x0A）はUTF-8の多バイト列の内部に現れないため、バイト単位で行へ分割してから復号する。
///   これにより各行の先頭バイト位置が正確に分かり、eventId の基準に使える。
/// - 完成した行だけを返し、行途中の追記は次回へ持ち越す。
/// - 不正なUTF-8を含む行は Undecodable として返し、有効なlocationを作らせない。
/// </summary>
public sealed class LogFileReader : IDisposable
{
    /// <summary>1行の上限。これを超える行は壊れたデータとして扱う。</summary>
    public const int MaxLineBytes = 1 << 20;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly FileStream _stream;
    private byte[] _buffer = new byte[64 * 1024];
    private int _bufferLength;

    /// <summary>バッファーのうち、まだ行として渡し終えていない部分の先頭。</summary>
    private int _bufferStart;

    private long _lineStart;
    private int _lineNumber;

    public LogFileReader(string path, long startOffset = 0, int startLineNumber = 0)
    {
        Path = path;
        _stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        _lineStart = Math.Min(startOffset, _stream.Length);
        _stream.Seek(_lineStart, SeekOrigin.Begin);
        _lineNumber = startLineNumber;
    }

    public string Path { get; }

    /// <summary>処理済みの完全な行の直後のバイト位置。未処理データや未完の行を含めない。</summary>
    public long Position => _lineStart;

    public int LineNumber => _lineNumber;

    public long FileLength
    {
        get
        {
            try
            {
                return _stream.Length;
            }
            catch (IOException)
            {
                return -1;
            }
        }
    }

    /// <summary>ファイルが短縮・差し替えられていないか。</summary>
    public bool IsTruncated => FileLength >= 0 && FileLength < _lineStart;

    /// <summary>
    /// 新しく完成した行を返す。
    ///
    /// 呼び出し側が途中で読むのをやめたとき（エンジンが <c>StopAtUtc</c> を越えた行で <c>break</c> するなど）は、
    /// 最後に渡した行を「まだ処理していない」ものとして残し、次の呼び出しでその行から返し直す。
    /// そのため、位置（<see cref="Position"/>・<see cref="LineNumber"/>）とバッファーの読み進めた位置は、
    /// 次の行を求められた時点（yield の後）で進める。
    /// </summary>
    public IEnumerable<LogLine> ReadNewLines()
    {
        while (true)
        {
            // 前回の途中でやめた行を含め、バッファーに残っている完成した行を先に返す。
            while (true)
            {
                var newline = Array.IndexOf(_buffer, (byte)'\n', _bufferStart, _bufferLength - _bufferStart);
                if (newline < 0)
                    break;

                var lineLength = newline - _bufferStart;
                if (lineLength > 0 && _buffer[newline - 1] == (byte)'\r')
                    lineLength--;

                var totalBytes = newline - _bufferStart + 1;
                yield return DecodeLine(_buffer.AsSpan(_bufferStart, lineLength), _lineStart, _lineNumber + 1) with
                {
                    ByteLength = totalBytes,
                };

                _lineStart += totalBytes;
                _lineNumber++;
                _bufferStart = newline + 1;
            }

            Compact();
            EnsureCapacity();

            int read;
            try
            {
                read = _stream.Read(_buffer, _bufferLength, _buffer.Length - _bufferLength);
            }
            catch (IOException)
            {
                yield break;
            }

            if (read <= 0)
                yield break;

            _bufferLength += read;
        }
    }

    /// <summary>読み終えた行をバッファーから外し、未完の行を先頭へ寄せる。</summary>
    private void Compact()
    {
        if (_bufferStart == 0)
            return;

        Buffer.BlockCopy(_buffer, _bufferStart, _buffer, 0, _bufferLength - _bufferStart);
        _bufferLength -= _bufferStart;
        _bufferStart = 0;
    }

    private LogLine DecodeLine(ReadOnlySpan<byte> bytes, long offset, int lineNumber)
    {
        // 先頭行のBOMだけ取り除く。
        if (offset == 0 && bytes.Length >= 3 && bytes[..3].SequenceEqual(Bom))
            bytes = bytes[3..];

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return LogLineParser.Undecodable(offset, lineNumber);
        }

        return LogLineParser.Parse(text, offset, lineNumber);
    }

    private void EnsureCapacity()
    {
        if (_bufferLength < _buffer.Length)
            return;

        if (_buffer.Length >= MaxLineBytes)
        {
            // 改行のない巨大な塊。壊れたデータとして捨て、以降の行から復帰する。
            _lineStart += _bufferLength;
            _bufferLength = 0;
            return;
        }

        Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, MaxLineBytes));
    }

    public void Dispose() => _stream.Dispose();
}
