namespace VRCInstanceWristory.Core;

/// <summary>期限切れ判定・未来時刻の除外に使う現在時刻。検証では差し替える。</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;
}
