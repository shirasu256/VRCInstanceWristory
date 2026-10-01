namespace VRCInstanceWristory.Core.Presentation;

/// <summary>
/// 帯の文字の一部と、その色（2026-09-21のユーザー指定→実装メモ5.30）。
/// <paramref name="Crash"/> が true の部分だけを赤で描く。
/// </summary>
public readonly record struct BandSegment(string Text, bool Crash);
