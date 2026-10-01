namespace VRCInstanceWristory.Core.Logging;

/// <summary>ログ1行の解析結果。</summary>
/// <param name="ByteOffset">行頭のバイト位置。eventId の基準になるので、文字数で代用しない。</param>
/// <param name="LineNumber">1始まりの行番号。診断用。</param>
/// <param name="Kind">行の種別。</param>
/// <param name="TimestampLocal">見出し行のローカル日時。種別が Entry 以外では既定値。</param>
/// <param name="Level">Debug / Warning / Error 等。</param>
/// <param name="Message">"-  " の後ろ。行末の空白だけを取り除く。</param>
public readonly record struct LogLine(
    long ByteOffset,
    int LineNumber,
    LogLineKind Kind,
    DateTime TimestampLocal,
    string Level,
    string Message)
{
    /// <summary>日時付きの正常な見出し行かどうか。</summary>
    public bool HasValidTimestamp => Kind == LogLineKind.Entry;

    /// <summary>改行を含む行全体のバイト数。読み取り位置の更新に使う。</summary>
    public int ByteLength { get; init; }

    /// <summary>この行を処理し終えた直後のバイト位置。</summary>
    public long ByteEnd => ByteOffset + ByteLength;
}

public enum LogLineKind
{
    /// <summary>日時・レベル付きの見出し行。</summary>
    Entry,

    /// <summary>スタックトレース等、日時を持たない継続行。訪問・現在地の判断に使わない。</summary>
    Continuation,

    /// <summary>日時の形はあるが値が不正な行。現在地を変えるイベントには使わない。</summary>
    BadTimestamp,

    /// <summary>UTF-8として解釈できないバイト列を含む行。</summary>
    Undecodable,
}
