using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Logging;

namespace VRCInstanceWristory.Core.Visits;

/// <summary>
/// 1つのログセッション内で、Joining候補と成功行の対応・現在地・世界名候補を管理する（仕様6節）。
/// カウンターへの適用や履歴の重複除去は呼び出し側が行う。
/// </summary>
public sealed class VisitTracker
{
    /// <summary>候補の有効期間（実装上の開始値）。</summary>
    public static readonly TimeSpan DefaultJoinWindow = TimeSpan.FromSeconds(30);

    /// <summary>世界名候補の有効期間。</summary>
    public static readonly TimeSpan DefaultWorldNameWindow = TimeSpan.FromSeconds(30);

    /// <summary>ログ時刻が現在より先でも許す誤差。これを超える行は壊れた日時として扱う。</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(5);

    private readonly string _sourceSessionId;
    private readonly int _sessionOrder;
    private readonly LogTimeConverter _time;
    private readonly IClock _clock;
    private readonly TimeSpan _joinWindow;
    private readonly TimeSpan _nameWindow;

    /// <summary>記録する種類（→<see cref="TargetAccessTypes"/>・実装メモ5.39）。</summary>
    private readonly IReadOnlySet<AccessType> _targets;

    private PendingJoin? _pending;
    private WorldNameCandidate? _worldName;
    private readonly List<DateTime> _targetLeaves = [];

    /// <summary>このセッションで最後に入った対象訪問のeventId。対象外へ移ったことをその行へ書き戻すのに使う。</summary>
    private string? _lastTargetEventId;

    /// <summary>
    /// 最後の対象訪問（なければセッションの始まり）から、対象外のインスタンスへの入室が確定したか
    /// （2026-09-26のユーザー指定→実装メモ5.38）。次の対象訪問の <see cref="VisitRecord.ExcludedBefore"/> になる。
    /// </summary>
    private bool _excludedSinceTarget;

    /// <summary>
    /// いまのインスタンスの在室者（自分を含む）。OnPlayerJoined / OnPlayerLeft で常に出し入れする。
    /// クラッシュのように退出のログが残らない終わり方でも人数が分かるよう、
    /// 退出のときに数えるのではなく滞在中ずっと持つ（2026-09-21のユーザー指定→実装メモ5.30）。
    /// </summary>
    private readonly HashSet<string> _roster = new(StringComparer.Ordinal);

    /// <summary>
    /// いまの対象訪問で見えた人（自分を除く）。鍵は userId。入室が確定するたびに作り直す
    /// （2026-09-26のユーザー指定→実装メモ5.46）。人数の <see cref="_roster"/> と違い、先に出た人も残す。
    /// </summary>
    private readonly Dictionary<string, Companion> _companions = new(StringComparer.Ordinal);

    /// <summary>このログセッションでログインしている利用者本人の userId（User Authenticated）。分からなければ null。</summary>
    private string? _selfUserId;

    /// <summary>
    /// 読み取れる「Joining」の行がないまま「Successfully joined room」が続いた回数（→実装メモ5.85）。
    /// 読み取れる入室があれば0へ戻す。
    /// </summary>
    private int _unmatchedJoins;

    /// <summary>
    /// 直前の VP の行が、メインメニューのページの OnWillCloseAllChildPages() だったときのページ名（→実装メモ5.104）。
    /// 次の VP の行が同じページの OnPageAboutToHide() なら、メインメニューを閉じたと見なす。
    /// </summary>
    private string? _closingMenuPage;

    public VisitTracker(
        string sourceSessionId,
        int sessionOrder,
        LogTimeConverter time,
        IClock clock,
        TimeSpan? joinWindow = null,
        TimeSpan? worldNameWindow = null,
        IReadOnlySet<AccessType>? targets = null)
    {
        _sourceSessionId = sourceSessionId;
        _sessionOrder = sessionOrder;
        _time = time;
        _clock = clock;
        _joinWindow = joinWindow ?? DefaultJoinWindow;
        _nameWindow = worldNameWindow ?? DefaultWorldNameWindow;
        _targets = targets ?? TargetAccessTypes.Default;
    }

    public PresenceState State { get; private set; } = PresenceState.Unknown;

    /// <summary>確定している現在地。InTarget / InExcluded のときだけ意味を持つ。</summary>
    public ParsedLocation? CurrentLocation { get; private set; }

    /// <summary>現在の入室行のeventId。`▶` を付ける行の特定に使う。</summary>
    public string? CurrentEventId { get; private set; }

    /// <summary>
    /// このセッションで対象インスタンスを離れた時刻（UTC）。古い順に並ぶ。
    /// 履歴の保持期限はここから測る（2026-09-18のユーザー指定）。
    /// 別の対象インスタンスへ移った場合も「いったん離れた」として記録する。
    /// </summary>
    public IReadOnlyList<DateTime> TargetLeaves => _targetLeaves;

    /// <summary>
    /// 直前の <see cref="Apply"/> で対象インスタンスを離れたときの、その訪問のeventIdと退出時刻。
    /// 離れていなければ null。呼び出し側が履歴の該当行へ退出時刻を書き戻すために使う
    /// （2026-09-20のユーザー指定の「入室 - 退出」表示）。
    /// </summary>
    public VisitLeave? LastLeave { get; private set; }

    /// <summary>
    /// 直前の <see cref="Apply"/> または <see cref="EndSession"/> で分かった「退出時にいた人数」。
    /// 離れていなければ null。離れた時点の在室者の数を1回だけ報告する。
    /// 在室者を1人も把握できていないとき（ログの途中から読んだ場合など）は、
    /// 0人と断定せずに報告しない（2026-09-21のユーザー指定→実装メモ5.30）。
    /// </summary>
    public VisitPeopleCount? LastPeopleCount { get; private set; }

    /// <summary>
    /// 直前の <see cref="Apply"/> で対象外のインスタンスへの入室が確定したとき、
    /// このセッションで最後に入っていた対象訪問のeventId。なければ null。
    /// 呼び出し側がその行へ <see cref="VisitRecord.ExcludedAfter"/> を書き戻すために使う
    /// （2026-09-26のユーザー指定→実装メモ5.38）。
    /// </summary>
    public string? LastExcludedAfter { get; private set; }

    /// <summary>
    /// 直前の <see cref="Apply"/> で入室が確定した、記録しない種類の訪問。なければ null。
    /// 行にはしないが、回数だけは数える（2026-09-27のユーザー指定→実装メモ5.58）。
    /// あとから記録する種類を増やしてログを読み直したとき、その種類の過去の訪問が「回数不明」にならないようにするため。
    /// 選べない種類（解析できない・未知）は数えない。
    /// </summary>
    public VisitRecord? LastExcludedVisit { get; private set; }

    /// <summary>
    /// 直前の <see cref="Apply"/> で変わった「一緒にいた人」の1件（入った・出た・名前が変わった）。なければ null。
    /// 呼び出し側がその訪問の行へ書き戻す（2026-09-26のユーザー指定→実装メモ5.46）。
    /// 対象インスタンスに滞在している間のものだけを報告する（こちらが離れたあとに並ぶ OnPlayerLeft は数えない）。
    /// </summary>
    public VisitCompanion? LastCompanion { get; private set; }

    /// <summary>
    /// 直前の <see cref="Apply"/> で撮った写真。対象インスタンスに滞在している間のものだけ。なければ null
    /// （2026-09-26のユーザー指定→実装メモ5.47）。
    /// </summary>
    public VisitPhotoTaken? LastPhoto { get; private set; }

    /// <summary>HandleApplicationQuit の時刻（UTC）。終了処理開始の記録であり、OS上の終了時刻ではない。</summary>
    public DateTime? QuitAtUtc { get; private set; }

    /// <summary>このセッションで最後に解析できたログ行の時刻（UTC）。診断用。</summary>
    public DateTime? LastEventAtUtc { get; private set; }

    /// <summary>
    /// パネルを出してよい状態か。メインメニューの対象ページ（ワールド・Live Now・ソーシャル・
    /// VRChat Plus）を開いた（OnPageAboutToShow）と true になる。
    ///
    /// 閉じる側は 2026-09-16 のユーザー指定で変更した。
    /// ページを閉じるログ（OnPageAboutToHide / OnPageHidden）は使わず、
    /// コントローラーの B / Y ボタン（VR側の入力）で閉じる。
    /// 状態が分からないうちは閉じているものとして扱う（勝手に表示しない）。
    /// 2026-10-01 からは、メインメニューを閉じたときにだけ出るログの並び（OnWillCloseAllChildPages() の直後の
    /// OnPageAboutToHide()）でも閉じる。B / Y の押し方を読み違えても、VRChat が閉じたことを取りこぼさない（→実装メモ5.104）。
    ///
    /// 2026-09-21のユーザー指定で、インスタンスからの退出（OnLeftRoom）では閉じなくなった。
    /// さらに 2026-09-22 の指定で、**ロード画面の間はメインメニューを開いているのと同じ扱い**にした。
    /// 退出（OnLeftRoom）でここが true になり、ロード画面が終わったあと
    /// 最初に手首の角度で隠れた時点で閉じる（<see cref="AwaitingViewAngleClose"/>・→5.31節）。
    /// </summary>
    public bool MenuPageOpen { get; private set; }

    /// <summary>
    /// VRChat の更新でログの形式が変わり、インスタンスの行を読み取れていない疑いがあるか（→実装メモ5.85）。
    /// 1回だけなら、ログを途中から読んだ・移動に失敗したなどでも起こるので、2回続いたときにする。
    /// </summary>
    public bool FormatSuspect => _unmatchedJoins >= 2;

    /// <summary>
    /// ロード画面の中か（2026-09-22のユーザー指定）。
    ///
    /// `OnLeftRoom`（退出）から `[Behaviour] Finished entering world.`（世界が見えた）までを
    /// ロード画面とみなし、その間は <see cref="MenuPageOpen"/> を true にする。
    /// 表示に使うのは <see cref="MenuPageOpen"/> のほうで、これは診断用。
    /// </summary>
    public bool InLoadingScreen { get; private set; }

    /// <summary>
    /// ロード画面の間もパネルを出すか（設定 <c>showPanelDuringLoading</c>・既定はオン→実装メモ5.62）。
    ///
    /// オフのときは、ロード画面の始まりでパネルを閉じ、終わっても出し直さない。始まりには
    /// <c>OnLeftRoom</c> より先に出る <c>Destination set</c>（メニューから移動を始めた時点）も使う。
    /// ロード画面の途中で切り替えたら、その場で表示にも反映する。
    /// </summary>
    public bool ShowDuringLoadingScreen
    {
        get => _showDuringLoading;
        set
        {
            if (_showDuringLoading == value)
                return;

            _showDuringLoading = value;

            if (InLoadingScreen)
                MenuPageOpen = value;
        }
    }

    private bool _showDuringLoading = true;

    /// <summary>
    /// ロード画面を抜け、次に手首の角度で隠れたら閉じる状態か（2026-09-21のユーザー指定）。
    ///
    /// ロード画面の終わり（<c>Finished entering world</c>）で true になる。以降は、
    /// メインメニューを開いているかどうかに関係なく、VR側が角度で隠した時点で
    /// <see cref="CloseMenuPage"/> を呼ぶ。閉じたあとは、もう一度メインメニューを開けば出る。
    /// </summary>
    public bool AwaitingViewAngleClose { get; private set; }

    /// <summary>VR側の操作（B / Y ボタン）、メインメニューを閉じたログ（→5.104）、移動後の初回の角度到達で閉じる。</summary>
    public void CloseMenuPage()
    {
        MenuPageOpen = false;
        AwaitingViewAngleClose = false;

        // ロード画面の途中で閉じたなら、そのロードでは出し直さない
        // （明示的に消したものを、世界が見えた時点で戻さない）。
        InLoadingScreen = false;
    }

    /// <summary>
    /// 1イベントを適用する。対象への入室が確定したときだけ <see cref="VisitRecord"/> を返す。
    /// 対象外の入室では null を返し、現在地だけを InExcluded にする。
    /// 対象インスタンスから離れた瞬間は <see cref="TargetLeaves"/> へ記録する。
    /// </summary>
    public VisitRecord? Apply(in LogEvent ev)
    {
        var wasInTarget = State == PresenceState.InTarget;

        // 離脱の検出はイベント適用後だが、そのときには CurrentEventId が消えている。
        // どの訪問を離れたのかを残すため、適用前に控えておく。
        var leavingEventId = CurrentEventId;

        // 人数は「離れると決まった時点」の在室者で数える。実ログでは離脱の判定（Destination set や
        // HandleApplicationQuit）のあとに OnPlayerLeft が並ぶので、適用後に数えると0になってしまう。
        var peopleBefore = _roster.Count;

        LastLeave = null;
        LastPeopleCount = null;
        LastExcludedAfter = null;
        LastExcludedVisit = null;
        LastCompanion = null;
        LastPhoto = null;
        var record = ApplyCore(ev);

        if (wasInTarget && State != PresenceState.InTarget)
        {
            var leftAt = TimeOf(ev.Line);
            _targetLeaves.Add(leftAt);

            if (leavingEventId is not null)
                ReportLeave(leavingEventId, leftAt, peopleBefore);
        }

        return record;
    }

    /// <summary>
    /// クライアントが終わったことをログの外から伝える（2026-09-21のユーザー指定→実装メモ5.30）。
    ///
    /// プロセスが消えた、またはログがそこで途切れている場合に呼ぶ。対象インスタンスに
    /// 滞在したまま終わっていれば、<paramref name="atUtc"/> を退出時刻として報告し、
    /// そのときの在室者の数を人数として残す。クラッシュでは退出のログが残らないので、
    /// ここを通らないと「入室したまま」の行になってしまう。
    ///
    /// 二重に呼ばれても2回目以降は何もしない（<see cref="PresenceState.Ended"/> で止まる）。
    /// </summary>
    public VisitLeave? EndSession(DateTime atUtc)
    {
        LastLeave = null;
        LastPeopleCount = null;

        if (State == PresenceState.Ended)
            return null;

        var leavingEventId = CurrentEventId;
        var wasInTarget = State == PresenceState.InTarget;
        var people = _roster.Count;

        Reset(PresenceState.Ended);
        _roster.Clear();

        if (!wasInTarget || leavingEventId is null)
            return null;

        _targetLeaves.Add(atUtc);
        ReportLeave(leavingEventId, atUtc, people);
        return LastLeave;
    }

    /// <summary>離れたことと、そのときの人数を呼び出し側へ知らせる。</summary>
    private void ReportLeave(string eventId, DateTime atUtc, int people)
    {
        LastLeave = new VisitLeave(eventId, atUtc);

        if (people > 0)
            LastPeopleCount = new VisitPeopleCount(eventId, people);
    }

    private VisitRecord? ApplyCore(in LogEvent ev)
    {
        if (State == PresenceState.Ended)
            return null;

        var line = ev.Line;

        switch (ev.Kind)
        {
            case LogEventKind.EnteringRoom:
                if (TryGetUtc(line, out var nameAt))
                    _worldName = new WorldNameCandidate(ev.Payload, nameAt);
                else
                    _worldName = null;

                Touch(line);
                return null;

            case LogEventKind.Joining:
            {
                // 現在地を変えるイベント。壊れていれば以前の対象状態を残さずUnknownにする。
                if (!TryGetUtc(line, out var joinAt))
                {
                    Reset(PresenceState.Unknown);
                    return null;
                }

                var location = LocationParser.Parse(ev.Payload);
                if (!location.IsValid)
                {
                    Reset(PresenceState.Unknown);
                    return null;
                }

                var name = ConsumeWorldName(joinAt);
                _pending = new PendingJoin(location, joinAt, name);
                CurrentLocation = null;
                CurrentEventId = null;
                State = PresenceState.Transitioning;
                Touch(line);
                return null;
            }

            case LogEventKind.JoinedRoom:
            {
                Touch(line);

                // 新しい部屋に入ったら在室者を数え直す。直後に、居合わせた全員ぶんの
                // OnPlayerJoined がまとめて並ぶので、そこから作り直せる。
                // 「一緒にいた人」も訪問ごとなので、ここで作り直す（→実装メモ5.46）。
                _roster.Clear();
                _companions.Clear();

                if (!TryGetUtc(line, out var joinedAt))
                {
                    Reset(PresenceState.Unknown);
                    return null;
                }

                var pending = _pending;
                _pending = null;
                _worldName = null;

                if (pending is null)
                {
                    // 候補なしの成功。以前の現在地を維持しない。
                    // 続くときは、VRChat の更新で「Joining」の行の形が変わった疑いがある（→実装メモ5.85）。
                    _unmatchedJoins++;
                    Reset(PresenceState.Unknown);
                    return null;
                }

                _unmatchedJoins = 0;

                var delta = joinedAt - pending.JoinedAtUtc;
                if (delta < TimeSpan.Zero || delta > _joinWindow)
                {
                    Reset(PresenceState.Unknown);
                    return null;
                }

                CurrentLocation = pending.Location;
                var eventId = MakeEventId(_sourceSessionId, line.ByteOffset);

                if (!pending.Location.IsValid || !_targets.Contains(pending.Location.AccessType))
                {
                    State = PresenceState.InExcluded;
                    CurrentEventId = null;

                    // 滞在の長さは問わない。入室が確定した時点で「対象外へ移った」とする
                    // （2026-09-26のユーザー指定→実装メモ5.38）。
                    _excludedSinceTarget = true;
                    LastExcludedAfter = _lastTargetEventId;

                    // 記録しない種類でも、選べる種類なら回数は数える（→実装メモ5.58）。
                    if (pending.Location.IsValid && TargetAccessTypes.Selectable.Contains(pending.Location.AccessType))
                        LastExcludedVisit = MakeRecord(eventId, pending, line, joinedAt, excludedBefore: false);

                    return null;
                }

                State = PresenceState.InTarget;
                CurrentEventId = eventId;

                var excludedBefore = _excludedSinceTarget;
                _excludedSinceTarget = false;
                _lastTargetEventId = eventId;

                return MakeRecord(eventId, pending, line, joinedAt, excludedBefore);
            }

            case LogEventKind.DestinationSet:
            {
                Touch(line);

                // 遷移開始。確定状態と古い候補を解除する。
                var valid = TryGetUtc(line, out _) && LocationParser.Parse(ev.Payload).IsValid;
                Reset(valid ? PresenceState.Transitioning : PresenceState.Unknown);

                // ロード画面で出さない設定なら、ここで閉じる（→実装メモ5.62）。
                // メニューから移動するとロード画面はこの行の時点で始まり、OnLeftRoom はその数秒あとに出る。
                if (valid && !_showDuringLoading)
                    HideForLoadingScreen();
                return null;
            }

            case LogEventKind.PlayerJoined:
            {
                Touch(line);
                var player = PlayerRef.Parse(ev.Payload);
                _roster.Add(player.Key);

                // 一緒にいた人（→実装メモ5.46）。一度出て戻ってきた人は、最初に見えた時刻を保ったまま「いる」に戻す。
                if (TryGetCompanionVisit(player.Key, out var visit))
                {
                    var companion = _companions.TryGetValue(player.Key, out var known)
                        ? known with { Name = player.Name, LeftAtUtc = null }
                        : new Companion(player.Key, player.Name, TimeOf(line), null);

                    ReportCompanion(visit, companion);
                }

                return null;
            }

            case LogEventKind.PlayerLeft:
            {
                Touch(line);
                var key = PlayerRef.Parse(ev.Payload).Key;
                _roster.Remove(key);

                // こちらより先に出た人。こちらが離れたあとに並ぶ OnPlayerLeft は、滞在中ではないので数えない。
                if (TryGetCompanionVisit(key, out var visit) && _companions.TryGetValue(key, out var known))
                    ReportCompanion(visit, known with { LeftAtUtc = TimeOf(line) });

                return null;
            }

            case LogEventKind.UserAuthenticated:
                Touch(line);
                _selfUserId = PlayerRef.Parse(ev.Payload).Key;
                return null;

            case LogEventKind.Screenshot:
                Touch(line);

                // 対象インスタンスに滞在している間に撮った写真だけを、その訪問へ紐付ける（→実装メモ5.47）。
                if (State == PresenceState.InTarget && CurrentEventId is { } photoVisit && ev.Payload.Length > 0)
                    LastPhoto = new VisitPhotoTaken(photoVisit, new VisitPhoto(NormalizePhotoPath(ev.Payload), TimeOf(line)));

                return null;

            case LogEventKind.LeftRoom:
                // 退出では閉じない（2026-09-21のユーザー指定）。ここからロード画面が始まり、
                // その間は「メインメニューを開いている」のと同じ扱いにする（2026-09-22の指定）。
                // 閉じていたとしても、ロード画面の間は出す。
                Touch(line);

                InLoadingScreen = true;

                if (_showDuringLoading)
                    MenuPageOpen = true;
                else
                    HideForLoadingScreen();

                _roster.Clear();
                Reset(PresenceState.Unknown);
                return null;

            case LogEventKind.FinishedEnteringWorld:
                // ロード画面の終わり。入室の確定（Successfully joined room）より数秒あとに出る。
                // ここから先は、メインメニューが開いているかどうかに関係なく、
                // 最初に角度で隠れた時点で閉じる（2026-09-22のユーザー指定）。
                Touch(line);

                if (InLoadingScreen)
                {
                    InLoadingScreen = false;

                    // ロード画面で出していなければ、角度で閉じるのを待つものもない。
                    AwaitingViewAngleClose = _showDuringLoading;
                }

                return null;

            case LogEventKind.Disconnected:
                Touch(line);
                _roster.Clear();
                Reset(PresenceState.Unknown);
                return null;

            case LogEventKind.JoinFailed:
                Touch(line);

                // 当該移動の候補を破棄する。まだ離脱・Destination set・新しいJoiningがなければ
                // 既存の確定現在地は維持する。遷移開始後ならUnknownのままにする。
                _pending = null;
                _worldName = null;

                if (State == PresenceState.Transitioning)
                {
                    State = PresenceState.Unknown;
                    CurrentLocation = null;
                    CurrentEventId = null;
                }

                return null;

            case LogEventKind.ApplicationQuit:
                Touch(line);
                if (TryGetUtc(line, out var quitAt))
                    QuitAtUtc = quitAt;

                _roster.Clear();
                Reset(PresenceState.Ended);
                return null;

            case LogEventKind.MainMenuPageShown:
                Touch(line);
                _closingMenuPage = null;
                MenuPageOpen = true;
                return null;

            case LogEventKind.MainMenuClosing:
                _closingMenuPage = ev.Payload;
                return null;

            case LogEventKind.MainMenuPageHiding:
            {
                // 同じページの OnWillCloseAllChildPages() の直後なら、メインメニューを閉じた（→実装メモ5.104）。
                // B / Y の押し方（片方を押し続けたままのもう片方の短押しなど）に関係なく、VRChat が閉じたことをそのまま拾う。
                // ロード画面の間は、メニューとは別に出しているので閉じない（→5.31節）。
                var closed = _closingMenuPage == ev.Payload;
                _closingMenuPage = null;

                if (closed && MenuPageOpen && !InLoadingScreen)
                {
                    Touch(line);
                    CloseMenuPage();
                }

                return null;
            }

            case LogEventKind.MenuPageOther:
                _closingMenuPage = null;
                return null;

            case LogEventKind.JoiningOrCreatingRoom:
                // 名前の参考情報。新しい世界名候補にしない。
                Touch(line);
                return null;

            default:
                return null;
        }
    }

    private VisitRecord MakeRecord(string eventId, PendingJoin pending, LogLine line, DateTime joinedAt, bool excludedBefore) => new()
    {
        EventId = eventId,
        SourceSessionId = _sourceSessionId,
        SuccessByteOffset = line.ByteOffset,
        SessionOrder = _sessionOrder,
        LocationKey = pending.Location.LocationKey,
        WorldId = pending.Location.WorldId,
        InstanceId = pending.Location.InstanceId,
        AccessType = pending.Location.AccessType,
        WorldName = pending.WorldName,
        Region = pending.Location.Region,
        GroupId = pending.Location.GroupId,
        Location = pending.Location.Raw.Trim(),
        VisitedAtUtc = joinedAt,
        ExcludedBefore = excludedBefore,
    };

    /// <summary>ロード画面で出さない設定のとき、ロード画面の始まりで閉じる。ロード画面の中かどうかは変えない。</summary>
    private void HideForLoadingScreen()
    {
        MenuPageOpen = false;
        AwaitingViewAngleClose = false;
    }

    /// <summary>新しいログセッション・プロセスへ移ったときに呼ぶ。</summary>
    public void ResetForNewSession()
    {
        _unmatchedJoins = 0;
        _closingMenuPage = null;
        MenuPageOpen = false;
        AwaitingViewAngleClose = false;
        InLoadingScreen = false;
        LastLeave = null;
        LastPeopleCount = null;
        LastExcludedAfter = null;
        LastExcludedVisit = null;
        LastCompanion = null;
        LastPhoto = null;
        _lastTargetEventId = null;
        _excludedSinceTarget = false;
        _selfUserId = null;
        _roster.Clear();
        _companions.Clear();
        _targetLeaves.Clear();
        Reset(PresenceState.Unknown);
    }

    /// <summary>
    /// 「一緒にいた人」として数える場面か。対象インスタンスに滞在していて、相手が自分自身でないとき
    /// （自分は OnPlayerJoined にも出るが、一覧には入れない）。
    /// </summary>
    private bool TryGetCompanionVisit(string userId, out string visit)
    {
        visit = string.Empty;

        if (State != PresenceState.InTarget || CurrentEventId is not { } current)
            return false;

        if (_selfUserId is not null && string.Equals(userId, _selfUserId, StringComparison.Ordinal))
            return false;

        visit = current;
        return true;
    }

    private void ReportCompanion(string visit, Companion companion)
    {
        _companions[companion.UserId] = companion;
        LastCompanion = new VisitCompanion(visit, companion);
    }

    /// <summary>写真の保存先の区切りをそろえる。ログでは "H:/…/VRC-Photo\2026-09\….png" のように / と \ が混ざる。</summary>
    public static string NormalizePhotoPath(string path) => path.Trim().Replace('/', '\\');

    private static string MakeEventId(string sourceSessionId, long byteOffset)
        => sourceSessionId + "@" + byteOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Reset(PresenceState state)
    {
        // クライアントが終わればメニューも閉じている。
        if (state == PresenceState.Ended)
        {
            MenuPageOpen = false;
            AwaitingViewAngleClose = false;
            InLoadingScreen = false;
        }

        State = state;
        CurrentLocation = null;
        CurrentEventId = null;
        _pending = null;
        _worldName = null;
    }

    private string? ConsumeWorldName(DateTime joinAtUtc)
    {
        var candidate = _worldName;
        _worldName = null;

        if (candidate is null)
            return null;

        var delta = joinAtUtc - candidate.AtUtc;
        if (delta < TimeSpan.Zero || delta > _nameWindow)
            return null;

        return candidate.Name;
    }

    private void Touch(in LogLine line)
    {
        if (TryGetUtc(line, out var at))
            LastEventAtUtc = at;
    }

    /// <summary>
    /// 行の時刻（退出・一緒にいた人の出入り・写真）。行の時刻が壊れていれば、このセッションで最後に読めた時刻を使う。
    /// それも無ければ現在時刻とし、保持期限を測れない状態にはしない。
    /// </summary>
    private DateTime TimeOf(in LogLine line)
        => TryGetUtc(line, out var at) ? at : LastEventAtUtc ?? _clock.UtcNow;

    private bool TryGetUtc(in LogLine line, out DateTime utc)
    {
        utc = default;

        if (!line.HasValidTimestamp)
            return false;

        if (!_time.TryToUtc(line.TimestampLocal, out utc))
            return false;

        // 未来のログ時刻は採用しない（仕様3.5節）。
        return utc <= _clock.UtcNow + FutureTolerance;
    }

    private sealed record PendingJoin(ParsedLocation Location, DateTime JoinedAtUtc, string? WorldName);

    private sealed record WorldNameCandidate(string Name, DateTime AtUtc);
}
