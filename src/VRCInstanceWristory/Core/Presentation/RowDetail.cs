using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// デスクトップのウィンドウで行を選んだときに出す、その訪問の詳しい情報（2026-09-26のユーザー指定→実装メモ5.42）。
///
/// 手首のパネルに出す <see cref="DisplayRow"/> とは分けてある。一緒にいた人の一覧などは長く、
/// 行の内容が同じかどうか（＝描き直すかどうか）の比べ合いに入れると、同じ行でも毎回「変わった」ことになるため。
///
/// 中身はすべて主ループで写し取った変わらない値で、ウィンドウのスレッドへそのまま渡せる。
/// </summary>
/// <param name="LaunchUrl">「ここへ戻る」（VRChatで開く）の URL（→5.43）。作れない location なら null。</param>
/// <param name="HereNow">いま滞在しているインスタンスか（「ここへ戻る」を押せない）。</param>
/// <param name="WebUrl">「ブラウザで開く」（既定→5.53）の URL。作れない location なら null。</param>
/// <param name="GroupName">利用者が付けたグループ名（→5.48）。付けていない・グループでなければ null。</param>
/// <param name="Companions">滞在中に一緒にいた人（→5.46）。自分を除く、最初に見えた順。</param>
/// <param name="Photos">滞在中に撮った写真（→5.47）。古い順。</param>
public sealed record RowDetail(
    string EventId,
    string WorldId,
    string InstanceId,
    string? LaunchUrl,
    bool HereNow,
    string? GroupId,
    string? GroupName,
    IReadOnlyList<Companion> Companions,
    IReadOnlyList<VisitPhoto> Photos,
    string? WebUrl = null)
{
    /// <summary>写真のフォルダーを開くときに選ぶ写真（いちばん新しいもの）。写真がなければ null。</summary>
    public VisitPhoto? LatestPhoto => Photos.Count > 0 ? Photos[^1] : null;

    /// <summary>行のボタンを押せるか。設定の開き方で決まる（→実装メモ5.53）。</summary>
    public bool CanOpen(ReturnAction action)
        => action == ReturnAction.VrChat ? LaunchUrl is not null && !HereNow : WebUrl is not null;

    /// <summary>行の中身の比べ合いは <see cref="EventId"/> と各値で行う（一覧は数と中身で比べる）。</summary>
    public bool Equals(RowDetail? other)
        => other is not null
           && EventId == other.EventId
           && WorldId == other.WorldId
           && InstanceId == other.InstanceId
           && LaunchUrl == other.LaunchUrl
           && WebUrl == other.WebUrl
           && HereNow == other.HereNow
           && GroupId == other.GroupId
           && GroupName == other.GroupName
           && Companions.SequenceEqual(other.Companions)
           && Photos.SequenceEqual(other.Photos);

    public override int GetHashCode() => HashCode.Combine(EventId, Companions.Count, Photos.Count, GroupName, HereNow);
}

public static class RowDetails
{
    /// <summary>
    /// 行ごとの詳しい情報を作る。訪問の記録は主ループだけが書き換えるので、一覧は配列へ写してから渡す。
    /// </summary>
    public static List<RowDetail> Build(IReadOnlyList<VisitRecord> records, string? currentEventId, GroupNaming? groups = null)
    {
        var naming = groups ?? GroupNaming.None;
        var currentLocation = RowFormatter.CurrentLocationKey(records, currentEventId);
        var details = new List<RowDetail>(records.Count);

        foreach (var r in records)
        {
            var here = currentLocation is not null && string.Equals(r.LocationKey, currentLocation, StringComparison.Ordinal);

            details.Add(new RowDetail(
                r.EventId,
                r.WorldId,
                r.InstanceId,
                InstanceLaunch.UrlFor(r.Location),
                here,
                r.GroupId,
                naming.NameOf(r.GroupId),
                [.. r.Companions],
                [.. r.Photos],
                InstanceLaunch.WebUrlFor(r.Location)));
        }

        return details;
    }
}
