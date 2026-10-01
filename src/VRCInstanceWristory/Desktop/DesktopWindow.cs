using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;
using static VRCInstanceWristory.Desktop.NativeMethods;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウ（2026-09-26のユーザー指定→実装メモ5.39）。
///
/// ウィンドウは専用のスレッドでメッセージを回し、中身は <see cref="DesktopView"/> が描く。
/// 主ループとのやり取りは2方向だけにしてある。
///
/// <list type="bullet">
/// <item>主ループ → ウィンドウ: 行・残り時間・状態（<see cref="IPanelTarget"/> と <see cref="SetStatus"/>）。
/// 最新の値だけを預けてウィンドウのスレッドを起こし、向こうで取り込む。</item>
/// <item>ウィンドウ → 主ループ: 操作（<see cref="DesktopCommand"/>）。主ループが <see cref="TryTakeCommand"/> で取り出して反映する。</item>
/// </list>
///
/// 履歴エンジンと設定を触るのは今までどおり主ループだけで、ウィンドウのスレッドからは触らない。
/// タスクトレイのアイコンは <see cref="TrayIcon"/>、グループ名を打ち込む欄は <see cref="InlineTextEdit"/>、
/// ファイルを選ぶ画面は <see cref="FileDialogs"/> に分けてある。
/// </summary>
public sealed class DesktopWindow : IPanelTarget, IDisposable
{
    /// <summary>ウィンドウクラスの名前。2つ目の起動が1つ目のウィンドウを探すのにも使う（→実装メモ5.52）。</summary>
    public const string ClassName = "VRCInstanceWristory.DesktopWindow";

    /// <summary>主ループが値を預けたときに、ウィンドウのスレッドを起こす。</summary>
    private const uint WakeMessage = WM_APP + 1;

    /// <summary>−／＋ を押し続けたときの繰り返し（最初の間と、その後の間隔）。</summary>
    private const uint RepeatDelayMs = 400;
    private const uint RepeatIntervalMs = 70;
    private const nuint RepeatTimer = 1;

    /// <summary>状態の段の点の点滅（→実装メモ5.91）。点滅する点がある間だけ動かす。</summary>
    private const nuint BlinkTimer = 2;

    private const uint BlinkIntervalMs = 50;

    // 作るときに決まり、あとは変わらないもの。
    private readonly PanelStyle _style;
    private readonly DesktopSettings _initialSettings;
    private readonly bool _showWelcome;
    private readonly bool _startInTray;
    private readonly IDiagnostics _log;
    private readonly PhotoThumbnails? _thumbnails;
    private readonly SessionEndWatcher? _sessionEnd;

    // スレッドをまたぐもの。
    private readonly ConcurrentQueue<DesktopCommand> _commands = new();
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private string? _startError;
    private volatile bool _closed;

    // _gate で守るもの。ウィンドウ（主ループが預ける先）と、主ループから預かった最新の値。
    // ウィンドウのスレッドが取り込むまで上書きしてよい。
    private nint _hwnd;
    private IReadOnlyList<DisplayRow>? _pendingRows;
    private IReadOnlyList<RowDetail>? _pendingDetails;
    private string? _pendingCountdown;
    private bool? _pendingCountdownStopped;
    private DesktopStatus? _pendingStatus;
    private DesktopSettings? _pendingSettings;
    private (bool? WithSteamVr, bool AtLogon)? _pendingStartup;
    private readonly HashSet<string> _pendingThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Text, bool Error)> _pendingNotices = [];
    private bool _wakePosted;

    // 主ループだけが触る。前に預けた値の控え（毎フレーム呼ばれても、変わったときだけ起こすため）。
    private string _lastCountdown = string.Empty;
    private bool? _lastCountdownStopped;
    private DesktopStatus? _lastStatus;

    // ウィンドウのスレッドだけが触る。
    private DesktopView? _view;
    private WndProc? _wndProc;
    private Bitmap? _buffer;
    private PointF? _mousePoint;
    private bool _trackingLeave;
    private bool _blinkTimerOn;
    private nint _arrow;
    private nint _hand;
    private TrayIcon? _tray;
    private InlineTextEdit? _edit;

    // エクスプローラーが作り直されたときの合図（トレイのアイコンを置き直す）と、2つ目の起動からの合図（→実装メモ5.52）。
    private uint _taskbarCreated;
    private uint _activateMessage;

    // アイコン（ダッシュボードと同じ図→実装メモ5.41）。表示倍率が変わったら作り直す。
    private nint _iconBig;
    private nint _iconSmall;
    private nint _classIcon;
    private nint _classIconSmall;

    // 写真のサムネイル（→実装メモ5.55）。ウィンドウのスレッドで置き場所から読み、描くあいだ持っておく。
    private readonly Dictionary<string, Bitmap> _thumbnailImages = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="startInTray">タスクトレイに入れた状態で始める（ログオン時・SteamVRと一緒の起動→実装メモ5.50・5.51）。</param>
    /// <param name="thumbnails">写真のサムネイルの置き場所（→実装メモ5.55）。なければ「選んだ行」の写真は枠だけになる。</param>
    /// <param name="showWelcome">初回起動の案内を出す（まだ「わかった」を押したことがない→<see cref="DesktopView.ShowWelcome"/>）。</param>
    /// <param name="sessionEnd">
    /// Windowsの終了の通知の受け手（→実装メモ5.59）。このウィンドウにも同じ通知が来るので、
    /// 受けたら保存が済むまで返さない（先に返すと、その時点でプロセスごと終わらされることがある）。
    /// ウィンドウのスレッドが読むので、スレッドを始める前（ここ）で渡す。
    /// </param>
    public DesktopWindow(
        PanelStyle style,
        DesktopSettings settings,
        IDiagnostics log,
        bool startInTray = false,
        PhotoThumbnails? thumbnails = null,
        bool showWelcome = false,
        SessionEndWatcher? sessionEnd = null)
    {
        _style = style;
        _initialSettings = settings;
        _log = log;
        _startInTray = startInTray;
        _thumbnails = thumbnails;
        _showWelcome = showWelcome;
        _sessionEnd = sessionEnd;

        if (thumbnails is not null)
            thumbnails.Ready += OnThumbnailReady;
    }

    /// <summary>利用者がウィンドウを閉じた（アプリを終える合図）。</summary>
    public bool Closed => _closed;

    /// <summary>
    /// いま手前（フォーカスのある）ウィンドウがこのアプリのものか（このウィンドウ・その上の小さな画面）。どのスレッドから呼んでもよい。
    /// ファイアウォールの許可を求める画面が出ている間は、そちらが手前になる（→実装メモ5.115）。
    /// </summary>
    public static bool InFront
    {
        get
        {
            var foreground = GetForegroundWindow();
            return foreground != 0 && GetWindowThreadProcessId(foreground, out var processId) != 0 && processId == (uint)Environment.ProcessId;
        }
    }

    /// <summary>ウィンドウを開く。開けなければ false と理由を返す（アプリはウィンドウなしで続ける）。</summary>
    public bool Start(out string error)
    {
        _thread = new Thread(Run)
        {
            Name = "DesktopWindow",
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_started.Wait(TimeSpan.FromSeconds(10)))
            _startError ??= "ウィンドウの作成が時間内に終わりませんでした。";

        error = _startError ?? string.Empty;
        return _startError is null;
    }

    /// <summary>ウィンドウでの操作を1つ取り出す。</summary>
    public bool TryTakeCommand(out DesktopCommand command)
    {
        if (_commands.TryDequeue(out var taken))
        {
            command = taken;
            return true;
        }

        command = null!;
        return false;
    }

    // ------------------------------------------------------------------ 主ループから

    public void SetContent(IReadOnlyList<DisplayRow> rows)
    {
        lock (_gate)
            _pendingRows = rows;

        Wake();
    }

    /// <summary>「選んだ行」に出す詳しい情報は、このウィンドウだけが使う（→実装メモ5.42）。</summary>
    public bool WantsDetails => true;

    public void SetDetails(IReadOnlyList<RowDetail> details)
    {
        lock (_gate)
            _pendingDetails = details;

        Wake();
    }

    /// <summary>
    /// ウィンドウのスクロール位置はVR側とは別に持つ。VR側が末尾へ戻すとき（メニューを開き直したとき）に
    /// ウィンドウまで動かすと、マウスで見ている位置が勝手に変わるので、ここでは何もしない。
    /// 新しい行が増えたときは、末尾を見ていれば <see cref="Core.Scrolling.ScrollController"/> が追従する。
    /// </summary>
    public void ResetScrollToTail()
    {
    }

    public void SetCountdown(TimeSpan remaining, TimeSpan total)
    {
        // 毎フレーム呼ばれるが、変わるのは1秒に1回。変わったときだけ起こす。
        var text = Countdown.Format(remaining, total);

        if (text == _lastCountdown)
            return;

        _lastCountdown = text;

        lock (_gate)
            _pendingCountdown = text;

        Wake();
    }

    /// <summary>カウントダウンが止まっているか（→実装メモ5.73）。変わったときだけ起こす。</summary>
    public void SetCountdownStopped(bool stopped)
    {
        if (stopped == _lastCountdownStopped)
            return;

        _lastCountdownStopped = stopped;

        lock (_gate)
            _pendingCountdownStopped = stopped;

        Wake();
    }

    public void SetStatus(DesktopStatus status)
    {
        if (status == _lastStatus)
            return;

        _lastStatus = status;

        lock (_gate)
            _pendingStatus = status;

        Wake();
    }

    /// <summary>SteamVRのダッシュボードで変わった設定を、ウィンドウの表示へ反映する（→実装メモ5.40）。</summary>
    public void SetSettings(DesktopSettings settings)
    {
        lock (_gate)
            _pendingSettings = settings;

        Wake();
    }

    /// <summary>SteamVR・Windows の登録の状態だけを、ウィンドウの表示へ反映する（→実装メモ5.50・5.51）。</summary>
    public void SetStartupState(bool? launchWithSteamVr, bool launchAtLogon)
    {
        lock (_gate)
            _pendingStartup = (launchWithSteamVr, launchAtLogon);

        Wake();
    }

    /// <summary>
    /// 利用者へ知らせる（グループ名の読み込み・書き出しの結果→実装メモ5.65）。ウィンドウのスレッドで小さな画面を出す。
    /// 知らせるのは、利用者がウィンドウで押した操作の結果だけにする（VRの最中に勝手に画面を出さない）。
    /// </summary>
    public void ShowNotice(string text, bool error = false)
    {
        lock (_gate)
            _pendingNotices.Add((text, error));

        Wake();
    }

    /// <summary>サムネイルができた（作るスレッドから）。ウィンドウのスレッドで読み直して描き直す。</summary>
    private void OnThumbnailReady(string photoPath)
    {
        lock (_gate)
            _pendingThumbnails.Add(photoPath);

        Wake();
    }

    /// <summary>ウィンドウのスレッドを起こす。起こしてまだ取り込まれていなければ、重ねて起こさない。</summary>
    private void Wake()
    {
        nint hwnd;

        lock (_gate)
        {
            if (_wakePosted || _hwnd == 0)
                return;

            _wakePosted = true;
            hwnd = _hwnd;
        }

        // 送れなかったら（キューがあふれた・ウィンドウが壊れた途中）、次に預けたときに起こし直せるよう印を戻す。
        // 戻さないと、印が立ったままになって、そのあと値が届いても二度と起こさない。
        if (!PostMessageW(hwnd, WakeMessage, 0, 0))
        {
            lock (_gate)
                _wakePosted = false;
        }
    }

    // ------------------------------------------------------------------ ウィンドウのスレッド

    private void Run()
    {
        try
        {
            // このスレッドのウィンドウだけをモニターごとのDPIに合わせる（他のスレッドには影響しない）。
            SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);

            _view = new DesktopView(_style, _initialSettings, OnViewCommand) { Thumbnails = Thumbnail };
            _tray = new TrayIcon(_log);
            _edit = new InlineTextEdit(_style, _log);

            if (_showWelcome)
                _view.ShowWelcome();

            if (!CreateWindow())
                return;

            _started.Set();

            while (true)
            {
                var result = GetMessageW(out var msg, 0, 0, 0);

                if (result == 0 || result == -1)
                    break;

                TranslateMessage(msg);
                DispatchMessageW(msg);
            }
        }
        catch (Exception ex)
        {
            _startError ??= $"ウィンドウでエラーが起きました: {ex.Message}";
            _log.Error($"デスクトップのウィンドウでエラー: {ex}");
        }
        finally
        {
            _closed = true;
            CleanUpWindowThread();
            _started.Set();
        }
    }

    /// <summary>
    /// ウィンドウのスレッドの後片付け。例外で抜けたときはウィンドウがまだ残っているので、先に壊してからクラスの登録を外す
    /// （ウィンドウが残っているとクラスの登録は外せない）。
    /// </summary>
    private void CleanUpWindowThread()
    {
        nint hwnd;

        lock (_gate)
        {
            hwnd = _hwnd;
            _hwnd = 0;
        }

        if (hwnd != 0 && IsWindow(hwnd))
            DestroyWindow(hwnd);

        _edit?.Dispose();
        _buffer?.Dispose();
        _view?.Dispose();
        UnregisterClassW(ClassName, GetModuleHandleW(null));

        foreach (var image in _thumbnailImages.Values)
            image.Dispose();

        _thumbnailImages.Clear();

        foreach (var icon in new[] { _iconBig, _iconSmall, _classIcon, _classIconSmall })
        {
            if (icon != 0)
                DestroyIcon(icon);
        }
    }

    private bool CreateWindow()
    {
        var instance = GetModuleHandleW(null);
        _wndProc = WindowProc;
        _arrow = LoadCursorW(0, IDC_ARROW);
        _hand = LoadCursorW(0, IDC_HAND);

        // エクスプローラーが落ちて作り直されたとき、タスクトレイのアイコンを置き直すための合図。
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        // 2つ目の起動からの「前に出して」の合図（→実装メモ5.52）。
        _activateMessage = RegisterWindowMessageW(SingleInstance.ActivateMessageName);

        // クラスのアイコンは等倍の大きさ。ウィンドウができたら、置かれたモニターの倍率で作り直す（ApplyIcons）。
        _classIcon = AppIcon.CreateHandle(32);
        _classIconSmall = AppIcon.CreateHandle(16);

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            // CS_DBLCLKS: 写真のサムネイルはダブルクリックで開く（→実装メモ5.65）。
            style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            hIcon = _classIcon,
            hCursor = _arrow,
            lpszClassName = ClassName,
            hIconSm = _classIconSmall,
        };

        if (RegisterClassExW(ref wc) == 0)
        {
            _startError = $"ウィンドウクラスを登録できません（{Marshal.GetLastWin32Error()}）。";
            return false;
        }

        // WS_CLIPCHILDREN: 描き直すときに子のウィンドウ（グループ名の欄）の上を塗らない。
        // これがないと、残り時間が変わるたび（1秒に1回・ログが来るとそれ以上）に欄の上を下の絵で塗ってから欄が描き直すので、
        // 欄の文字と点滅するカーソルが崩れて見えた（→実装メモ5.56）。
        var hwnd = CreateWindowExW(
            0,
            ClassName,
            AppInfo.NameWithVersion,
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            DesktopView.DefaultClientSize.Width,
            DesktopView.DefaultClientSize.Height,
            0,
            0,
            instance,
            0);

        if (hwnd == 0)
        {
            _startError = $"ウィンドウを作れません（{Marshal.GetLastWin32Error()}）。";
            return false;
        }

        lock (_gate)
            _hwnd = hwnd;

        // タイトルバーも暗くする（効かない環境では何もしない）。
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // 置かれたモニターのDPIで、既定の大きさに直す。作業領域より大きければ収める。
        var dpi = GetDpiForWindow(hwnd);
        ApplyIcons(hwnd, dpi);
        var size = WindowSizeFor(DesktopView.DefaultClientSize, dpi);

        if (SystemParametersInfoW(SPI_GETWORKAREA, 0, out var work, 0))
            size = new Size(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));

        SetWindowPos(hwnd, 0, 0, 0, size.Width, size.Height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        ApplyTopMost(hwnd, _initialSettings.TopMost);

        // ログオン時・SteamVRと一緒の起動では、ウィンドウを出さずにタスクトレイへ入れて始める（→実装メモ5.50・5.51）。
        if (_startInTray)
        {
            Resize(hwnd, _view!);

            // 起動した側が表示のしかたを渡していると、最初の ShowWindow の指定は無視されてその値になる（Win32の決まり）。
            // 念のため1回空で呼んでおき、トレイへ入れるときの SW_HIDE が必ず効くようにする。
            ShowWindow(hwnd, SW_HIDE);
            MinimizeToTray(hwnd);
        }
        else
        {
            ShowWindow(hwnd, SW_SHOWNORMAL);
        }

        // 起動前に届いていた値を取り込む。
        PostMessageW(hwnd, WakeMessage, 0, 0);
        return true;
    }

    /// <summary>
    /// タスクバー・Alt+Tab・タイトルバーのアイコンを、その倍率の大きさで描いて付ける（→実装メモ5.41）。
    /// 縮めた絵よりその大きさで描いた絵のほうが、細い線がつぶれない。
    /// </summary>
    private void ApplyIcons(nint hwnd, uint dpi)
    {
        var big = AppIcon.CreateHandle(GetSystemMetricsForDpi(SM_CXICON, dpi));
        var small = AppIcon.CreateHandle(GetSystemMetricsForDpi(SM_CXSMICON, dpi));

        SendMessageW(hwnd, WM_SETICON, ICON_BIG, big);
        SendMessageW(hwnd, WM_SETICON, ICON_SMALL, small);

        if (_iconBig != 0)
            DestroyIcon(_iconBig);

        if (_iconSmall != 0)
            DestroyIcon(_iconSmall);

        _iconBig = big;
        _iconSmall = small;

        // タスクトレイに入っているなら、そちらも新しい大きさで置き直す。
        _tray!.Readd(hwnd, _iconSmall, explorerRestarted: false);
    }

    // ------------------------------------------------------------------ タスクトレイ（→実装メモ5.41）

    /// <summary>最小化したら、タスクバーから消してタスクトレイへ入れる。</summary>
    private void MinimizeToTray(nint hwnd)
    {
        if (_tray!.Visible)
            return;

        ShowWindow(hwnd, SW_HIDE);
        _tray.Add(hwnd, _iconSmall);
    }

    /// <summary>タスクトレイから戻す。アイコンはトレイから外す。</summary>
    private void RestoreFromTray(nint hwnd)
    {
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        _tray!.Remove(hwnd);
    }

    /// <summary>
    /// 2つ目の起動から「前に出して」と言われた（→実装メモ5.52）。タスクトレイに入っていれば戻し、
    /// 最小化されていれば元の大きさにして、手前に出す。
    /// </summary>
    private void BringToFront(nint hwnd)
    {
        if (_tray!.Visible || !IsWindowVisible(hwnd))
        {
            RestoreFromTray(hwnd);
            return;
        }

        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        SetForegroundWindow(hwnd);
    }

    private static Size WindowSizeFor(Size logicalClient, uint dpi)
    {
        var scale = dpi / 96f;
        var rect = new RECT
        {
            Right = (int)MathF.Ceiling(logicalClient.Width * scale),
            Bottom = (int)MathF.Ceiling(logicalClient.Height * scale),
        };

        AdjustWindowRectExForDpi(ref rect, WS_OVERLAPPEDWINDOW, false, 0, dpi);
        return new Size(rect.Width, rect.Height);
    }

    /// <summary>ウィンドウでの操作。常に手前の切り替えはここ（ウィンドウのスレッド）で反映し、保存のために主ループへも渡す。</summary>
    private void OnViewCommand(DesktopCommand command)
    {
        if (command is DesktopCommand.ChangeSettings change && change.Fields.HasFlag(SettingsField.DesktopWindow))
            ApplyTopMost(OwnWindow(), change.Settings.TopMost);

        _commands.Enqueue(command);
    }

    private static void ApplyTopMost(nint hwnd, bool topMost)
        => SetWindowPos(hwnd, topMost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private nint OwnWindow()
    {
        lock (_gate)
            return _hwnd;
    }

    // ------------------------------------------------------------------ メッセージ

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        // ここで例外を外へ出すとネイティブ側で落ちる。記録して既定の処理へ回す。
        try
        {
            if (HandleMessage(hwnd, message, wParam, lParam, out var result))
                return result;
        }
        catch (Exception ex)
        {
            _log.Error($"デスクトップのウィンドウでエラー: {ex}");
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private bool HandleMessage(nint hwnd, uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        var view = _view!;

        // エクスプローラーが作り直された。トレイに入っていたらアイコンを置き直す。
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            _tray!.Readd(hwnd, _iconSmall, explorerRestarted: true);
            return true;
        }

        // 2つ目の起動があった（→実装メモ5.52）。SteamVR が開いたものなら前には出さず、主ループへ知らせるだけ。
        if (message == _activateMessage && _activateMessage != 0)
        {
            var fromSteamVr = wParam == SingleInstance.FromSteamVrRequest;

            if (!fromSteamVr)
                BringToFront(hwnd);

            _commands.Enqueue(new DesktopCommand.AnotherLaunch(fromSteamVr));
            return true;
        }

        switch (message)
        {
            case WakeMessage:
                TakePending(view);
                break;

            case InlineTextEdit.DoneMessage:
                // グループ名の欄から Enter・Esc・欄を離れた、のいずれか（→実装メモ5.48）。
                if (wParam == 1)
                    CommitEdit();
                else
                    _edit!.Cancel();

                break;

            case WM_SIZE:
                // 大きさが変わったら、打ち込み中のグループ名は確定する（欄の位置が合わなくなるため）。
                CommitEdit();

                // 最小化はタスクトレイへ入れる（初期設定を終えるまではタスクバーに残す→実装メモ5.113）。
                // 中身の大きさは最小化の前のまま保つ（0×0で並べ直さない）。
                if (wParam == SIZE_MINIMIZED)
                {
                    if (view.MinimizesToTray)
                        MinimizeToTray(hwnd);

                    return true;
                }

                Resize(hwnd, view);
                break;

            case TrayIcon.CallbackMessage:
                switch (_tray!.HandleMessage(hwnd, lParam))
                {
                    case TrayIcon.Request.Open:
                        RestoreFromTray(hwnd);
                        break;

                    case TrayIcon.Request.Exit:
                        PostMessageW(hwnd, WM_CLOSE, 0, 0);
                        break;
                }

                return true;

            case WM_DPICHANGED:
            {
                // 移った先のモニターに合わせて、Windowsが勧める大きさへ直す。
                var suggested = Marshal.PtrToStructure<RECT>(lParam);
                SetWindowPos(hwnd, 0, suggested.Left, suggested.Top, suggested.Width, suggested.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                ApplyIcons(hwnd, (uint)HighWord(wParam));
                Resize(hwnd, view);
                return true;
            }

            case WM_GETMINMAXINFO:
            {
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var min = WindowSizeFor(DesktopView.MinClientSize, hwnd == 0 ? 96u : GetDpiForWindow(hwnd));
                info.ptMinTrackSize = new POINT { X = min.Width, Y = min.Height };
                Marshal.StructureToPtr(info, lParam, false);
                return true;
            }

            case WM_ERASEBKGND:
                // 背景は描画で全面を塗るので、消さない（ちらつき防止）。
                result = 1;
                return true;

            case WM_PAINT:
                Paint(hwnd, view);
                return true;

            case WM_SETCURSOR:
                // グループ名の欄（子のウィンドウ）の上では、欄の I の形のカーソルに任せる。
                if (LowWord(lParam) != HTCLIENT || wParam != hwnd)
                    return false;

                SetCursor(_hand != 0 && _mousePoint is { } p && view.IsClickable(p) ? _hand : _arrow);
                result = 1;
                return true;

            case WM_MOUSEMOVE:
                _mousePoint = ClientPoint(lParam);
                TrackLeave(hwnd);
                view.MouseMove(_mousePoint.Value);
                break;

            case WM_MOUSELEAVE:
                _trackingLeave = false;
                _mousePoint = null;
                view.MouseLeave();
                break;

            // CS_DBLCLKS のクラスでは、素早い2回目の押下は WM_LBUTTONDOWN ではなく WM_LBUTTONDBLCLK で届く。
            // 2回目も1回の押下として扱い（−／＋ を素早く2回押したときに1回ぶん取りこぼさない）、そのうえでダブルクリックを知らせる（→実装メモ5.65）。
            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                // 欄の外を押したら、打ち込み中のグループ名は確定する。
                CommitEdit();

                _mousePoint = ClientPoint(lParam);
                SetCapture(hwnd);
                view.MouseDown(_mousePoint.Value);

                if (message == WM_LBUTTONDBLCLK)
                    view.MouseDoubleClick(_mousePoint.Value);

                if (view.Repeating)
                    SetTimer(hwnd, RepeatTimer, RepeatDelayMs, 0);

                HandleViewRequests(hwnd, view);
                break;

            case WM_RBUTTONDOWN:
                // 右クリックは、VR内でトリガーを引いたときと同じ目印のポップアップ（→実装メモ5.42）。
                CommitEdit();
                _mousePoint = ClientPoint(lParam);
                view.RightMouseDown(_mousePoint.Value);
                break;

            case WM_CTLCOLOREDIT when _edit!.TryColor(wParam, lParam, out var brush):
                // 打ち込む欄もパネルと同じ暗い配色にする。
                result = brush;
                return true;

            case WM_LBUTTONUP:
                KillTimer(hwnd, RepeatTimer);
                ReleaseCapture();
                view.MouseUp();
                break;

            case WM_CAPTURECHANGED:
                KillTimer(hwnd, RepeatTimer);
                view.MouseUp();
                break;

            case WM_TIMER when wParam == (nint)RepeatTimer:
                view.RepeatPress();
                SetTimer(hwnd, RepeatTimer, RepeatIntervalMs, 0);
                break;

            case WM_TIMER when wParam == (nint)BlinkTimer:
                RenderBlinkingDots(hwnd, view);
                break;

            case WM_MOUSEWHEEL:
            {
                // ホイールの位置は画面座標で来る。
                var point = new POINT { X = LowWord(lParam), Y = HighWord(lParam) };
                ScreenToClient(hwnd, ref point);
                view.MouseWheel(new PointF(point.X, point.Y), HighWord(wParam));
                break;
            }

            case WM_CLOSE:
                DestroyWindow(hwnd);
                return true;

            case WM_QUERYENDSESSION:
                result = 1;
                return true;

            case WM_ENDSESSION:
                _sessionEnd?.OnEndSession(wParam != 0);
                return true;

            case WM_DESTROY:
                _edit!.Cancel();
                _tray!.Remove(hwnd);
                PostQuitMessage(0);
                return true;

            default:
                return false;
        }

        if (view.Dirty)
            InvalidateRect(hwnd, 0, false);

        UpdateBlinkTimer(hwnd, view);
        return true;
    }

    /// <summary>
    /// 点だけを裏の面へ描き直し、その範囲だけを写す（ウィンドウ全体は描き直さない→実装メモ5.91）。
    /// ほかの変更で全体を描き直す必要があれば、そちらに任せる。
    /// </summary>
    private void RenderBlinkingDots(nint hwnd, DesktopView view)
    {
        if (_buffer is null || view.Dirty)
            return;

        using var g = Graphics.FromImage(_buffer);

        if (view.RenderBlinkingDots(g) is { } area)
        {
            var rect = new RECT { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
            InvalidateArea(hwnd, rect, false);
        }
    }

    /// <summary>点滅する点があるときだけタイマーを動かす。止めるときは、点を点いた状態へ戻すため全体を描き直す。</summary>
    private void UpdateBlinkTimer(nint hwnd, DesktopView view)
    {
        var blinking = view.StatusBlinking;

        if (blinking == _blinkTimerOn)
            return;

        _blinkTimerOn = blinking;

        if (blinking)
        {
            SetTimer(hwnd, BlinkTimer, BlinkIntervalMs, 0);
        }
        else
        {
            KillTimer(hwnd, BlinkTimer);
            InvalidateRect(hwnd, 0, false);
        }
    }

    private static PointF ClientPoint(nint lParam) => new(LowWord(lParam), HighWord(lParam));

    private void TrackLeave(nint hwnd)
    {
        if (_trackingLeave)
            return;

        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE,
            hwndTrack = hwnd,
        };

        _trackingLeave = TrackMouseEvent(ref track);
    }

    /// <summary>
    /// 主ループから預かった値を取り込む。1つの値で例外が起きても残りは取り込む（1つの失敗で状態や残り時間が古いまま残らないように）。
    /// </summary>
    private void TakePending(DesktopView view)
    {
        IReadOnlyList<DisplayRow>? rows;
        IReadOnlyList<RowDetail>? details;
        string? countdown;
        bool? countdownStopped;
        DesktopStatus? status;
        DesktopSettings? settings;
        (bool? WithSteamVr, bool AtLogon)? startup;
        List<string> thumbnails;
        List<(string Text, bool Error)> notices;

        lock (_gate)
        {
            thumbnails = [.. _pendingThumbnails];
            notices = [.. _pendingNotices];
            rows = _pendingRows;
            details = _pendingDetails;
            countdown = _pendingCountdown;
            countdownStopped = _pendingCountdownStopped;
            status = _pendingStatus;
            settings = _pendingSettings;
            startup = _pendingStartup;

            _pendingThumbnails.Clear();
            _pendingNotices.Clear();
            _pendingRows = null;
            _pendingDetails = null;
            _pendingCountdown = null;
            _pendingCountdownStopped = null;
            _pendingStatus = null;
            _pendingSettings = null;
            _pendingStartup = null;
            _wakePosted = false;
        }

        if (settings is not null)
            Apply("設定", () => view.SetSettings(settings));

        if (startup is { } state)
            Apply("起動の登録", () => view.SetStartupState(state.WithSteamVr, state.AtLogon));

        if (rows is not null)
            Apply("行", () => view.SetRows(rows));

        if (details is not null)
        {
            Apply("詳しい情報", () =>
            {
                view.SetDetails(details);
                ForgetThumbnailsExcept(details);
            });
        }

        // できたサムネイルは読み直す（まだなかったときは読んでいないので、捨てるものがなくても描き直す）。
        if (thumbnails.Count > 0)
        {
            Apply("サムネイル", () =>
            {
                foreach (var path in thumbnails)
                {
                    if (_thumbnailImages.Remove(path, out var old))
                        old.Dispose();
                }

                view.ThumbnailsChanged();
            });
        }

        if (countdown is not null)
            Apply("残り時間", () => view.SetCountdown(countdown));

        if (countdownStopped is { } stoppedNow)
            Apply("カウントダウンの停止", () => view.SetCountdownStopped(stoppedNow));

        if (status is not null)
            Apply("状態", () => view.SetStatus(status));

        // 知らせは描き直しを済ませてから出す（小さな画面を閉じるまで、ここで待つ）。
        foreach (var (text, error) in notices)
            MessageBoxW(OwnWindow(), text, AppInfo.DisplayName, MB_OK | (error ? MB_ICONERROR : MB_ICONINFORMATION));
    }

    /// <summary>預かった値を1つ取り込む。失敗は記録して、残りの取り込みを続ける。</summary>
    private void Apply(string what, Action apply)
    {
        try
        {
            apply();
        }
        catch (Exception ex)
        {
            _log.Error($"デスクトップのウィンドウへ{what}を取り込めませんでした: {ex}");
        }
    }

    private static void Resize(nint hwnd, DesktopView view)
    {
        GetClientRect(hwnd, out var client);
        view.Resize(new Size(Math.Max(1, client.Width), Math.Max(1, client.Height)), GetDpiForWindow(hwnd) / 96f);
    }

    private void Paint(nint hwnd, DesktopView view)
    {
        var hdc = BeginPaint(hwnd, out var paint);

        try
        {
            var size = view.ClientSize;

            if (size.Width <= 0 || size.Height <= 0)
                return;

            // 中身は裏の面へ描いてから1回で写す（途中の絵を見せない）。
            if (_buffer is null || _buffer.Width != size.Width || _buffer.Height != size.Height)
            {
                _buffer?.Dispose();
                _buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(_buffer);
                view.Render(g);
            }
            else if (view.Dirty)
            {
                using var g = Graphics.FromImage(_buffer);
                view.Render(g);
            }

            using var target = Graphics.FromHdc(hdc);
            target.CompositingMode = CompositingMode.SourceCopy;
            target.InterpolationMode = InterpolationMode.NearestNeighbor;
            target.DrawImage(_buffer, new Rectangle(0, 0, size.Width, size.Height), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel);
        }
        finally
        {
            EndPaint(hwnd, paint);
        }
    }

    // ------------------------------------------------------------------ 写真（→実装メモ5.55）

    /// <summary>写真のサムネイル。読めたものは描くあいだ持っておき、まだできていなければ null（次に描くときに試し直す）。</summary>
    private Image? Thumbnail(string photoPath)
    {
        if (_thumbnailImages.TryGetValue(photoPath, out var cached))
            return cached;

        if (_thumbnails?.TryLoad(photoPath) is not { } image)
            return null;

        _thumbnailImages[photoPath] = image;
        return image;
    }

    /// <summary>もう履歴にない写真のサムネイルを手放す。</summary>
    private void ForgetThumbnailsExcept(IReadOnlyList<RowDetail> details)
    {
        var keep = new HashSet<string>(details.SelectMany(d => d.Photos).Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var path in _thumbnailImages.Keys.Where(p => !keep.Contains(p)).ToList())
        {
            if (_thumbnailImages.Remove(path, out var image))
                image.Dispose();
        }
    }

    // ------------------------------------------------------------------ ウィンドウ（Win32）でしか出せないもの

    /// <summary>押した部品が、ウィンドウ（Win32）でしか出せないものを頼んできたら出す。</summary>
    private void HandleViewRequests(nint hwnd, DesktopView view)
    {
        // 「名前を付ける」を押したなら、そこへ打ち込む欄を置く（→実装メモ5.48）。
        if (view.TakeTextEditRequest() is { } request)
        {
            ReleaseCapture();
            _edit!.Open(hwnd, request);
        }

        // 写真を開くアプリの「選んだアプリ」を押したなら、実行ファイルを選ぶ画面を出す（→実装メモ5.55）。
        if (view.TakeAppChoiceRequest())
        {
            ReleaseCapture();

            if (FileDialogs.ChooseExecutable(hwnd) is { } executable)
                view.ChoosePhotoViewer(executable);
        }

        // グループ名の読み込み・書き出し（→実装メモ5.65）。
        switch (view.TakeGroupFileRequest())
        {
            case GroupFileRequest.Import:
                ReleaseCapture();
                ImportGroupNames(hwnd);
                break;

            case GroupFileRequest.Export:
                ReleaseCapture();

                if (FileDialogs.ChooseJsonFile(hwnd, save: true) is { } exportPath)
                    _commands.Enqueue(new DesktopCommand.ExportGroupNames(exportPath));

                break;
        }

        // 更新して再起動（→実装メモ5.121）。アプリが一度終わる（VR の最中なら手首のパネルも消える）ので、確かめてから送る。
        if (view.TakeUpdateRequest() && view.UpdateStatus is { Phase: UpdatePhase.Available, Version: { } version })
        {
            ReleaseCapture();

            if (UpdateConfirm.Ask(hwnd, version))
                _commands.Enqueue(new DesktopCommand.ApplyUpdate());
        }
    }

    /// <summary>
    /// グループ名のファイルを選んで読む（→実装メモ5.65）。読んで検めるのはここ（ウィンドウのスレッド）で、
    /// 設定へ書くのは主ループ。読めなければ、ここで知らせる。
    /// </summary>
    private void ImportGroupNames(nint hwnd)
    {
        if (FileDialogs.ChooseJsonFile(hwnd, save: false) is not { } path)
            return;

        if (GroupNameFile.Read(path, out var error) is not { } result)
        {
            MessageBoxW(hwnd, error, "グループ名のインポート", MB_OK | MB_ICONERROR);
            return;
        }

        if (result.Names.Count == 0)
        {
            MessageBoxW(hwnd, $"使えるグループ名がありませんでした（{result.Skipped}件は Group ID か名前が使えないため読み飛ばしました）。", "グループ名のインポート", MB_OK | MB_ICONERROR);
            return;
        }

        _commands.Enqueue(new DesktopCommand.ImportGroupNames(result.Names));

        if (result.Skipped > 0)
            _log.Warn($"グループ名のインポート: {result.Skipped}件は Group ID か名前が使えないため読み飛ばしました。");
    }

    /// <summary>打ち込んだグループ名を確定して欄を閉じる（→実装メモ5.48）。欄を開いていなければ何もしない。</summary>
    private void CommitEdit()
    {
        if (_edit!.Commit() is { } done)
            _view?.CommitGroupName(done.GroupId, done.Text);
    }

    /// <summary>
    /// ウィンドウを閉じてスレッドの終わりを待つ。待ちきれなかったとき（スレッドがまだ片付けている）は、
    /// スレッドが後で使う合図は手放さない（手放すと、スレッドが合図を立てたときに例外になる）。
    /// </summary>
    public void Dispose()
    {
        if (_thumbnails is not null)
            _thumbnails.Ready -= OnThumbnailReady;

        var hwnd = OwnWindow();

        if (hwnd != 0)
            PostMessageW(hwnd, WM_CLOSE, 0, 0);

        if (_thread is null || _thread.Join(TimeSpan.FromSeconds(3)))
            _started.Dispose();
        else
            _log.Warn("デスクトップのウィンドウが時間内に閉じませんでした。");
    }
}
