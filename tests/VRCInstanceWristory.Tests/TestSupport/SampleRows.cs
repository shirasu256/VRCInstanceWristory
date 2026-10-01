using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Modes;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// <c>--render-sample</c> と同じ見本の履歴を、決まった「今」で作る。
/// 壁の時計で作ると「N分前」などの文字が実行した時刻と PC の時間帯で変わり、画素や幅を比べる検証が揺れる。
/// </summary>
public static class SampleRows
{
    /// <summary>見本の「今」（東京の 2026-09-11 12:00）。</summary>
    public static readonly DateTime NowUtc = new(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>見本の訪問記録。</summary>
    public static List<VisitRecord> Records() => SampleHistory.Build(NowUtc);

    /// <summary>見本の行（東京の時刻で書く）。</summary>
    public static List<DisplayRow> Build() => SampleHistory.BuildRows(NowUtc, new LogTimeConverter(EngineHarness.Tokyo));

    /// <summary>見本の行ごとの詳しい情報。</summary>
    public static IReadOnlyList<RowDetail> Details()
    {
        var records = Records();
        return RowDetails.Build(records, records[^1].EventId, SampleHistory.Groups);
    }
}
