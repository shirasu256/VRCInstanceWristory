using System.Globalization;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core.Presentation;

public static class RowFormatter
{
    public const string UnknownWorldName = "ワールド名不明";

    /// <summary>
    /// 回数が分からない行の回数欄。回数は括弧を外して `1回目` と出すので（2026-09-25のユーザー指定）、
    /// こちらも括弧を付けない。
    /// </summary>
    public const string UnknownOrdinal = "回数不明";

    /// <summary>
    /// 「N分前」の欄の幅の下限にする見た目（2桁）。一覧の中で最も長い値がこれより短くても、
    /// この幅は取っておく。9分前から10分前へ変わるたびに回数と人数の位置が動かないようにするため。
    /// </summary>
    public const string AgoSample = "(00分前)";

    /// <summary>入室と退出の区切り。</summary>
    public const string TimeSeparator = " - ";

    /// <summary>
    /// 時刻欄の最大の見た目（等幅なのでこの幅で足りる）。
    /// 退出時刻が未定の行でも回数の位置がずれないよう、描画側の列幅の基準に使う。
    /// </summary>
    public const string TimeSample = "00:00" + TimeSeparator + "00:00";

    /// <param name="currentLocationKey">
    /// いま滞在しているインスタンスの locationKey。同じインスタンスの行では「ここへ戻る」を押せないようにする（→実装メモ5.43）。
    /// </param>
    public static DisplayRow Build(
        VisitRecord record,
        LogTimeConverter time,
        bool isCurrent,
        DateTime nowUtc,
        VisitRecord? previous = null,
        InstanceMark mark = InstanceMark.None,
        GroupNaming? groups = null,
        string? currentLocationKey = null)
    {
        var local = time.ToLocal(record.VisitedAtUtc);
        var left = record.LeftAtUtc is { } leftUtc ? time.ToLocal(leftUtc) : (DateTime?)null;
        var naming = groups ?? GroupNaming.None;

        var here = isCurrent
            || (currentLocationKey is not null && string.Equals(record.LocationKey, currentLocationKey, StringComparison.Ordinal));
        var linkable = InstanceLaunch.CanOpen(record.Location);

        return new DisplayRow(
            record.EventId,
            record.InstanceId,
            FormatTime(local, left),
            FormatOrdinal(record.VisitOrdinal),
            string.IsNullOrWhiteSpace(record.WorldName) ? UnknownWorldName : record.WorldName!,
            FormatType(record.AccessType, record.GroupId, naming.NameOf(record.GroupId), naming.ShowIdWithName),
            isCurrent,
            FormatPeople(record.PeopleCount),
            StayFraction(record, nowUtc),
            ExcludedBetween(previous, record),
            previous?.EndedByCrash ?? false,
            mark,
            record.LeftAtUtc is { } leftAt ? FormatAgo(MinutesAgo(leftAt, nowUtc)) : string.Empty,
            FormatClock(local),
            record.Photos.Count,
            !here && linkable,
            linkable,
            local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    public static List<DisplayRow> Build(
        IReadOnlyList<VisitRecord> records,
        string? currentEventId,
        LogTimeConverter time,
        DateTime nowUtc,
        IReadOnlyDictionary<string, InstanceMark>? marks = null,
        GroupNaming? groups = null)
    {
        var rows = new List<DisplayRow>(records.Count);
        var currentLocation = CurrentLocationKey(records, currentEventId);

        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            var isCurrent = currentEventId is not null
                && string.Equals(r.EventId, currentEventId, StringComparison.Ordinal);

            // 目印はインスタンス（ワールドID + インスタンス番号）に付くので、
            // 同じインスタンスの行はすべて同じ印になる（→実装メモ5.32）。
            var mark = marks is not null && marks.TryGetValue(r.LocationKey, out var m) ? m : InstanceMark.None;

            // 対象外へ移ったかは1つ前の行との間で見る。先頭の行には前がないので帯を出さない。
            rows.Add(Build(r, time, isCurrent, nowUtc, i > 0 ? records[i - 1] : null, mark, groups, currentLocation));
        }

        return rows;
    }

    /// <summary>いま滞在している行の locationKey。滞在していなければ null。</summary>
    public static string? CurrentLocationKey(IReadOnlyList<VisitRecord> records, string? currentEventId)
    {
        if (currentEventId is null)
            return null;

        foreach (var r in records)
        {
            if (string.Equals(r.EventId, currentEventId, StringComparison.Ordinal))
                return r.LocationKey;
        }

        return null;
    }

    /// <summary>
    /// 滞在時間を既定の保持時間（<see cref="HistoryStore.DefaultRetention"/>・60分）で割った割合（0〜1）。
    /// 印の列の棒の長さに使う。まだ滞在している行は入室から現在までで数える。
    ///
    /// 保持時間は設定で変えられるが（→実装メモ5.39）、棒の尺は60分のまま変えない。
    /// 設定に合わせて尺を変えると、同じ長さの滞在が設定しだいで違う長さの棒に見えてしまうため。
    ///
    /// 1分単位に切り捨てる。こうすると滞在中の行でも値が変わるのは1分に1回までで、
    /// 行の下絵の描き直し（1回10msほど→実装メモ5.35）がそれ以上増えない。
    /// 切り捨てなので、退出した瞬間に棒が短くなることもない。
    /// </summary>
    public static float StayFraction(VisitRecord record, DateTime nowUtc)
    {
        var until = record.LeftAtUtc ?? nowUtc;
        var minutes = Math.Floor((until - record.VisitedAtUtc).TotalMinutes);

        if (minutes <= 0d)
            return 0f;

        return (float)Math.Clamp(minutes / HistoryStore.DefaultRetention.TotalMinutes, 0d, 1d);
    }

    /// <summary>
    /// 行の間に出す「対象外のインスタンスへ移った」断り（2026-09-26のユーザー指定→実装メモ5.38）。
    ///
    /// <paramref name="previous"/> を離れてから <paramref name="record"/> へ入るまでに、
    /// 対象外のインスタンスへの入室が確定していれば true。滞在の長さは問わない。
    /// 同じセッションの中なら <see cref="VisitRecord.ExcludedBefore"/> で分かり、
    /// 対象外へ移ったまま終了して次の起動で直接対象へ入った場合は、前の行の
    /// <see cref="VisitRecord.ExcludedAfter"/> で分かる。
    ///
    /// 前の行がない（先頭の行）場合は false。帯は行と行の間の断りなので、比べる行がなければ出さない。
    /// </summary>
    public static bool ExcludedBetween(VisitRecord? previous, VisitRecord record)
        => previous is not null && (previous.ExcludedAfter || record.ExcludedBefore);

    /// <summary>
    /// クラッシュの断り（2026-09-21のユーザー指定→実装メモ5.30）。
    ///
    /// VRChatが正常終了の記録を残さずに終わり、履歴が消えるまでの間に立ち上げ直して
    /// 対象インスタンスへ戻ったとき、その前後の行の間に入れる。
    /// 「対象外のインスタンスへ移動」と同じ見た目・同じ位置の帯に出す。
    /// </summary>
    public const string CrashBody = "VRChat クライアントがクラッシュしました";

    /// <summary>
    /// 対象外のインスタンスへ移った区間の本文（2026-09-26のユーザー指定→実装メモ5.38）。
    /// 滞在の長さは問わないので、分数は付けない。
    /// </summary>
    public const string ExcludedBody = "対象外のインスタンスへ移動";

    /// <summary>
    /// 帯に並べる本文の区切り。クラッシュと対象外への移動が重なっても帯は1枚にまとめる
    /// （2026-09-21のユーザー指定→実装メモ5.30）。
    /// </summary>
    public const string BandSeparator = "・";

    /// <summary>
    /// 帯の文字を、色を変える区切りで分ける（2026-09-21のユーザー指定→実装メモ5.30）。
    ///
    /// <see cref="BandSegment.Crash"/> の部分だけを赤で描く。クラッシュは推定を含む断りなので、
    /// 同じ帯に並ぶ「対象外のインスタンスへ移動」や挟みの記号とは色で区別する。
    /// どちらも立たなければ空のリストを返す。
    /// </summary>
    public static List<BandSegment> BandSegments(bool crashed, bool excluded)
    {
        if (!crashed && !excluded)
            return [];

        var segments = new List<BandSegment>(4) { new("∧ ", false) };

        if (crashed)
            segments.Add(new BandSegment(CrashBody, true));

        if (excluded)
        {
            if (crashed)
                segments.Add(new BandSegment(BandSeparator, false));

            segments.Add(new BandSegment(ExcludedBody, false));
        }

        segments.Add(new BandSegment(" ∨", false));
        return segments;
    }

    /// <summary>
    /// 1段目の時刻。「入室 - 退出」で出す（2026-09-20のユーザー指定）。
    /// まだ対象インスタンスに滞在している行は退出時刻がないので、区切りだけを出して開いたままにする。
    /// </summary>
    public static string FormatTime(DateTime joinLocal, DateTime? leaveLocal)
        => leaveLocal is { } leave
            ? $"{FormatClock(joinLocal)}{TimeSeparator}{FormatClock(leave)}"
            : $"{FormatClock(joinLocal)}{TimeSeparator.TrimEnd()}";

    /// <summary>時刻を `HH:mm` で出す。行の時刻と見出しの「〜から」で同じ書き方にする。</summary>
    public static string FormatClock(DateTime local)
        => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 退出してから今までの分数（2026-09-25のユーザー指定→実装メモ5.36）。
    ///
    /// 秒を切り捨てた「時:分」どうしの差にする。行に出している退出時刻（`HH:mm`）と手元の時計を
    /// 見比べた値と一致し、00:36 に出た行は 00:45 のあいだずっと「9分前」になる。
    /// どの行も毎分0秒にそろって1つ進むので、行を作り直すのは1分に1回で済む。
    /// 時計のずれで退出が未来に見えても、負にはしない。
    /// </summary>
    public static int MinutesAgo(DateTime leftUtc, DateTime nowUtc)
        => (int)Math.Max(0L, MinuteIndex(nowUtc) - MinuteIndex(leftUtc));

    /// <summary>秒を切り捨てた「何分目か」。<see cref="MinutesAgo"/> と行の作り直しの判断に使う。</summary>
    public static long MinuteIndex(DateTime utc) => utc.Ticks / TimeSpan.TicksPerMinute;

    /// <summary>
    /// 「N分前」の見た目。対象インスタンスを渡り歩いている間は古い行も消えずに残るので、
    /// 60分を超える値も出る。そのときも時間へ直さず、分のまま出す（`(76分前)`）。
    /// </summary>
    public static string FormatAgo(int minutes)
        => string.Create(CultureInfo.InvariantCulture, $"({minutes}分前)");

    /// <summary>
    /// 回数欄。2026-09-25のユーザー指定で括弧を外し、括弧は前の「(9分前)」へ譲った
    /// （`00:35 - 00:36 (9分前) 1回目 24人`）。
    /// </summary>
    public static string FormatOrdinal(int? ordinal)
        => ordinal is { } n ? $"{n}回目" : UnknownOrdinal;

    /// <summary>
    /// 退出時にそのインスタンスにいた人数。自分を含む（2026-09-20のユーザー指定）。
    /// まだ分からない行（滞在中・退出を記録できなかった）は空文字にして何も出さない。
    /// </summary>
    public static string FormatPeople(int? people)
        => people is { } n ? $"{n}人" : string.Empty;

    /// <summary>
    /// 3段目。PublicにはGroup IDがないので種類だけを表示する。
    /// Group IDは短縮せずフルで渡す。収まらない場合の見せ方は描画側（右端フェード）に任せる。
    ///
    /// 利用者がグループに名前を付けていれば、Group ID の代わりに名前を出す（2026-09-26のユーザー指定→実装メモ5.48）。
    /// <paramref name="showIdWithName"/> なら名前の後ろに Group ID も並べる。
    /// </summary>
    public static string FormatType(AccessType accessType, string? groupId, string? groupName = null, bool showIdWithName = false)
    {
        if (string.IsNullOrEmpty(groupId))
            return accessType.DisplayName();

        if (string.IsNullOrWhiteSpace(groupName))
            return $"{accessType.DisplayName()} · {groupId}";

        return showIdWithName
            ? $"{accessType.DisplayName()} · {groupName} · {groupId}"
            : $"{accessType.DisplayName()} · {groupName}";
    }
}
