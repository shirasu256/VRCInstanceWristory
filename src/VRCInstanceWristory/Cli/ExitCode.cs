namespace VRCInstanceWristory.Cli;

/// <summary>終了コード。2・3 はいまは使っていない。</summary>
public static class ExitCode
{
    public const int Success = 0;

    /// <summary>予期しないエラー・フレーム処理の失敗が続いた。</summary>
    public const int Failure = 1;

    /// <summary>既に起動していた（1つ目へ知らせて終わった）か、旧名の版が起動していた（→実装メモ5.52・5.84）。</summary>
    public const int AlreadyRunning = 4;
}
