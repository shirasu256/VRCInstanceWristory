namespace VRCInstanceWristory.Core.Visits;

/// <summary>
/// 退出時にそのインスタンスにいた人数。EventId は離れた訪問（入室成功行）のeventId。
/// Count は離れた時点の在室者の数で、自分自身を含む（→実装メモ5.30）。
/// </summary>
public readonly record struct VisitPeopleCount(string EventId, int Count);
