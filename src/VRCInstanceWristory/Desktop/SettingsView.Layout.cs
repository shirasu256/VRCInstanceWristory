using System.Drawing;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Desktop;

/// <summary>設定の部品の並べ方（<see cref="Layout"/>）。まとまりごとに1つのメソッドで並べる。</summary>
public sealed partial class SettingsView
{
    // 寸法は論理px（96dpi）。実際の画素は Scale 倍する。

    /// <summary>1列の幅。</summary>
    public const float ColumnWidth = 344f;

    /// <summary>列と列の間。</summary>
    public const float ColumnGap = 16f;

    private const float SectionGap = UiMetrics.CardGap;
    private const float SectionTitleHeight = UiMetrics.CardTitleHeight;
    private const float SectionPadding = UiMetrics.CardPadding;
    private const float StepperRowHeight = 30f;
    private const float StepButtonSize = 24f;
    private const float StepValueWidth = 100f;

    // 目立たせる数値の部品（リセットまでの時間→実装メモ5.58）。字もボタンもひと回り大きくし、値はアクセント色にする。
    private const float ProminentRowHeight = 42f;
    // −／＋ は 2026-09-27 のユーザー指定で 0.9倍（30→27px）、さらに 0.8倍（27→21.6px）にした（→実装メモ5.68・5.69）。
    // 値の幅も詰めて、−／＋ を値へ寄せる（104→84px）。
    private const float ProminentButtonSize = 21.6f;
    private const float ProminentValueWidth = 84f;

    /// <summary>
    /// <see cref="EmphasizeToFit"/> で倍率を詰めるときの刻み。字の幅は倍率にほぼ比例するが、字形の合わせ込みで
    /// わずかに外れることがあるので、比例で求めた倍率から測り直して、収まるまでこの刻みで下げる。
    /// </summary>
    private const float EmphasisStep = 0.01f;
    private const float CheckRowHeight = 26f;
    private const float CheckBoxSize = 15f;
    private const float NoteLineHeight = UiMetrics.NoteLineHeight;

    /// <summary>まとまりの最上部の説明と、その下の部品の間の空き。</summary>
    private const float IntroGap = 6f;

    /// <summary>見出しの帯と、まとまりの最初の部品の間の空き（→実装メモ5.67）。</summary>
    private const float BodyTop = 6f;

    /// <summary>部品と注記の間の空き（→実装メモ5.67）。</summary>
    private const float NotesGap = 4f;
    private const float ButtonHeight = 28f;

    /// <summary>ボタンと、その上の部品の間の空き。</summary>
    private const float ButtonGap = 6f;

    /// <summary>
    /// 項目名の段と、その下に並べるボタンの間の空き（「インスタンス操作の挙動」「グループ名リストの json」→実装メモ5.74）。
    /// 項目名の段の高さだけでは、字の下とボタンの枠が近すぎた。
    /// </summary>
    private const float LabelToButtonGap = 4f;

    /// <summary>選択肢の行で、ラベルを上・選択肢を下の2段に分けたときのラベルの段の高さ。</summary>
    private const float ChoiceLabelHeight = 22f;

    private const float ChoiceSegmentHeight = 24f;
    private const float ChoiceSegmentGap = 4f;

    /// <summary>外部連携のコマンドの枠の高さと、その中の「コピー」のボタン（→実装メモ5.83）。</summary>
    private const float CodeBlockHeight = 32f;

    private const float CopyButtonHeight = 22f;
    private const float CopyButtonWidth = 84f;

    /// <summary>コマンドの枠の中の左右の余白。</summary>
    private const float CodePadding = 8f;

    /// <summary>外部連携のまとまりの中の部品の間。</summary>
    private const float ExternalGap = 4f;

    /// <summary>
    /// まとまりを並べる順（1列でも2列でもこの順に積む）。2026-09-27に見直した（→実装メモ5.72）。
    /// 「一般設定」は、訪問履歴そのもの（履歴・表示する種類）→ 行を選んだあとに使うもの（インスタンス操作・グループ名・写真）→
    /// アプリ全体のふるまい（起動・デスクトップウィンドウ）の順にする。よく触るものほど上に置く。
    /// VR の2つは一般設定には出ないが、ダッシュボード（2列）の右の列で「インスタンス操作」より上に来るよう、その前に置く。
    /// </summary>
    private static readonly SettingsSections[] Order =
    [
        SettingsSections.History,
        SettingsSections.TargetTypes,
        SettingsSections.Overlay,
        SettingsSections.WristPanel,
        SettingsSections.ResetWarning,
        SettingsSections.Rows,
        SettingsSections.Photos,
        SettingsSections.Startup,
        SettingsSections.Window,
        SettingsSections.External,
    ];

    /// <summary>初期設定の画面での並び（<see cref="SettingsSections.Setup"/>）。</summary>
    private static readonly SettingsSections[] SetupOrder =
    [
        SettingsSections.SetupRetention,
        SettingsSections.TargetTypes,

        // 右の列は、手首パネル・AFK 検知・起動の順（2026-09-30のユーザー指定→実装メモ5.100）。
        SettingsSections.SetupWrist,
        SettingsSections.Afk,
        SettingsSections.Startup,
    ];

    /// <summary>
    /// 2列に並べるとき右の列に置くまとまり。ダッシュボードでは VR の3つ（VR オーバーレイ・手首パネル・予告通知）を右、それ以外を左に置く。
    /// 「インスタンス操作」は予告通知を足して右が長くなったので左へ移した（→実装メモ5.90）。
    /// 初期設定の画面（→実装メモ5.98）では、右に「起動」「パネル位置」「AFK」を置く（「起動」はダッシュボードには出さないので、2列はこの画面だけ）。
    /// </summary>
    private const SettingsSections RightColumn = SettingsSections.Overlay | SettingsSections.WristPanel | SettingsSections.ResetWarning
        | SettingsSections.Photos | SettingsSections.Window | SettingsSections.Startup | SettingsSections.SetupWrist | SettingsSections.Afk;

    /// <summary>まとまり1つ。注記は幅で折り返してある。</summary>
    private sealed record Section(RectangleF Rect, string Title, IReadOnlyList<string> Notes, float NotesTop);

    // 配置（Layout で決める）。
    private readonly List<HitTarget> _targets = [];
    private readonly List<Section> _sections = [];
    private readonly Dictionary<SettingsStepper, RectangleF> _stepperRows = [];
    private readonly List<(RectangleF Rect, string Text)> _labels = [];

    // まとまりの最上部に置く説明（→実装メモ5.66）。注記と同じ字で、部品より上に出す。
    private readonly List<(PointF Origin, string Text)> _intros = [];

    // 手首パネルの枠（VRオーバーレイ機能をオフにしている間に薄くする→実装メモ5.71）。並べていなければ空。
    private RectangleF _wristSection;

    // 予告通知の枠と、「リセット予告アイコンを表示する」より下の部品の範囲（オフの間に薄くする→実装メモ5.89）。並べていなければ空。
    private RectangleF _warningSection;
    private RectangleF _warningBody;

    // 外部連携のコマンドの枠と「最終実行」の段（→実装メモ5.83）。並べていなければ空。
    private RectangleF _codeBlock;
    private RectangleF _lastRunRow;

    // 前に並べたときの引数（注記の行数が変わったときに同じ所へ並べ直す）。
    private (PointF Origin, float Scale, int Columns, SettingsSections Sections, float? ColumnWidth)? _lastLayout;

    // 並べている途中だけ使う、左上・1列の幅・列の数・列ごとの次に積む高さ。
    private PointF _origin;
    private float _sectionWidth;
    private float[] _columnTops = [];

    /// <summary>
    /// <paramref name="origin"/> から <paramref name="sections"/> のまとまりを並べる。戻り値は使った大きさ（画素）。
    /// <paramref name="columns"/> が2なら、左に「履歴」「記録する種類」「インスタンス操作」「グループ名」、右に VR の設定（と「写真」「このウィンドウ」）を置く。
    /// </summary>
    /// <param name="columnWidth">1列の幅（画素）。省くと <see cref="ColumnWidth"/>。ウィンドウが横に長いときは広げる（→実装メモ5.74）。</param>
    public SizeF Layout(PointF origin, float scale, int columns, SettingsSections sections = SettingsSections.All, float? columnWidth = null)
    {
        if (_painter is null || _painter.Scale != scale)
        {
            _painter?.Dispose();
            _painter = new UiPainter(_style, scale);
        }

        _targets.Clear();
        _sections.Clear();
        _stepperRows.Clear();
        _labels.Clear();
        _intros.Clear();
        _wristSection = RectangleF.Empty;
        _warningSection = RectangleF.Empty;
        _warningBody = RectangleF.Empty;
        _codeBlock = RectangleF.Empty;
        _lastRunRow = RectangleF.Empty;

        _origin = origin;
        _sectionWidth = columnWidth ?? S(ColumnWidth);
        FitEmphasisScale();
        _columnTops = new float[Math.Max(1, columns)];
        Array.Fill(_columnTops, origin.Y);

        foreach (var which in (sections & (SettingsSections.SetupRetention | SettingsSections.SetupWrist)) != 0 ? SetupOrder : Order)
        {
            if (sections.HasFlag(which))
                LayoutSection(which);
        }

        Bounds = _sections.Count == 0
            ? new RectangleF(origin, SizeF.Empty)
            : RectangleF.FromLTRB(origin.X, origin.Y, _sections.Max(s => s.Rect.Right), _sections.Max(s => s.Rect.Bottom));

        _pointedTarget = _pointer is { } p ? HitAt(p) : null;
        _hoveredTarget = _pointer is { } h ? TargetAt(h) : null;
        _lastLayout = (origin, scale, columns, sections, columnWidth);
        Dirty = true;
        return Bounds.Size;
    }

    /// <summary>前と同じ場所・倍率・まとまりで並べ直す（注記の行数が変わることがある）。</summary>
    private void Relayout()
    {
        if (_lastLayout is { } last)
            Layout(last.Origin, last.Scale, last.Columns, last.Sections, last.ColumnWidth);
    }

    private void LayoutSection(SettingsSections which)
    {
        switch (which)
        {
            case SettingsSections.History:
                LayoutHistory();
                break;

            case SettingsSections.SetupRetention:
                LayoutSetupRetention();
                break;

            case SettingsSections.SetupWrist:
                LayoutSetupWrist();
                break;

            case SettingsSections.Afk:
                LayoutAfk();
                break;

            case SettingsSections.Overlay:
                LayoutOverlay();
                break;

            case SettingsSections.TargetTypes:
                LayoutTargetTypes();
                break;

            case SettingsSections.Startup:
                LayoutStartup();
                break;

            case SettingsSections.WristPanel:
                LayoutWristPanel();
                break;

            case SettingsSections.ResetWarning:
                LayoutResetWarning();
                break;

            case SettingsSections.Rows:
                LayoutRows();
                break;

            case SettingsSections.Photos:
                LayoutPhotos();
                break;

            case SettingsSections.Window:
                LayoutWindow();
                break;

            case SettingsSections.External:
                LayoutExternal();
                break;
        }
    }

    // ------------------------------------------------------------------ まとまりごと

    private void LayoutHistory()
    {
        // 最上部に「履歴の自動リセットを有効にする」（2026-09-27のユーザー指定→実装メモ5.71。下の断りは2026-10-01のユーザー指定で外した→5.109）。
        // リセットまでの時間の下に「直前のリセットを戻す」（2026-09-28のユーザー指定→実装メモ5.86）。
        // 「直前のリセットを戻す」の上に「AFK中はカウントダウンを停止する」とその断り（2026-09-29のユーザー指定→実装メモ5.89）。
        var oscNote = WrapIntro(OscNote);
        // 「リセットまでの時間」のすぐ下に「訪問履歴に含めるインスタンスタイプに滞在中はカウントダウンを停止する」（2026-09-29のユーザー指定→実装メモ5.90）。
        var history = AddSection(SettingsSections.History, "履歴",
            S(CheckRowHeight) + StepperHeight(SettingsStepper.Retention) + (S(CheckRowHeight) * 2f) + IntroHeight(oscNote) + S(ButtonHeight), []);
        var top = BodyTopOf(history);

        top += AddCheck(HitKind.AutoReset, history, top);
        top += AddStepper(SettingsStepper.Retention, history, top);
        top += AddCheck(HitKind.TargetPause, history, top);
        top += AddCheck(HitKind.AfkPause, history, top);
        top += AddIntro(history, top, oscNote) - S(ButtonGap);

        AddButton(HitKind.UndoClear, history, top);
    }

    private void LayoutSetupRetention()
    {
        var history = AddSection(SettingsSections.SetupRetention, "履歴", StepperHeight(SettingsStepper.Retention), []);
        AddStepper(SettingsStepper.Retention, history, BodyTopOf(history));
    }

    private void LayoutSetupWrist()
    {
        var wrist = AddSection(SettingsSections.SetupWrist, "手首パネル", S(StepperRowHeight), []);
        AddChoices(HitKind.WristSide, "パネル位置", WristChoices.Length, wrist, BodyTopOf(wrist), inline: true);
    }

    private void LayoutAfk()
    {
        // オンにしたその場で OSC の受け口を開き、Windows のファイアウォールの許可を求められる（2026-09-30のユーザー指定→実装メモ5.98）。
        var afkNote = WrapIntro(AfkDetectionNote);
        var afk = AddSection(SettingsSections.Afk, "AFK 検知", S(CheckRowHeight) + IntroHeight(afkNote) - S(IntroGap), []);
        var top = BodyTopOf(afk);
        top += AddCheck(HitKind.AfkDetection, afk, top);
        AddIntro(afk, top, afkNote);
    }

    private void LayoutOverlay()
    {
        // 「コントローラーの振動」は手首パネルのまとまりの最下部へ移した（2026-09-29のユーザー指定→実装メモ5.90）。
        var overlay = AddSection(SettingsSections.Overlay, "VR オーバーレイ", S(CheckRowHeight), []);
        AddCheck(HitKind.VrOverlay, overlay, BodyTopOf(overlay));
    }

    private void LayoutTargetTypes()
    {
        // まとまりの中は2列。選択肢は「リセットまでの時間」と同じ EmphasisScale 倍で描く（→実装メモ5.106）。
        var typeRows = (TargetAccessTypes.Selectable.Count + 1) / 2;
        var types = AddSection(SettingsSections.TargetTypes, "訪問履歴に含めるインスタンスタイプ", typeRows * E(CheckRowHeight), []);

        var inner = types.X + S(SectionPadding);
        var width = types.Width - S(SectionPadding * 2f);

        for (var i = 0; i < TargetAccessTypes.Selectable.Count; i++)
        {
            var rect = new RectangleF(
                inner + ((i % 2) * width / 2f),
                BodyTopOf(types) + ((i / 2) * E(CheckRowHeight)),
                width / 2f,
                E(CheckRowHeight));

            _targets.Add(new HitTarget(HitKind.TargetType, rect, i));
        }
    }

    private void LayoutStartup()
    {
        // SteamVRを起動していないときに変えられない理由は、グレーアウトした項目を指したときの吹き出しで出す（→実装メモ5.67）。
        var startup = AddSection(SettingsSections.Startup, "起動", S(CheckRowHeight) * 2f,
        [
            "アプリはタスクトレイに格納された状態で開始します。",
        ]);

        var top = BodyTopOf(startup);
        top += AddCheck(HitKind.LaunchWithSteamVr, startup, top);
        AddCheck(HitKind.LaunchAtLogon, startup, top);
    }

    private void LayoutWristPanel()
    {
        var intro = WrapIntro("VRChat のメインメニュー（大きいメニュー）が出ている間だけ表示されます。");

        // 掴んで動かせることの説明は、最上部から「手首パネルの移動」の下へ移した（2026-09-28のユーザー指定→実装メモ5.78）。
        var grabNote = WrapIntro("有効な場合、手首パネルは掴んで移動できます。");
        var body = IntroHeight(intro) + S(StepperRowHeight) + S(CheckRowHeight) + IntroHeight(grabNote) + S(CheckRowHeight)
            + (SettingsSteppers.WristOrder.Length * S(StepperRowHeight)) + S(CheckRowHeight) + ((S(ButtonGap) + S(ButtonHeight)) * 2f) + S(ButtonGap) + S(CheckRowHeight);
        var vr = AddSection(SettingsSections.WristPanel, "手首パネル", body, []);
        var top = BodyTopOf(vr);

        top += AddIntro(vr, top, intro);

        // 付ける手首（→実装メモ5.49）。
        top += AddChoices(HitKind.WristSide, "パネル位置", WristChoices.Length, vr, top, inline: true);
        top += AddCheck(HitKind.PanelGrab, vr, top);
        top += AddIntro(vr, top, grabNote);
        top += AddCheck(HitKind.ShowDuringLoading, vr, top);

        foreach (var id in SettingsSteppers.WristOrder)
            top += AddStepper(id, vr, top);

        // 「手首パネルの位置をデフォルトに戻す」の上に「トリガーで操作メニューを表示」（2026-09-28のユーザー指定→実装メモ5.86）。
        top += AddCheck(HitKind.TriggerMenu, vr, top);
        top += AddButton(HitKind.ResetPlacement, vr, top);

        // まとまりの末尾に「手首パネルのパラメータをデフォルトに戻す」（2026-09-29のユーザー指定→実装メモ5.89）。
        top += AddButton(HitKind.ResetWristParameters, vr, top);

        // 最下部に「コントローラーの振動を有効にする」（2026-09-29のユーザー指定→実装メモ5.90）。
        AddCheck(HitKind.Vibration, vr, top + S(ButtonGap));

        // VRオーバーレイ機能をオフにしている間は、この枠をまるごと薄くする（→実装メモ5.71）。
        _wristSection = vr;
    }

    private void LayoutResetWarning()
    {
        // 上から、表示する・表示位置・表示サイズ・不透明度・点滅回数・表示タイミング・AFKから復帰時に再表示・その断り
        // （2026-09-29のユーザー指定→実装メモ5.89）。
        // 最上部に説明（2026-09-29のユーザー指定→実装メモ5.90）。
        var oscNote = WrapIntro(OscNote);
        var intro = WrapIntro("指定した表示タイミングになった時、VRChatのマイクアイコンそばに通知が表示されます。");

        // マイクアイコン位置の下に、合わせ方の説明（→実装メモ5.92）。
        var micNote = WrapIntro(MicNote);
        var body = IntroHeight(intro) + S(CheckRowHeight) + (SettingsSteppers.WarningOrder.Length * S(StepperRowHeight)) + IntroHeight(micNote)
            + S(CheckRowHeight) + IntroHeight(oscNote) - S(IntroGap);
        var warning = AddSection(SettingsSections.ResetWarning, "履歴自動リセットの予告通知", body, []);
        var top = BodyTopOf(warning);

        top += AddIntro(warning, top, intro);
        top += AddCheck(HitKind.ResetWarning, warning, top);
        var bodyTop = top;

        foreach (var id in SettingsSteppers.WarningOrder)
        {
            top += AddStepper(id, warning, top);

            // 合わせ方の説明は、マイクアイコン位置の3つの下。
            if (id == SettingsStepper.MicOffsetZ)
                top += AddIntro(warning, top, micNote);
        }

        top += AddCheck(HitKind.WarningReshow, warning, top);
        AddIntro(warning, top, oscNote);

        _warningSection = warning;
        _warningBody = RectangleF.FromLTRB(warning.X, bodyTop, warning.Right, warning.Bottom - S(SectionPadding / 2f));
    }

    private void LayoutRows()
    {
        // インスタンス詳細とグループ名は別の枠にする（2026-09-27のユーザー指定）。
        // 選択肢の文字が長いので、横に並べずに縦に積む（→実装メモ5.65）。
        // 「再起動指定時はボタンが「ここへ戻る」になります。」は紛らわしいので外した（→実装メモ5.66）。
        var rows = AddSection(SettingsSections.Rows, "インスタンス操作", StackedChoicesHeight(ReturnChoices.Length),
        [
            "VRChat クライアントを再起動すると、FBT キャリブレーションなどはリセットされます。",
        ]);
        AddChoices(HitKind.ReturnAction, "インスタンス操作の挙動", ReturnChoices.Length, rows, BodyTopOf(rows), inline: false, stacked: true);

        // 説明は枠の最上部に置く（2026-09-27のユーザー指定→実装メモ5.66）。インポートに触れるのはウィンドウだけ。
        var groupIntro = WrapIntro(FileDialogs
            ? "ログからは読み取れないグループ名を手動でつけることができます。該当する訪問履歴のインスタンス詳細から設定できます。json ファイルからインポートすることもできます。"
            : "ログからは読み取れないグループ名を手動でつけることができます。該当する訪問履歴のインスタンス詳細から設定できます。");

        // 読み込み・書き出しは、ファイルを選ぶ画面を出せるデスクトップのウィンドウだけ（→実装メモ5.65）。
        // ボタンの上に項目名「グループ名リストのjson」を置く（→実装メモ5.66）。
        var fileRow = FileDialogs ? S(4f) + S(ChoiceLabelHeight) + S(LabelToButtonGap) + S(ButtonHeight) : 0f;
        var groups = AddSection(SettingsSections.Rows, "グループ名", IntroHeight(groupIntro) + S(CheckRowHeight) + fileRow,
            [], column: 0); // 2列のときは左へ置き、右の列だけが長くならないようにする。

        var top = BodyTopOf(groups);
        top += AddIntro(groups, top, groupIntro);
        top += AddCheck(HitKind.GroupIdWithName, groups, top);

        if (!FileDialogs)
            return;

        var inner = groups.X + S(SectionPadding);
        var width = groups.Width - S(SectionPadding * 2f);
        var half = (width - S(ChoiceSegmentGap * 2f)) / 2f;

        _labels.Add((new RectangleF(inner, top + S(4f), width, S(ChoiceLabelHeight)), "グループ名リストの json"));
        var y = top + S(4f) + S(ChoiceLabelHeight) + S(LabelToButtonGap);

        _targets.Add(new HitTarget(HitKind.GroupImport, new RectangleF(inner, y, half, S(ButtonHeight))));
        _targets.Add(new HitTarget(HitKind.GroupExport, new RectangleF(inner + width - half, y, half, S(ButtonHeight))));
    }

    private void LayoutPhotos()
    {
        var photos = AddSection(SettingsSections.Photos, "写真", S(StepperRowHeight), [PhotoViewerNote()]);
        AddChoices(HitKind.PhotoViewer, "写真を開くアプリ", ViewerChoices.Length, photos, BodyTopOf(photos), inline: true);
    }

    private void LayoutWindow()
    {
        var window = AddSection(SettingsSections.Window, "デスクトップウィンドウ", S(CheckRowHeight), []);
        AddCheck(HitKind.TopMost, window, BodyTopOf(window));
    }

    private void LayoutExternal()
    {
        // 上から、受け付けるかの切り替え・コマンド（右端に「コピー」）・最終実行・説明（2026-09-28のユーザー指定→実装メモ5.83）。
        // 説明の2文目は長いので、1文の中の折り返しを多めに許す。
        var about = WrapIntro(ExternalAbout, maxLines: 6);
        var body = S(CheckRowHeight) + S(ExternalGap) + S(CodeBlockHeight) + S(ExternalGap) + S(ChoiceLabelHeight) + S(ExternalGap) + IntroHeight(about) - S(IntroGap);
        var external = AddSection(SettingsSections.External, "外部連携", body, []);
        var top = BodyTopOf(external);
        var inner = external.X + S(SectionPadding);
        var width = external.Width - S(SectionPadding * 2f);

        top += AddCheck(HitKind.ExternalReset, external, top);
        top += S(ExternalGap);

        _codeBlock = new RectangleF(inner, top, width, S(CodeBlockHeight));
        _targets.Add(new HitTarget(
            HitKind.CopyCommand,
            new RectangleF(_codeBlock.Right - S(CodePadding / 2f) - S(CopyButtonWidth), top + ((S(CodeBlockHeight) - S(CopyButtonHeight)) / 2f), S(CopyButtonWidth), S(CopyButtonHeight))));
        top += S(CodeBlockHeight) + S(ExternalGap);

        _lastRunRow = new RectangleF(inner, top, width, S(ChoiceLabelHeight));
        top += S(ChoiceLabelHeight) + S(ExternalGap);

        AddIntro(external, top, about);
    }

    // ------------------------------------------------------------------ 部品を置く

    /// <summary>
    /// まとまりの枠を積む。<paramref name="column"/> を省くと、2列のときは <see cref="RightColumn"/> なら右、そうでなければ左に置く。
    /// 注記は幅に収まらなければ折り返す（1つの注記は3行まで）。
    /// </summary>
    private RectangleF AddSection(SettingsSections which, string title, float bodyHeight, IReadOnlyList<string> notes, int? column = null)
    {
        var index = _columnTops.Length >= 2 ? column ?? ((RightColumn & which) != 0 ? 1 : 0) : 0;
        index = Math.Min(index, _columnTops.Length - 1);

        var wrapped = notes.SelectMany(note => Painter.Wrap(note, Painter.Fonts.Absence, _sectionWidth - S(SectionPadding * 2f), maxLines: 3)).ToList();

        var x = _origin.X + (index * (_sectionWidth + S(ColumnGap)));
        var notesHeight = wrapped.Count * S(NoteLineHeight);
        // 見出しの帯と部品の間・部品と注記の間にも空きを取る（それまでは0で、下の余白12pxと釣り合わなかった→実装メモ5.67）。
        var notesGap = wrapped.Count > 0 ? S(NotesGap) : 0f;
        var rect = new RectangleF(x, _columnTops[index], _sectionWidth, S(SectionTitleHeight) + S(BodyTop) + bodyHeight + notesGap + notesHeight + S(SectionPadding));

        _sections.Add(new Section(rect, title, wrapped, rect.Y + S(SectionTitleHeight) + S(BodyTop) + bodyHeight + notesGap));
        _columnTops[index] = rect.Bottom + S(SectionGap);
        return rect;
    }

    /// <summary>
    /// まとまりの最上部の説明を幅で折り返す。高さは <see cref="IntroHeight"/>、置くのは <see cref="AddIntro"/>。
    /// 句点ごとに行を改める（2026-09-27のユーザー指定→実装メモ5.68）。1文が幅に収まらなければ、その中で折り返す。
    /// </summary>
    private List<string> WrapIntro(string text, int maxLines = 3)
        => [.. text.Split('。', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(sentence => Painter.Wrap(sentence + "。", Painter.Fonts.Absence, _sectionWidth - S(SectionPadding * 2f), maxLines))];

    private float IntroHeight(IReadOnlyList<string> lines) => (lines.Count * S(NoteLineHeight)) + S(IntroGap);

    private float AddIntro(RectangleF section, float top, IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
            _intros.Add((new PointF(section.X + S(SectionPadding), top + (i * S(NoteLineHeight))), lines[i]));

        return IntroHeight(lines);
    }

    /// <summary>数値の部品の行を置く。戻り値は行の高さ。</summary>
    private float AddStepper(SettingsStepper id, RectangleF section, float top)
    {
        var prominent = SettingsSteppers.Spec(id).Prominent;
        var button = prominent ? E(ProminentButtonSize) : S(StepButtonSize);
        var valueWidth = prominent ? E(ProminentValueWidth) : S(StepValueWidth);
        var inner = section.X + S(SectionPadding);
        var row = new RectangleF(inner, top, section.Width - S(SectionPadding * 2f), StepperHeight(id));
        var buttonY = row.Y + ((row.Height - button) / 2f);
        var plus = new RectangleF(row.Right - button, buttonY, button, button);
        var minus = new RectangleF(plus.X - valueWidth - button, buttonY, button, button);

        _stepperRows[id] = new RectangleF(section.X, row.Y, section.Width, row.Height);
        _targets.Add(new HitTarget(HitKind.StepperMinus, minus, (int)id));
        _targets.Add(new HitTarget(HitKind.StepperPlus, plus, (int)id));
        return row.Height;
    }

    /// <summary>
    /// 選択肢を並べる。<paramref name="inline"/> なら右寄せの幅（−／＋ と同じ）に、そうでなければ行の幅いっぱいに横へ並べる。
    /// <paramref name="stacked"/> なら、ラベルの下に1つずつ行の幅いっぱいで縦に積む（選択肢の文字が長いとき→実装メモ5.65）。
    /// 戻り値は使った高さ。
    /// </summary>
    private float AddChoices(HitKind kind, string label, int count, RectangleF section, float top, bool inline, bool stacked = false)
    {
        var inner = section.X + S(SectionPadding);
        var width = section.Width - S(SectionPadding * 2f);

        if (stacked)
        {
            _labels.Add((new RectangleF(inner, top, width, S(ChoiceLabelHeight)), label));

            for (var i = 0; i < count; i++)
            {
                var y = top + S(ChoiceLabelHeight) + S(LabelToButtonGap) + (i * (S(ChoiceSegmentHeight) + S(ChoiceSegmentGap)));
                _targets.Add(new HitTarget(kind, new RectangleF(inner, y, width, S(ChoiceSegmentHeight)), i));
            }

            return StackedChoicesHeight(count);
        }

        float segmentsLeft;
        float segmentsWidth;
        float segmentsTop;
        float height;

        if (inline)
        {
            height = S(StepperRowHeight);
            segmentsWidth = S(StepValueWidth) + (S(StepButtonSize) * 2f);
            segmentsLeft = inner + width - segmentsWidth;
            segmentsTop = top + ((height - S(ChoiceSegmentHeight)) / 2f);
            _labels.Add((new RectangleF(inner, top, width - segmentsWidth, height), label));
        }
        else
        {
            height = S(ChoiceLabelHeight) + S(ChoiceSegmentHeight) + S(6f);
            segmentsWidth = width;
            segmentsLeft = inner;
            segmentsTop = top + S(ChoiceLabelHeight);
            _labels.Add((new RectangleF(inner, top, width, S(ChoiceLabelHeight)), label));
        }

        var gap = S(ChoiceSegmentGap);
        var segment = (segmentsWidth - (gap * (count - 1))) / count;

        for (var i = 0; i < count; i++)
            _targets.Add(new HitTarget(kind, new RectangleF(segmentsLeft + (i * (segment + gap)), segmentsTop, segment, S(ChoiceSegmentHeight)), i));

        return height;
    }

    /// <summary>オン・オフの部品の行を置く。戻り値は行の高さ。</summary>
    private float AddCheck(HitKind kind, RectangleF section, float top)
    {
        _targets.Add(new HitTarget(kind, new RectangleF(section.X + S(SectionPadding), top, section.Width - S(SectionPadding * 2f), S(CheckRowHeight))));
        return S(CheckRowHeight);
    }

    /// <summary>枠の幅いっぱいのボタンを、上に少し空けて置く。戻り値は空きとボタンの高さ。</summary>
    private float AddButton(HitKind kind, RectangleF section, float top)
    {
        _targets.Add(new HitTarget(kind, new RectangleF(section.X + S(SectionPadding), top + S(ButtonGap), section.Width - S(SectionPadding * 2f), S(ButtonHeight))));
        return S(ButtonGap) + S(ButtonHeight);
    }

    /// <summary>まとまりの部品を置き始める位置（見出しの帯の下に空きを取った所）。</summary>
    private float BodyTopOf(RectangleF section) => section.Y + S(SectionTitleHeight) + S(BodyTop);

    /// <summary>選択肢を縦に積んだときの高さ（ラベルの段を含む）。</summary>
    private float StackedChoicesHeight(int count)
        => S(ChoiceLabelHeight) + S(LabelToButtonGap) + (count * S(ChoiceSegmentHeight)) + ((count - 1) * S(ChoiceSegmentGap)) + S(6f);

    /// <summary>数値の部品1つの行の高さ（目立たせるものは高い）。</summary>
    private float StepperHeight(SettingsStepper id) => SettingsSteppers.Spec(id).Prominent ? E(ProminentRowHeight) : S(StepperRowHeight);

    /// <summary>
    /// <see cref="EmphasizeToFit"/> のとき、「リセットまでの時間」の名前・−／＋・値が列の内側の幅に1行で収まる最大の倍率を求め、
    /// その倍率の描き手を用意する（2026-10-01のユーザー指定→実装メモ5.106）。名前と − の間は名前の字1つぶん（字の大きさ）空ける。
    /// 1倍でも収まらないときは1倍のまま（ほかの部品より小さくはしない）。
    /// </summary>
    private void FitEmphasisScale()
    {
        var factor = 1f;

        if (EmphasizeToFit)
        {
            var room = _sectionWidth - S(SectionPadding * 2f);
            factor = MathF.Max(1f, room / RetentionRowWidth(Painter));

            // 字の幅は倍率にほぼ比例するが、字形の合わせ込みでわずかに外れるので、測り直して収まるまで下げる。
            while (factor > 1f)
            {
                SetEmphasisPainter(factor);

                if (RetentionRowWidth(Emphasis) <= room)
                    break;

                factor = MathF.Max(1f, factor - EmphasisStep);
            }
        }

        SetEmphasisPainter(factor);
    }

    /// <summary>「リセットまでの時間」の行を1行に並べるのに要る幅（名前・名前の字1つぶんの空き・−・値・＋）。</summary>
    private static float RetentionRowWidth(UiPainter painter)
    {
        var label = painter.MeasureWidth(SettingsSteppers.Spec(SettingsStepper.Retention).Label, painter.ProminentLabel);
        return label + painter.ProminentLabel.Size + painter.S((ProminentButtonSize * 2f) + ProminentValueWidth);
    }

    private void SetEmphasisPainter(float factor)
    {
        EmphasisScale = factor;

        if (factor == 1f)
        {
            _emphasisPainter?.Dispose();
            _emphasisPainter = null;
            return;
        }

        var scale = Painter.Scale * factor;

        if (_emphasisPainter?.Scale == scale)
            return;

        _emphasisPainter?.Dispose();
        _emphasisPainter = new UiPainter(_style, scale);
    }

    // ------------------------------------------------------------------ 説明の文

    /// <summary>マイクアイコン位置の説明（→実装メモ5.92）。</summary>
    private const string MicNote = "アイコンの位置がマイクアイコンとずれるときに合わせます。予告通知の設定を変えている間は、VR 内にアイコンを表示し続けます。";

    /// <summary>「VRChatのAFKを検知する」の断り（2026-09-30のユーザー指定→実装メモ5.98）。オンにするとファイアウォールの許可を求められる。</summary>
    private const string AfkDetectionNote = "ネットワークへのアクセス許可が必要です。";

    /// <summary>
    /// AFK を使う2つの設定の断り（→実装メモ5.89）。オンにするとファイアウォールの許可を求められるので、その断りを冒頭に置く
    /// （2026-09-30のユーザー指定→実装メモ5.99）。句点ごとに行を改めるので2行になる。
    /// </summary>
    private const string OscNote = "ネットワークへのアクセス許可が必要です。VRChat側でOSCが有効である必要があります。";

    /// <summary>外部連携の説明（→実装メモ5.83）。句点ごとに行を改める。</summary>
    private static readonly string ExternalAbout =
        $"このコマンドが実行されると、{AppInfo.DisplayName} は即座に訪問履歴をリセットします。" +
        "例えば、OyasumiVR のコマンドの実行機能にこのコマンドを入力しておくことで、睡眠モード有効時に訪問履歴を自動でリセットできます。";

    /// <summary>写真を開くアプリの注記。選んだアプリがあれば、その名前を出す。</summary>
    private string PhotoViewerNote()
        => _settings.PhotoViewer == PhotoViewerKind.Custom && _settings.PhotoViewerPath is { } path
            ? $"指定アプリ: {Path.GetFileName(path)}"
            : "写真はサムネイル画像をダブルクリックすると開きます。";
}
