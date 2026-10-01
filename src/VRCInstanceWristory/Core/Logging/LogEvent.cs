namespace VRCInstanceWristory.Core.Logging;

public enum LogEventKind
{
    /// <summary>訪問・現在地の判断に使わない行。</summary>
    Other,

    /// <summary>[Behaviour] Entering Room: {worldName} — 世界名の候補。</summary>
    EnteringRoom,

    /// <summary>[Behaviour] Joining {location} — 未確定の入室候補。</summary>
    Joining,

    /// <summary>[Behaviour] Successfully joined room — 候補を入室として確定する。</summary>
    JoinedRoom,

    /// <summary>[Behaviour] Joining or Creating Room: {name} — 名前の参考。候補にしない。</summary>
    JoiningOrCreatingRoom,

    /// <summary>
    /// [Behaviour] Finished entering world. — ロード画面が終わり、世界が見えるようになった。
    /// 入室の確定（<see cref="JoinedRoom"/>）より数秒あとに出る。
    /// パネルを閉じる起点をここに置く（→5.31節）。
    /// </summary>
    FinishedEnteringWorld,

    /// <summary>[Behaviour] Destination set: {location} — 遷移開始。</summary>
    DestinationSet,

    /// <summary>[Behaviour] OnLeftRoom</summary>
    LeftRoom,

    /// <summary>
    /// [Behaviour] OnPlayerJoined {表示名} ({userId}) — 誰かがインスタンスへ入った。
    /// 入室直後には、そのとき居合わせた全員ぶんがまとめて並ぶ。
    /// これと <see cref="PlayerLeft"/> で在室者を常に追跡し、人数を求める（5.30節）。
    /// Payload に「表示名 (userId)」がそのまま入る。
    /// </summary>
    PlayerJoined,

    /// <summary>
    /// [Behaviour] OnPlayerLeft {表示名} ({userId}) — 誰かがインスタンスから出た。
    /// Payload に「表示名 (userId)」がそのまま入る。
    /// </summary>
    PlayerLeft,

    /// <summary>[Behaviour] OnDisconnected: {reason}</summary>
    Disconnected,

    /// <summary>移動の失敗。候補を破棄する。</summary>
    JoinFailed,

    /// <summary>
    /// VRCApplication: HandleApplicationQuit / OnApplicationQuit — 終了処理開始。
    /// この行のないまま終わったセッションはクラッシュとして扱う（→5.30節）。
    /// </summary>
    ApplicationQuit,

    /// <summary>
    /// VP &lt;ページ&gt; OnPageAboutToShow() — パネルを出すきっかけになるメインメニューのページを開いた。
    /// Payload にページ名が入る。
    /// </summary>
    MainMenuPageShown,

    /// <summary>
    /// VP MainMenu&lt;ページ&gt; OnWillCloseAllChildPages() — メインメニューのページが子のページを閉じようとしている。
    /// 直後（次の VP の行）に同じページの <see cref="MainMenuPageHiding"/> が続けば、メインメニューを閉じた（→実装メモ5.104）。
    /// Payload にページ名が入る。
    /// </summary>
    MainMenuClosing,

    /// <summary>
    /// VP MainMenu&lt;ページ&gt; OnPageAboutToHide() — メインメニューのページが隠れようとしている。
    /// タブの切り替えでも出るので、これ1行では閉じたと見なさない（→実装メモ5.104）。Payload にページ名が入る。
    /// </summary>
    MainMenuPageHiding,

    /// <summary>
    /// 上のどれにも当たらない VP &lt;ページ&gt; の行。<see cref="MainMenuClosing"/> と <see cref="MainMenuPageHiding"/> が
    /// 隣り合っているかを見るためだけに使う（→実装メモ5.104）。Payload にページ名が入る。
    /// </summary>
    MenuPageOther,

    /// <summary>
    /// User Authenticated: {表示名} ({userId}) — このログセッションでログインしている利用者本人。
    /// 「一緒にいた人」から自分を除くのに使う（→実装メモ5.46）。Payload に「表示名 (userId)」が入る。
    /// </summary>
    UserAuthenticated,

    /// <summary>
    /// [VRC Camera] Took screenshot to: {path} — 写真を撮った。その訪問へ紐付ける（→実装メモ5.47）。
    /// Payload に保存先のパスがそのまま入る（区切りに / と \ が混ざる）。
    /// </summary>
    Screenshot,

    /// <summary>
    /// StartVRSDK: {読み込んだもの} — VR の方式（実ログでは SteamVR のとき <c>OpenVRLoader</c>）。
    /// SteamVR 以外の経路（OpenXR）で VR モードの VRChat が動いているかを見分けるのに使う（→実装メモ5.85）。
    /// 訪問・現在地の判断には使わない。
    /// </summary>
    VrSdkStarted,
}

/// <param name="Payload">世界名・location文字列など、種別ごとの引数部分。</param>
public readonly record struct LogEvent(LogEventKind Kind, LogLine Line, string Payload)
{
    public static LogEvent Other(LogLine line) => new(LogEventKind.Other, line, string.Empty);
}
