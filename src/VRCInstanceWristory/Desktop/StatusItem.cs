namespace VRCInstanceWristory.Desktop;

/// <summary>状態の段の1項目。<see cref="Detail"/> は値を指したときに出す（なければ出さない）。</summary>
public sealed record StatusItem(string Label, string Value, StatusTone Tone, string? Detail = null);
