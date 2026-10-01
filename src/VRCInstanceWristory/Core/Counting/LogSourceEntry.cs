using System.Globalization;

namespace VRCInstanceWristory.Core.Counting;

/// <summary>
/// ログファイル1つ（＝1つのVRChat起動セッション）に対応する記録。
/// sourceSessionId は初回認識時に割り当て、追記・アプリ再起動では変えない。
/// ファイルが差し替えられた場合は同じIDを再利用しない（仕様5.3節）。
/// </summary>
public sealed class LogSourceEntry
{
    public required string SourceSessionId { get; init; }

    /// <summary>フォルダーを含まないファイル名。</summary>
    public required string FileName { get; set; }

    /// <summary>先頭バイト列のハッシュ。ファイル全体のハッシュは追記で変わるため使わない。</summary>
    public string? PrefixHash { get; set; }

    public int PrefixLength { get; set; }

    public DateTime? CreatedUtc { get; set; }

    /// <summary>処理済みの完全な行の直後のバイト位置。未処理データや未完の行を含めない。</summary>
    public long AppliedOffset { get; set; }

    public required LogSessionInfo Session { get; init; }

    /// <summary>走査で見つからなかった（VRChatに削除された）ファイル。記録は回数の判定に残す。</summary>
    public bool FileMissing { get; set; }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{SourceSessionId} {FileName} applied={AppliedOffset}");
}
