namespace VRCInstanceWristory.Core;

/// <summary>VRChat のログのフォルダーの様子（→実装メモ5.85）。</summary>
public enum LogFolderState
{
    /// <summary>フォルダーがあり、ログのファイルもある。</summary>
    Ok,

    /// <summary>フォルダーがない。</summary>
    Missing,

    /// <summary>フォルダーはあるが、ログのファイルがない。</summary>
    Empty,
}
