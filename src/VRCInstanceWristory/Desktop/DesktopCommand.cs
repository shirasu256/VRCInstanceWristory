using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Desktop;

/// <summary>ウィンドウでの操作。ウィンドウのスレッドから主ループへ渡す。</summary>
public abstract record DesktopCommand
{
    /// <summary>見出しの「延長」を押した（VR内で押したのと同じ）。</summary>
    public sealed record ExtendRetention : DesktopCommand;

    /// <summary>
    /// 初回起動の案内と初期設定を終えた（初期設定の画面の「はじめる」を押した→<see cref="DesktopView.ShowWelcome"/>・実装メモ5.97・5.98）。
    /// 次からは案内を出さない。
    /// </summary>
    public sealed record FinishWelcome : DesktopCommand;

    /// <summary>ユーザーの vrchat.com のページ（<c>https://vrchat.com/home/user/usr_…</c>）をブラウザで開く（→実装メモ5.75）。</summary>
    public sealed record OpenUserPage(string UserId) : DesktopCommand;

    /// <summary>ステータスの段の右端の「shirasu256」を押した（→実装メモ5.71）。開発者のページをブラウザで開く。</summary>
    public sealed record OpenDeveloperPage : DesktopCommand;

    /// <summary>見出しの「リセット」を押し、確認で「リセット」を選んだ（VR内と同じ→実装メモ5.65）。訪問履歴を今すぐ消す。</summary>
    public sealed record ClearHistory : DesktopCommand;

    /// <summary>行の目印を選んだ（VR内で選んだのと同じ。いま付いている印なら外れる）。</summary>
    public sealed record SetMark(string EventId, InstanceMark Mark) : DesktopCommand;

    /// <summary>設定を変えた。<see cref="Fields"/> が変わった項目。</summary>
    public sealed record ChangeSettings(DesktopSettings Settings, SettingsField Fields) : DesktopCommand;

    /// <summary>手首のパネルの配置（位置と角度）を既定値へ戻す（「手首パネルの位置をデフォルトに戻す」→実装メモ5.86）。</summary>
    public sealed record ResetPlacement : DesktopCommand;

    /// <summary>直前の履歴のリセットを戻す（「直前のリセットを戻す」→実装メモ5.86）。</summary>
    public sealed record UndoClearHistory : DesktopCommand;

    /// <summary>
    /// 行のインスタンスを開く（→実装メモ5.43・5.53）。主ループがその訪問の location から、設定の開き方
    /// （既定はWebページをブラウザで。<c>returnAction</c> が <c>vrchat</c> なら launch URL）で開く。
    /// </summary>
    public sealed record OpenInstance(string EventId) : DesktopCommand;

    /// <summary>グループの vrchat.com のページ（<c>https://vrchat.com/home/group/grp_…</c>）をブラウザで開く（→実装メモ5.69）。</summary>
    public sealed record OpenGroupPage(string GroupId) : DesktopCommand;

    /// <summary>写真を1枚、設定のアプリ（既定はその種類のファイルの既定のアプリ）で開く（→実装メモ5.55）。</summary>
    public sealed record OpenPhoto(string Path) : DesktopCommand;

    /// <summary>その訪問で撮った写真のフォルダーを開く（→実装メモ5.47）。<see cref="Path"/> はいちばん新しい写真。</summary>
    public sealed record OpenPhotoFolder(string Path) : DesktopCommand;

    /// <summary>グループに名前を付ける（→実装メモ5.48）。<see cref="Name"/> が空なら名前を外す。</summary>
    public sealed record SetGroupName(string GroupId, string? Name) : DesktopCommand;

    /// <summary>
    /// ファイルから読んだグループ名を取り込む（→実装メモ5.65）。同じ Group ID の名前は読んだほうで置き換え、ほかは残す。
    /// ファイルを読んで検めるのはウィンドウのスレッド、設定へ書くのは主ループ。
    /// </summary>
    public sealed record ImportGroupNames(IReadOnlyDictionary<string, string> Names) : DesktopCommand;

    /// <summary>付けてあるグループ名をファイルへ書き出す（→実装メモ5.65）。</summary>
    public sealed record ExportGroupNames(string Path) : DesktopCommand;

    /// <summary>文字をクリップボードへ写す（外部連携のコマンドの「コピー」→実装メモ5.83）。</summary>
    public sealed record CopyText(string Text) : DesktopCommand;

    /// <summary>「今すぐ確認」を押した（→実装メモ5.121）。新しい版を確かめる。</summary>
    public sealed record CheckForUpdates : DesktopCommand;

    /// <summary>
    /// 「更新して再起動」（設定か状態の段のリンク）を押し、確かめる画面で「はい」を選んだ（→実装メモ5.121）。
    /// 主ループが新しい版を落とし、落とし終えたら保存を済ませて終わる。入れ替えと起動し直しは Velopack が行う。
    /// </summary>
    public sealed record ApplyUpdate : DesktopCommand;

    /// <summary>
    /// 2つ目の起動があった（→実装メモ5.52）。SteamVRにつながっていなければ、すぐにつなげるか確かめる。
    /// <see cref="FromSteamVr"/> なら SteamVR が起動のついでに開いたもの。
    /// </summary>
    public sealed record AnotherLaunch(bool FromSteamVr) : DesktopCommand;
}
