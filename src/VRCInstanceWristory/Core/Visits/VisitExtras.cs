namespace VRCInstanceWristory.Core.Visits;

/// <summary>
/// その訪問で同じインスタンスにいた人1人ぶん（2026-09-26のユーザー指定→実装メモ5.46）。
///
/// 人数（<see cref="VisitRecord.PeopleCount"/>）を出すために追っている <c>OnPlayerJoined</c> / <c>OnPlayerLeft</c>
/// から作る。自分自身（<c>User Authenticated</c> の本人）は含めない。
/// </summary>
/// <param name="UserId">鍵。表示名は変えられるうえ重複もし得るので、userId で見分ける。</param>
/// <param name="Name">最後に見えた表示名。</param>
/// <param name="FirstSeenUtc">この訪問の中で最初に見えた時刻。</param>
/// <param name="LeftAtUtc">
/// こちらより先にインスタンスを出た時刻。こちらが離れるときにまだいた人は null。
/// 一度出て戻ってきた人も、戻った時点で null に戻す。
/// </param>
public sealed record Companion(string UserId, string Name, DateTime FirstSeenUtc, DateTime? LeftAtUtc);

/// <summary>その訪問の間に撮った写真1枚（2026-09-26のユーザー指定→実装メモ5.47）。</summary>
/// <param name="Path">保存先。ログの書き方（/ と \ の混在）を \ にそろえてある。</param>
/// <param name="TakenAtUtc">ログに書かれた時刻。</param>
public sealed record VisitPhoto(string Path, DateTime TakenAtUtc);

/// <summary>訪問の行へ書き戻す「一緒にいた人」の1件。EventId は入室成功行のeventId。</summary>
public readonly record struct VisitCompanion(string EventId, Companion Companion);

/// <summary>訪問の行へ書き戻す写真の1枚。EventId は入室成功行のeventId。</summary>
public readonly record struct VisitPhotoTaken(string EventId, VisitPhoto Photo);
