using System.Globalization;
using VRCInstanceWristory.Core.Locations;

namespace VRCInstanceWristory.Core.Visits;

/// <summary>
/// 対象インスタンスへの入室成功1件（仕様9.2節）。
/// groupId と worldName は完全な値を保持し、短縮表記は描画時に導出する。
/// </summary>
public sealed class VisitRecord
{
    /// <summary>sourceSessionId + 成功行の先頭バイト位置（仕様5.3節）。二重適用の防止に使う。</summary>
    public required string EventId { get; init; }

    public required string SourceSessionId { get; init; }

    /// <summary>成功行の先頭バイト位置。</summary>
    public required long SuccessByteOffset { get; init; }

    /// <summary>ログセッションの並び順。同秒の安定した整列に使う。</summary>
    public required int SessionOrder { get; init; }

    public required string LocationKey { get; init; }

    public required string WorldId { get; init; }

    /// <summary>表示ID。先頭ゼロ・大小文字をそのまま保つ（R07）。</summary>
    public required string InstanceId { get; init; }

    public required AccessType AccessType { get; init; }

    /// <summary>Entering Room から対応づけた世界名。取得できなければ null。</summary>
    public string? WorldName { get; init; }

    public string? Region { get; init; }

    /// <summary>完全なGroup ID。</summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// ログの <c>Joining</c> に書かれていた location 文字列そのもの（タグの並びもログのまま）。
    /// 「ここへ戻る」で VRChat へ渡す（2026-09-26のユーザー指定→実装メモ5.43）。
    /// <see cref="LocationKey"/> はタグを並べ替えた比較用の値なので、渡すのはこちら。
    /// </summary>
    public string? Location { get; init; }

    public required DateTime VisitedAtUtc { get; init; }

    /// <summary>
    /// この訪問で対象インスタンスを離れた時刻（UTC）。まだ滞在中なら null。
    /// 別の対象インスタンスへ移った場合も離脱として記録する（<see cref="VisitTracker"/>）。
    /// 表示の「入室 - 退出」の右側に使う（2026-09-20のユーザー指定）。
    /// </summary>
    public DateTime? LeftAtUtc { get; set; }

    /// <summary>
    /// 退出した時点でそのインスタンスにいた人数。自分自身を含む。まだ分からなければ null。
    /// 滞在中ずっと追いかけている在室者の数から求める（2026-09-21のユーザー指定・5.30節）。
    /// 滞在中の行と、在室者を1人も把握できなかった行は null のまま。
    /// </summary>
    public int? PeopleCount { get; set; }

    /// <summary>
    /// この訪問が VRChat のクラッシュ（正常終了のログを残さない終わり方）で終わったか
    /// （2026-09-21のユーザー指定・5.30節）。
    ///
    /// true の行は <see cref="LeftAtUtc"/> をログではなくプロセスの消滅時刻
    /// （それも分からなければログの最後の時刻）から入れているため、表示では退出時刻を赤くする。
    /// 次の行との間には「∧ VRChatクライアントがクラッシュしました ∨」の帯を入れる。
    /// </summary>
    public bool EndedByCrash { get; set; }

    /// <summary>
    /// 同じログセッションの1つ前の対象訪問からこの訪問までの間に、対象外のインスタンスへの
    /// 入室が確定していたか（2026-09-26のユーザー指定→実装メモ5.38）。
    /// セッションの最初の対象訪問では、セッションの始まりからこの訪問までを見る。
    /// </summary>
    public bool ExcludedBefore { get; init; }

    /// <summary>
    /// この訪問を離れたあと、同じログセッションの中で対象外のインスタンスへの入室が確定したか
    /// （2026-09-26のユーザー指定→実装メモ5.38）。
    /// 対象外へ移ったまま終了し、次の起動で直接対象へ入った場合も、前後の行の間に帯を入れられるようにする。
    /// </summary>
    public bool ExcludedAfter { get; set; }

    /// <summary>
    /// 滞在中に同じインスタンスにいた人（自分を除く・最初に見えた順）。
    /// デスクトップのウィンドウで行を選ぶと一覧を出す（2026-09-26のユーザー指定→実装メモ5.46）。
    /// 書き換えるのは主ループ（<see cref="History.HistoryStore.SetCompanion"/>）だけで、表示側へは写しを渡す。
    /// </summary>
    public List<Companion> Companions { get; } = [];

    /// <summary>
    /// 滞在中に撮った写真（古い順）。行に写真の印と枚数を出し、ウィンドウからそのフォルダーを開ける
    /// （2026-09-26のユーザー指定→実装メモ5.47）。
    /// </summary>
    public List<VisitPhoto> Photos { get; } = [];

    /// <summary>その訪問時点の回数。null は「回数不明」（仕様3.4節）。</summary>
    public int? VisitOrdinal { get; set; }

    /// <summary>回数を付けたカウント期間のID。</summary>
    public string? CounterEpochId { get; set; }

    public string? ShortGroupId => ParsedLocation.FormatShortGroupId(GroupId);

    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{VisitedAtUtc:HH:mm:ss}-{(LeftAtUtc is { } left ? left.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "--:--:--")} {InstanceId} {AccessType.DisplayName()} n={(VisitOrdinal is { } n ? n : "?")} p={(PeopleCount is { } p ? p : "?")}");
}
