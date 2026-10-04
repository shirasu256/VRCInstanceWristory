using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;

namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 1行ぶんの表示内容（仕様3.2・8.1節）。
/// 左にID、右に3段の付加情報。世界名の省略は描画時に幅で判断する。
/// </summary>
/// <param name="StayFraction">
/// 滞在時間を既定の保持時間（<see cref="History.HistoryStore.DefaultRetention"/>・60分）で割った割合（0〜1）。
/// 印の列に出す棒の長さに使う。長く滞在したインスタンスほど棒が長くなる。
/// 1分単位に丸めてあるので、滞在中の行でもこの値が変わるのは1分に1回までとなり、
/// 行の下絵を描き直す回数が増えない（→実装メモ5.28・5.35）。
/// </param>
/// <param name="ExcludedBefore">
/// この行の1つ前の行を離れてから、この行へ入るまでに、対象外のインスタンスへ
/// 一瞬でも移ったか（2026-09-26のユーザー指定→実装メモ5.38）。先頭の行は常に false。
/// true なら、描画側がこの行の上へ「∧ 対象外のインスタンスへ移動 ∨」の帯を入れる。
/// </param>
/// <param name="CrashedBefore">
/// 1つ前の行を離れてからこの行へ入るまでに VRChat がクラッシュしたか（2026-09-21のユーザー指定。
/// 対象外のインスタンスにいる間のクラッシュも含める→実装メモ5.129）。
/// true なら、描画側がこの行の上の帯へ「VRChat クライアントクラッシュ」を入れ、
/// その部分だけを赤で出す。<see cref="ExcludedBefore"/> と両方が立つ場合は、
/// 帯を2枚にせず `∧ VRChat クライアントクラッシュ・対象外のインスタンスへ移動 ∨` と
/// 1行にまとめる（→実装メモ5.30）。
/// </param>
/// <param name="ExcludedBeforeCrash">
/// <see cref="ExcludedBefore"/> と <see cref="CrashedBefore"/> の両方が立つとき、対象外への移動のほうが先に起きたか
/// （2026-10-05のユーザー指定→実装メモ5.129）。true なら帯を起きた順に
/// `∧ 対象外のインスタンスへ移動・VRChat クライアントクラッシュ ∨` とする。
/// </param>
/// <param name="Mark">
/// このインスタンスに付けた目印（2026-09-22のユーザー指定→実装メモ5.32）。
/// 目印は1回の訪問ではなくインスタンスに付くので、同じインスタンスの行はすべて同じ値になる。
/// <see cref="InstanceMark.None"/> 以外なら、描画側がIDの後ろにその印を描く。
/// </param>
/// <param name="AgoText">
/// 退出してから今までの分数（`(9分前)`・2026-09-25のユーザー指定→実装メモ5.36）。
/// 1段目の時刻の後ろに出す。まだ滞在している行（退出時刻がない）は空文字。
/// 分単位でしか変わらないので、行を作り直すのは1分に1回で足りる。
/// </param>
/// <param name="JoinText">
/// 入室した時刻（`HH:mm`）。見出しの「訪問履歴 (全8件 00:14~)」に、先頭の行のものを出す
/// （2026-09-25のユーザー指定→実装メモ5.36）。
/// </param>
/// <param name="JoinDateText">
/// 入室した日付と時刻（`yyyy-MM-dd HH:mm`）。履歴の自動リセットを無効にしている間は、見出しに日付まで出す（→実装メモ5.71）。
/// </param>
/// <param name="PhotoCount">
/// 滞在中に撮った写真の枚数（2026-09-26のユーザー指定→実装メモ5.47）。1枚以上なら、1段目の右端に写真の印と枚数を出す。
/// </param>
/// <param name="Returnable">
/// 目印のポップアップの「ここへ戻る」（VRChatで開く）を押せるか（2026-09-26のユーザー指定→実装メモ5.43）。
/// ログの location から launch URL を作れて、いま滞在しているインスタンスではないときだけ true。
/// </param>
/// <param name="Linkable">
/// 「ブラウザで開く」（インスタンスのWebページ・既定→実装メモ5.53）を押せるか。
/// location から URL を作れれば、いま滞在しているインスタンスでも true（ページを見るだけで VRChat には何も起きない）。
/// </param>
public sealed record DisplayRow(
    string EventId,
    string InstanceId,
    string TimeText,
    string OrdinalText,
    string WorldName,
    string TypeText,
    bool IsCurrent,
    string PeopleText = "",
    float StayFraction = 0f,
    bool ExcludedBefore = false,
    bool CrashedBefore = false,
    InstanceMark Mark = InstanceMark.None,
    string AgoText = "",
    string JoinText = "",
    int PhotoCount = 0,
    bool Returnable = false,
    bool Linkable = false,
    string JoinDateText = "",
    bool ExcludedBeforeCrash = false)
{
    /// <summary>行のボタン（「ブラウザで開く」／「ここへ戻る」）を押せるか。設定の開き方で決まる（→実装メモ5.53）。</summary>
    public bool CanOpen(ReturnAction action) => action == ReturnAction.VrChat ? Returnable : Linkable;
}
