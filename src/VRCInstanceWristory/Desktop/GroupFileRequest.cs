namespace VRCInstanceWristory.Desktop;

/// <summary>グループ名のファイルを選んでほしい、という依頼（→実装メモ5.65）。</summary>
public enum GroupFileRequest
{
    None,

    /// <summary>読み込むファイルを選ぶ。</summary>
    Import,

    /// <summary>書き出すファイルを選ぶ。</summary>
    Export,
}
