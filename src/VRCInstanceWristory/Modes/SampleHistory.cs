using System.Drawing;
using System.Drawing.Drawing2D;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Modes;

/// <summary>
/// 表示・入力の動作確認だけに使う固定サンプル（仕様13節の「最初の動作確認」）。
/// 実履歴ではない。製品のログ監視モードとは明確に分ける。
/// </summary>
public static class SampleHistory
{
    private const string WorldA = "wrld_00000000-0000-4000-9000-000000000005";
    private const string WorldB = "wrld_00000000-0000-4000-9000-000000000006";
    private const string GroupA = "grp_00000001-0000-4000-a000-000000000000";
    private const string GroupB = "grp_00000002-0000-4000-a000-000000000000";
    private const string GroupC = "grp_00000090-0000-4000-a000-000000000000";
    private const string NameA = "～まったり交流ラウンジ～［OpenBeta］";
    private const string NameB = "のんびり雑談カフェ【PC Desktop＆VR両対応】";

    /// <summary>仕様11.1節の9訪問と同じ並び。最新行を現在地として印を付ける。</summary>
    public static List<VisitRecord> Build(DateTime nowUtc)
    {
        // LeftMinutesAgo は退出時刻。null は「まだ滞在中」で、最新行（現在地）だけがそうなる。
        // People は退出時にいた人数（自分を含む）。滞在中の行は分からないので null。
        //
        // 5行目は Excluded にしてある。4行目を離れてから5行目へ入るまでに対象外のインスタンスへ
        // 移っていた区間なので、ここに「∧ 対象外のインスタンスへ移動 ∨」の帯が出る（→実装メモ5.38）。
        //
        // その4行目は Crashed にしてある。退出時刻が赤くなり、次の行との間に
        // 「∧ VRChatクライアントがクラッシュしました ∨」の帯も入る（→実装メモ5.30）。
        // 2枚の帯が重なる並びを、この見本1つで確かめられるようにしてある。
        var entries = new (int MinutesAgo, int? LeftMinutesAgo, string Id, AccessType Type, string? Group, string World, int Ordinal, int? People, bool Crashed, bool Excluded)[]
        {
            (53, 49, "07254", AccessType.GroupPublic, GroupA, NameA, 1, 12, false, false),
            (49, 46, "29719", AccessType.GroupPublic, GroupB, NameB, 1, 8, false, false),
            (46, 45, "07254", AccessType.GroupPublic, GroupA, NameA, 2, 14, false, false),
            (45, 44, "39437", AccessType.GroupPublic, GroupA, NameA, 1, 3, true, false),
            (10, 7, "24688", AccessType.Public, null, NameB, 1, 27, false, true),
            (7, 5, "c1e88b6419", AccessType.Public, null, NameB, 1, 40, false, false),
            (5, 1, "db7da28295", AccessType.Public, null, NameB, 1, 6, false, false),
            (1, 0, "86688", AccessType.GroupOnly, GroupC, "Group Only の確認用ワールド", 1, 5, false, false),
            (0, null, "07254", AccessType.GroupPublic, GroupA, NameA, 3, null, false, false),
        };

        var records = new List<VisitRecord>(entries.Length);
        var offset = 0;

        foreach (var e in entries)
        {
            offset += 1000;
            var world = e.Type == AccessType.Public || e.Id is "29719" or "24688" or "c1e88b6419" or "db7da28295"
                ? WorldB
                : WorldA;

            var groupAccess = e.Type == AccessType.GroupOnly ? "members" : "public";

            records.Add(new VisitRecord
            {
                EventId = $"sample@{offset}",
                SourceSessionId = "sample",
                SuccessByteOffset = offset,
                SessionOrder = 0,
                LocationKey = $"{world}:{e.Id}",
                WorldId = world,
                InstanceId = e.Id,
                AccessType = e.Type,
                WorldName = e.World,
                Region = "jp",
                GroupId = e.Group,
                Location = e.Group is null
                    ? $"{world}:{e.Id}~region(jp)"
                    : $"{world}:{e.Id}~group({e.Group})~groupAccessType({groupAccess})~region(jp)",
                VisitedAtUtc = nowUtc - TimeSpan.FromMinutes(e.MinutesAgo),
                LeftAtUtc = e.LeftMinutesAgo is { } left ? nowUtc - TimeSpan.FromMinutes(left) : null,
                VisitOrdinal = e.Ordinal,
                PeopleCount = e.People,
                EndedByCrash = e.Crashed,
                ExcludedBefore = e.Excluded,
            });
        }

        AddExtras(records, nowUtc);
        return records;
    }

    /// <summary>
    /// 見本の「一緒にいた人」（→実装メモ5.46）と写真（→実装メモ5.47）。名前は見本用に作ったもので、実在の利用者ではない。
    ///
    /// 8行目（86688）には4人と写真4枚を付け、1人はこちらより先に出たことにする。
    /// デスクトップのウィンドウの見本はこの行を選んだ状態を描く。滞在中の行（最後の07254）にも写真を1枚付ける。
    /// </summary>
    private static void AddExtras(List<VisitRecord> records, DateTime nowUtc)
    {
        var group = records[SelectedSampleIndex];
        var names = new[] { "ことり", "Aoi_VR", "もちもち", "Kuro_7" };

        for (var i = 0; i < names.Length; i++)
        {
            group.Companions.Add(new Companion(
                $"usr_sample-{i}",
                names[i],
                group.VisitedAtUtc,
                i == 3 ? group.VisitedAtUtc.AddSeconds(30) : null));
        }

        for (var i = 1; i <= 4; i++)
            group.Photos.Add(new VisitPhoto($@"C:\Users\Public\Pictures\VRChat\sample-{i}.png", nowUtc.AddSeconds(-60 + (i * 5))));

        records[^1].Photos.Add(new VisitPhoto(@"C:\Users\Public\Pictures\VRChat\sample-5.png", nowUtc.AddSeconds(-10)));
    }

    /// <summary>
    /// 見本の写真のサムネイル（→実装メモ5.55）。見本の写真は実在しないので、写真ごとに色の違う簡単な風景をその場で描く。
    /// <c>--render-sample --window</c> で「選んだ行」の写真の並びを確かめるためだけに使う。返した絵は呼び出し側が捨てる。
    /// </summary>
    public static Bitmap SampleThumbnail(string photoPath)
    {
        var index = int.TryParse(Path.GetFileNameWithoutExtension(photoPath).Replace("sample-", string.Empty), out var n) ? n : 0;
        var skies = new[]
        {
            (Color.FromArgb(0x2b, 0x3f, 0x7a), Color.FromArgb(0xe8, 0x9a, 0x6a)),
            (Color.FromArgb(0x1d, 0x5c, 0x8c), Color.FromArgb(0x9f, 0xd6, 0xf0)),
            (Color.FromArgb(0x3a, 0x24, 0x5e), Color.FromArgb(0xd0, 0x7a, 0xb8)),
            (Color.FromArgb(0x0f, 0x2a, 0x3a), Color.FromArgb(0x4f, 0x9f, 0x9a)),
            (Color.FromArgb(0x24, 0x2a, 0x55), Color.FromArgb(0xf2, 0xc9, 0x7c)),
        };

        var (top, bottom) = skies[Math.Abs(index) % skies.Length];
        var bitmap = new Bitmap(320, 180);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, 320, 180), top, bottom, LinearGradientMode.Vertical))
            g.FillRectangle(sky, 0, 0, 320, 180);

        using (var sun = new SolidBrush(Color.FromArgb(200, 255, 244, 214)))
            g.FillEllipse(sun, 40 + (index * 47 % 200), 30 + (index * 13 % 40), 38, 38);

        using (var hills = new SolidBrush(Color.FromArgb(230, 0x10, 0x18, 0x20)))
        {
            g.FillPolygon(hills, [new Point(0, 180), new Point(0, 130), new Point(90, 100), new Point(170, 135), new Point(250, 95), new Point(320, 120), new Point(320, 180)]);
        }

        using (var person = new SolidBrush(Color.FromArgb(240, 0x0a, 0x0e, 0x12)))
        {
            // 手前に人影を1〜2人。
            var x = 120 + (index * 31 % 90);
            g.FillEllipse(person, x, 108, 18, 18);
            g.FillRectangle(person, x + 2, 124, 14, 40);

            if (index % 2 == 0)
            {
                g.FillEllipse(person, x + 34, 112, 16, 16);
                g.FillRectangle(person, x + 36, 126, 12, 38);
            }
        }

        return bitmap;
    }

    /// <summary>デスクトップのウィンドウの見本で選んでおく行（86688・0始まり）。</summary>
    public const int SelectedSampleIndex = 7;

    /// <summary>
    /// 見本のグループ名（→実装メモ5.48）。いちばん多く出てくるグループにだけ名前を付け、もう1つ（Group Only）は付けずにおく。
    /// </summary>
    public static GroupNaming Groups { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal) { [GroupA] = "まったり会" });

    /// <summary>
    /// 見本に最初から付けておく目印（2026-09-22のユーザー指定→実装メモ5.32）。
    ///
    /// 3種類を1つずつ付ける。07254 は3回訪れているので、1つ付ければ3行すべてに出る。
    /// 目印が訪問ではなく<b>インスタンス</b>に付くことを、この見本1つで確かめられるようにしてある。
    /// </summary>
    public static Dictionary<string, InstanceMark> Marks(IReadOnlyList<VisitRecord> records)
    {
        var marks = new Dictionary<string, InstanceMark>(StringComparer.Ordinal);

        void Set(string instanceId, InstanceMark mark)
        {
            var record = records.FirstOrDefault(r => string.Equals(r.InstanceId, instanceId, StringComparison.Ordinal));

            if (record is not null)
                marks[record.LocationKey] = mark;
        }

        Set("07254", InstanceMark.Heart);
        Set("24688", InstanceMark.Check);
        Set("39437", InstanceMark.Warning);

        return marks;
    }

    public static List<DisplayRow> BuildRows(DateTime nowUtc, LogTimeConverter time)
    {
        var records = Build(nowUtc);

        // 見本の時刻と同じ「今」で作る。別に時計を読むと、分の境目をまたいだとき「N分前」が1つずれる。
        return RowFormatter.Build(records, records[^1].EventId, time, nowUtc, Marks(records), Groups);
    }
}
