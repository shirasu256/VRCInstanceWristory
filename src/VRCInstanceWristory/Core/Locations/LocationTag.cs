namespace VRCInstanceWristory.Core.Locations;

/// <summary><c>~key(value)</c> または <c>~flag</c>。</summary>
/// <param name="Value">フラグの場合は null。</param>
public readonly record struct LocationTag(string Key, string? Value)
{
    public override string ToString() => Value is null ? Key : $"{Key}={Value}";
}
