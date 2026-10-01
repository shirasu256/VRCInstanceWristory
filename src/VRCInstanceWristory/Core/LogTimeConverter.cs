namespace VRCInstanceWristory.Core;

/// <summary>
/// ログのローカル時刻をUTCへ変換する（仕様3.5節）。
/// ログにUTCオフセットがないため、生成した環境の時間帯を指定して解釈する。
/// 内部の比較・保存はUTCへ統一する。
/// </summary>
public sealed class LogTimeConverter(TimeZoneInfo timeZone)
{
    public static LogTimeConverter Local => new(TimeZoneInfo.Local);

    public TimeZoneInfo TimeZone { get; } = timeZone;

    /// <summary>
    /// 変換できない（存在しない）ローカル時刻は false。夏時間の重複時刻は標準時側（早い方）を採る。
    /// 日本標準時には夏時間がないため、提供ログを読む検証では常に一意に定まる。
    /// </summary>
    public bool TryToUtc(DateTime local, out DateTime utc)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (TimeZone.IsInvalidTime(unspecified))
        {
            utc = default;
            return false;
        }

        if (TimeZone.IsAmbiguousTime(unspecified))
        {
            var offsets = TimeZone.GetAmbiguousTimeOffsets(unspecified);
            var chosen = offsets[0];
            foreach (var o in offsets)
            {
                if (o > chosen)
                    chosen = o; // 大きいオフセット = 早い時刻（夏時間側の終了直前）
            }

            utc = DateTime.SpecifyKind(unspecified - chosen, DateTimeKind.Utc);
            return true;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
        return true;
    }

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);
}
