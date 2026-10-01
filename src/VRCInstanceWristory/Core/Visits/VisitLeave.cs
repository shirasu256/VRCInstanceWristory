namespace VRCInstanceWristory.Core.Visits;

/// <summary>
/// 対象インスタンスを離れた1回ぶん。EventId は離れた訪問（入室成功行）のeventId。
/// </summary>
public readonly record struct VisitLeave(string EventId, DateTime AtUtc);
