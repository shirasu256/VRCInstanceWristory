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
    // ワールド名は実在の公開ワールド、Group ID は実在の公開グループ（2026-10-01のユーザー指定→実装メモ5.124・5.125）。
    // ワールド ID は画面に出ないので、架空の値のまま。
    private const string HimariWorld = "wrld_00000000-0000-4000-9000-000000000005";
    private const string TutorialWorld = "wrld_00000000-0000-4000-9000-000000000006";
    private const string NagisaWorld = "wrld_00000000-0000-4000-9000-000000000007";
    private const string HimariGroup = "grp_344c2fbd-91d3-41d3-949a-cda7001b2c41";
    private const string NagisaGroup = "grp_09e67308-05ec-4dc5-9061-4b50c4967515";
    private const string HimariName = "ヘヤタテ型交流ワールド「ひまり旅館」-Himari Ryokan- ［JP］";
    private const string TutorialName = "［JP］Tutorial world";
    private const string NagisaName = "日本人向け 1対1お話しワールド NAGiSA ［JP］";

    /// <summary>見本の7訪問。最新行を現在地として印を付ける。</summary>
    public static List<VisitRecord> Build(DateTime nowUtc)
    {
        // LeftMinutesAgo は退出時刻。null は「まだ滞在中」で、最新行（現在地）だけがそうなる。
        // People は退出時にいた人数（自分を含む）。滞在中の行は分からないので null。
        //
        // 2行目は Crashed、3行目は Excluded にしてある。2行目の退出時刻が赤くなり、その下に
        // 「∧ VRChat クライアントクラッシュ・対象外のインスタンスへ移動 ∨」の帯が入る（→実装メモ5.30・5.38）。
        // 6行目も Excluded にしてあり、5行目との間に「∧ 対象外のインスタンスへ移動 ∨」の帯だけが入る。
        // 3行目と5行目は同じインスタンス（39437）で、5行目は2回目になる。目印はインスタンスに付くので、どちらにも出る。
        var entries = new (int MinutesAgo, int? LeftMinutesAgo, string Id, AccessType Type, string World, string? Group, string Name, int Ordinal, int? People, bool Crashed, bool Excluded)[]
        {
            (53, 49, "07254", AccessType.Public, HimariWorld, null, HimariName, 1, 12, false, false),
            (49, 45, "29719", AccessType.GroupPublic, HimariWorld, HimariGroup, HimariName, 1, 8, true, false),
            (30, 24, "39437", AccessType.Public, TutorialWorld, null, TutorialName, 1, 14, false, true),
            (24, 18, "24688", AccessType.Public, TutorialWorld, null, TutorialName, 1, 27, false, false),
            (18, 12, "39437", AccessType.Public, TutorialWorld, null, TutorialName, 2, 40, false, false),
            (8, 3, "c1e88b6419", AccessType.Public, TutorialWorld, null, TutorialName, 1, 6, false, true),
            (3, null, "86688", AccessType.GroupOnly, NagisaWorld, NagisaGroup, NagisaName, 1, null, false, false),
        };

        var records = new List<VisitRecord>(entries.Length);
        var offset = 0;

        foreach (var e in entries)
        {
            offset += 1000;
            var world = e.World;
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
                WorldName = e.Name,
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
    /// 最後の行（86688・滞在中）には4人と写真5枚を付け、1人はこちらより先に出たことにする。
    /// デスクトップのウィンドウの見本はこの行を選んだ状態を描く。
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
    public const int SelectedSampleIndex = 6;

    /// <summary>
    /// 見本のグループ名（→実装メモ5.48）。NAGiSA の Group にだけ名前を付け、ひまり旅館の Group Public は Group ID のままにする（→実装メモ5.124）。
    /// </summary>
    public static GroupNaming Groups { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal) { [NagisaGroup] = "ロングNAGiSA" });

    /// <summary>
    /// 見本に最初から付けておく目印（2026-09-22のユーザー指定→実装メモ5.32）。
    ///
    /// 07254 にチェック、39437 にハートを付ける（→実装メモ5.124）。39437 は2回訪れているので、1つ付ければ2行どちらにも出る。
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

        Set("07254", InstanceMark.Check);
        Set("39437", InstanceMark.Heart);

        return marks;
    }

    public static List<DisplayRow> BuildRows(DateTime nowUtc, LogTimeConverter time)
    {
        var records = Build(nowUtc);

        // 見本の時刻と同じ「今」で作る。別に時計を読むと、分の境目をまたいだとき「N分前」が1つずれる。
        return RowFormatter.Build(records, records[^1].EventId, time, nowUtc, Marks(records), Groups);
    }
}
