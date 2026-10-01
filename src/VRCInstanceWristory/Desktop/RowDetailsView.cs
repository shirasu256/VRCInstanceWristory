using System.Drawing;
using System.Drawing.Drawing2D;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Marks;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Desktop;

/// <summary>
/// デスクトップのウィンドウの「選んだ行」のページ（2026-09-26のユーザー指定→実装メモ5.42）。
///
/// パネルで行をクリックすると、その訪問の詳しい情報をここに出す。
/// <list type="bullet">
/// <item>「ブラウザで開く」／「ここへ戻る」（→5.43・5.53）と、目印の付け外し（パネルのポップアップと同じ規則→5.32）</item>
/// <item>グループ名を付ける・変える（→5.48）</item>
/// <item>撮った写真のサムネイル。押すとその写真を設定のアプリ（既定はその種類のファイルの既定のアプリ）で開く（→5.47・5.55）</item>
/// <item>滞在中に一緒にいた人の一覧（→5.46）</item>
/// </list>
///
/// 見た目は設定のまとまりと同じ（見出しの帯と本文の2段）にしてある。ウィンドウ（Win32）とは切り離してあり、
/// 自動検証と <c>--render-sample --window</c> がウィンドウを開かずに同じ絵を確かめられる。
/// </summary>
public sealed class RowDetailsView
{
    // 寸法は論理px（96dpi）。

    private const float SectionGap = UiMetrics.CardGap;
    private const float SectionTitleHeight = UiMetrics.CardTitleHeight;
    private const float SectionPadding = UiMetrics.CardPadding;
    private const float NoteLineHeight = UiMetrics.NoteLineHeight;
    private const float LineHeight = 20f;
    private const float ButtonHeight = 28f;
    private const float MarkButtonSize = 32f;
    private const float MarkButtonGap = 8f;

    /// <summary>
    /// 先頭の枠（インスタンス番号・滞在中・ワールド名・タイプ）の見出しの高さ。ほかの枠の1.3倍（→実装メモ5.69）から、
    /// さらに0.9倍にした（1.17倍・28→32.76px→5.70）。字の大きさは1.3倍のまま。
    /// </summary>
    private const float VisitTitleHeight = SectionTitleHeight * 1.3f * 0.9f;

    /// <summary>Group ID のリンクの字の左右の余白（字は枠の左端にそろえ、余白は左へはみ出させる）。</summary>
    private const float GroupLinkPadding = 6f;

    private const float PeopleLineHeight = 18f;

    /// <summary>サムネイルを横に並べる数と、その間（→5.55）。高さは 16:9。</summary>
    private const int PhotoColumns = 3;

    private const float PhotoGap = 6f;

    /// <summary>サムネイルを並べる段の上限。入り切らない分は最後の枠を「ほか n枚」（押すとフォルダーを開く）にする。</summary>
    private const int MaxPhotoRows = 3;

    /// <summary>一緒にいた人の欄に残す高さの下限（見出しと2行）。</summary>
    private const float MinPeopleBody = 40f;

    private const string GroupNameLabel = "グループ名:";

    private const string FolderLabel = "フォルダーを開く";

    /// <summary>滞在しているインスタンスの行で、番号の後ろに添える文字（2026-09-27に「（いまここにいます）」から改めた）。</summary>
    private const string HereLabel = "滞在中";

    /// <summary>行を選んでいないときの案内（2026-09-27のユーザー指定→実装メモ5.68）。</summary>
    private const string EmptyText = "インスタンス番号をクリックして詳細を表示";

    /// <summary>先頭の枠の末尾の案内（→実装メモ5.69）。</summary>
    private const string MarkNote = "ブックマークしておきたいインスタンスにアイコンをつけることができます。";

    private readonly PanelStyle _style;
    private readonly Action<DesktopCommand> _emit;

    private UiPainter? _painter;
    private RectangleF _bounds;
    private DisplayRow? _row;
    private RowDetail? _detail;
    private ReturnAction _returnAction = ReturnAction.Browser;
    private PointF? _pointer;
    private Target? _pointed;

    // グループ名を打ち込む欄を開いてほしい、という依頼。ウィンドウが取り出す（TakeTextEditRequest）。
    private TextEditRequest? _pendingTextEdit;

    // 配置（Relayout で決める）。
    private readonly List<Target> _targets = [];
    private readonly List<Card> _cards = [];

    // 枠ごとの矩形（出していない枠は空）。
    private RectangleF _visitCard;
    private RectangleF _groupCard;
    private RectangleF _photosCard;
    private RectangleF _peopleCard;

    // グループの枠の名前の欄と Group ID のリンク（グループのインスタンスでなければ null）。
    private Target? _groupField;
    private Target? _groupLink;

    // 先頭の枠の末尾の案内を幅で折り返したものと、その上端。
    private IReadOnlyList<string> _markNote = [];
    private float _markNoteTop;

    // 一緒にいた人の並び。上端・1列の行数・出す人数・1列の幅。
    private (float Top, int Rows, int Shown, float ColumnWidth) _peopleLayout;

    public RowDetailsView(PanelStyle style, Action<DesktopCommand> emit)
    {
        _style = style;
        _emit = emit;
    }

    public enum HitKind
    {
        /// <summary>「ブラウザで開く」／「ここへ戻る」（→5.43・5.53）。</summary>
        Return,

        /// <summary>目印（番号は <see cref="InstanceMarks.Choices"/> の並び）。</summary>
        Mark,

        /// <summary>グループ名の欄（→5.48）。押すとその場で打ち込める（→5.67）。</summary>
        GroupName,

        /// <summary>Group ID のリンク。押すとそのグループの vrchat.com のページを開く（→5.69）。</summary>
        GroupLink,

        /// <summary>このインスタンスにいたユーザー1人（番号は <see cref="RowDetail.Companions"/> の並び）。押すとそのユーザーのページを開く（→5.75）。</summary>
        Person,

        /// <summary>写真のフォルダーを開く（→5.47）。写真の欄の見出しの右（番号 -1）と、入り切らないときの「ほか n枚」（番号は残りの枚数）。</summary>
        OpenPhotos,

        /// <summary>写真1枚（番号は <see cref="RowDetail.Photos"/> の並び→5.55）。ダブルクリックでその写真を開く（→5.65）。</summary>
        Photo,
    }

    private readonly record struct Target(HitKind Kind, RectangleF Rect, int Index = 0);

    /// <param name="BandHeight">見出しの帯の高さ（画素）。見出しのない枠は0。</param>
    /// <param name="IsVisit">先頭の枠（見出しに番号・滞在中・ワールド名を分けて描く）。</param>
    private sealed record Card(RectangleF Rect, string Title, float BandHeight, bool IsVisit = false);

    /// <summary>描き直しが必要か。</summary>
    public bool Dirty { get; private set; } = true;

    /// <summary>インスタンス操作の挙動（設定 <c>returnAction</c>→5.53）。ボタンの文字と押せるかが変わる。</summary>
    public ReturnAction ReturnAction
    {
        get => _returnAction;
        set
        {
            if (_returnAction == value)
                return;

            _returnAction = value;
            _pointed = _pointer is { } p ? HitAt(p) : null;
            Dirty = true;
        }
    }

    /// <summary>
    /// 写真のサムネイルを返す（写真の場所 → 絵。まだなければ null→5.55）。ウィンドウがサムネイルの置き場所から読む。
    /// 返した絵はこちらでは捨てない（持ち主が管理する）。
    /// </summary>
    public Func<string, Image?>? Thumbnails { get; set; }

    /// <summary>サムネイルができた。描き直す。</summary>
    public void ThumbnailsChanged() => Dirty = true;

    /// <summary>グループ名を打ち込む欄を開いてほしい、という依頼を取り出す。</summary>
    public TextEditRequest? TakeTextEditRequest()
    {
        var request = _pendingTextEdit;
        _pendingTextEdit = null;
        return request;
    }

    private UiPainter Painter => _painter ?? throw new InvalidOperationException("Layout の前です。");

    private float S(float logical) => Painter.S(logical);

    /// <summary>出す行を差し替える。どちらかが null なら「行を選んでいない」ページになる。</summary>
    public void SetRow(DisplayRow? row, RowDetail? detail)
    {
        if (row == _row && Equals(detail, _detail))
            return;

        _row = row;
        _detail = detail;
        Relayout();
    }

    /// <summary>
    /// <paramref name="bounds"/> の中へ並べる。<paramref name="painter"/> は置く側のもので、置く側が倍率を変えて作り直したら
    /// 古いものを手放す前にここへ渡し直す（手放した字で並べると例外になる）。
    /// </summary>
    public void Layout(RectangleF bounds, UiPainter painter)
    {
        _bounds = bounds;
        _painter = painter;
        Relayout();
    }

    private void Relayout()
    {
        _targets.Clear();
        _cards.Clear();
        _visitCard = _groupCard = _photosCard = _peopleCard = RectangleF.Empty;
        _groupField = null;
        _groupLink = null;
        Dirty = true;

        if (_painter is null)
            return;

        var x = _bounds.X;
        var width = _bounds.Width;
        var top = _bounds.Y;
        var inner = x + S(SectionPadding);
        var innerWidth = width - S(SectionPadding * 2f);

        // 見出しのない枠（title が空）は、見出しの帯を取らない。
        RectangleF AddCard(string title, float bodyHeight, float titleHeightLogical = SectionTitleHeight, bool isVisit = false)
        {
            var band = title.Length == 0 ? 0f : S(titleHeightLogical);
            var titleHeight = title.Length == 0 ? S(SectionPadding) : band;
            var rect = new RectangleF(x, top, width, titleHeight + bodyHeight + S(SectionPadding));
            _cards.Add(new Card(rect, title, band, isVisit));
            top = rect.Bottom + S(SectionGap);
            return rect;
        }

        // 行を選んでいないときは、見出しを付けずに案内の1行だけを出す（2026-09-27のユーザー指定→実装メモ5.68）。
        if (_row is null || _detail is null)
        {
            AddCard(string.Empty, S(LineHeight));
            return;
        }

        // 訪問（ここへ戻る・目印だけ→2026-09-27のユーザー指定）。ワールド名・種類・時刻はパネルの行で見える。
        // 行のボタンと目印は1段に並べ（左が目印、右が行のボタン。手首のパネルの目印のポップアップと同じ順→実装メモ5.32・5.101）、
        // 末尾に目印の案内を置く（→実装メモ5.69）。
        _markNote = Painter.Wrap(MarkNote, Painter.Fonts.Absence, innerWidth, maxLines: 3);
        var noteHeight = _markNote.Count * S(NoteLineHeight);
        _visitCard = AddCard(_row.InstanceId, S(8f) + S(MarkButtonSize) + S(6f) + noteHeight, VisitTitleHeight, isVisit: true);
        var y = _visitCard.Y + S(VisitTitleHeight) + S(8f);

        for (var i = 0; i < InstanceMarks.Choices.Count; i++)
            _targets.Add(new Target(HitKind.Mark, new RectangleF(inner + (i * (S(MarkButtonSize) + S(MarkButtonGap))), y, S(MarkButtonSize), S(MarkButtonSize)), i));

        _markNoteTop = y + S(MarkButtonSize) + S(6f);

        var returnLeft = inner + (InstanceMarks.Choices.Count * S(MarkButtonSize)) + ((InstanceMarks.Choices.Count - 1) * S(MarkButtonGap)) + S(MarkButtonGap * 2f);
        _targets.Add(new Target(HitKind.Return, new RectangleF(returnLeft, y, inner + innerWidth - returnLeft, S(MarkButtonSize))));

        // グループ（名前を付ける）。グループのインスタンスだけ。
        if (_detail.GroupId is not null)
        {
            // 「グループ名入力」のボタンはやめ、名前（未設定なら「未設定」）の欄そのものを押すと打ち込める（→実装メモ5.67）。
            // 欄は「グループ名:」の右から枠の右端まで。打ち込む欄（EDIT）も同じ矩形に置く。
            // 上に Group ID、その下に名前の欄（2026-09-27のユーザー指定で入れ替えた→実装メモ5.75）。
            var fieldHeight = S(ButtonHeight) - S(4f);
            _groupCard = AddCard("グループ", S(4f) + S(LineHeight) + S(4f) + fieldHeight);
            var linkY = _groupCard.Y + S(SectionTitleHeight) + S(4f);

            // Group ID はリンク（押すとそのグループのページをブラウザで開く→実装メモ5.69）。押せるのは字の幅だけ。字の左右に少し余白を取る。
            var linkWidth = MathF.Min(Painter.MeasureWidth(_detail.GroupId, Painter.Fonts.Absence) + S(GroupLinkPadding * 2f), innerWidth + S(GroupLinkPadding));
            _groupLink = new Target(HitKind.GroupLink, new RectangleF(inner - S(GroupLinkPadding), linkY, linkWidth, S(LineHeight)));
            _targets.Add(_groupLink.Value);

            var fieldY = linkY + S(LineHeight) + S(4f);
            var fieldLeft = inner + Painter.MeasureWidth(GroupNameLabel, Painter.Fonts.Aux) + S(8f);
            _groupField = new Target(HitKind.GroupName, new RectangleF(fieldLeft, fieldY, inner + innerWidth - fieldLeft, fieldHeight));
            _targets.Add(_groupField.Value);
        }

        // 写真（→5.55）。サムネイルを3列で並べ、一緒にいた人の欄に最低限の高さを残せるだけの段を使う。
        var photoCount = _detail.Photos.Count;
        var cellWidth = (innerWidth - (S(PhotoGap) * (PhotoColumns - 1))) / PhotoColumns;
        var cellHeight = MathF.Round(cellWidth * 9f / 16f);
        var neededRows = (photoCount + PhotoColumns - 1) / PhotoColumns;
        var spare = _bounds.Bottom - top - S(SectionTitleHeight) - S(SectionPadding) - S(SectionGap)
            - (S(SectionTitleHeight) + S(MinPeopleBody) + S(SectionPadding));
        var fitRows = Math.Max(1, (int)MathF.Floor((spare + S(PhotoGap)) / (cellHeight + S(PhotoGap))));
        var photoRows = Math.Min(Math.Min(neededRows, MaxPhotoRows), fitRows);
        var photoBody = photoCount == 0 ? S(8f) + S(LineHeight) : S(8f) + (photoRows * cellHeight) + ((photoRows - 1) * S(PhotoGap));
        _photosCard = AddCard(photoCount == 0 ? "写真" : $"写真（{photoCount}枚）", photoBody);

        if (photoCount > 0)
        {
            // 見出しの右に「フォルダーを開く」。
            var folderWidth = Painter.MeasureWidth(FolderLabel, Painter.Fonts.Absence) + S(16f);
            _targets.Add(new Target(HitKind.OpenPhotos, new RectangleF(_photosCard.Right - S(6f) - folderWidth, _photosCard.Y + S(4f), folderWidth, S(SectionTitleHeight) - S(8f)), -1));

            var capacity = photoRows * PhotoColumns;
            var shown = photoCount > capacity ? capacity - 1 : photoCount;
            var gridTop = _photosCard.Y + S(SectionTitleHeight) + S(8f);

            for (var i = 0; i < shown; i++)
                _targets.Add(new Target(HitKind.Photo, PhotoCell(inner, gridTop, cellWidth, cellHeight, i), i));

            // 入り切らない分は、最後の枠を「ほか n枚」にする（押すとフォルダーを開く）。
            if (shown < photoCount)
                _targets.Add(new Target(HitKind.OpenPhotos, PhotoCell(inner, gridTop, cellWidth, cellHeight, shown), photoCount - shown));
        }

        // 一緒にいた人は、残りの高さを使い切る。
        var people = _detail.Companions;
        var remaining = MathF.Max(S(LineHeight) * 2f, _bounds.Bottom - top - S(SectionTitleHeight) - S(SectionPadding));
        _peopleCard = AddCard(people.Count == 0 ? "このインスタンスにいたユーザー" : $"このインスタンスにいたユーザー（{people.Count}人）", remaining);

        // 1人ずつリンク（押すとそのユーザーの vrchat.com のページを開く→実装メモ5.75）。2列で上から詰める。
        var peopleTop = _peopleCard.Y + S(SectionTitleHeight) + S(8f);
        var peopleRows = Math.Max(1, (int)MathF.Floor((_peopleCard.Bottom - S(SectionPadding) - peopleTop) / S(PeopleLineHeight)));
        var peopleCapacity = peopleRows * 2;
        var shownPeople = people.Count > peopleCapacity ? peopleCapacity - 1 : people.Count;
        var columnWidth = innerWidth / 2f;

        for (var i = 0; i < shownPeople; i++)
        {
            var nameWidth = MathF.Min(Painter.MeasureWidth(people[i].Name, Painter.Fonts.Aux), columnWidth - S(8f));
            var cx = inner + ((i / peopleRows) * columnWidth);
            var cy = peopleTop + ((i % peopleRows) * S(PeopleLineHeight));
            _targets.Add(new Target(HitKind.Person, new RectangleF(cx, cy, nameWidth, S(PeopleLineHeight)), i));
        }

        _peopleLayout = (peopleTop, peopleRows, shownPeople, columnWidth);

        _pointed = _pointer is { } p ? HitAt(p) : null;
    }

    private RectangleF PhotoCell(float left, float top, float width, float height, int index)
        => new(
            left + ((index % PhotoColumns) * (width + S(PhotoGap))),
            top + ((index / PhotoColumns) * (height + S(PhotoGap))),
            width,
            height);

    // ------------------------------------------------------------------ ポインター

    public void PointerMove(PointF? point)
    {
        _pointer = point;
        var pointed = point is { } p ? HitAt(p) : null;

        if (pointed == _pointed)
            return;

        _pointed = pointed;
        Dirty = true;
    }

    /// <summary>押した。部品の上なら働かせて true。</summary>
    public bool PointerDown(PointF point)
    {
        PointerMove(point);

        if (HitAt(point) is not { } target || _row is null || _detail is null)
            return false;

        switch (target.Kind)
        {
            case HitKind.Return:
                _emit(new DesktopCommand.OpenInstance(_row.EventId));
                break;

            case HitKind.Mark:
                _emit(new DesktopCommand.SetMark(_row.EventId, InstanceMarks.Choices[target.Index]));
                break;

            case HitKind.OpenPhotos when _detail.LatestPhoto is { } photo:
                _emit(new DesktopCommand.OpenPhotoFolder(photo.Path));
                break;

            // 写真は1回押しただけでは開かない。ダブルクリックで開く（2026-09-27のユーザー指定→5.65・DoubleClick）。
            case HitKind.Photo:
                break;

            case HitKind.GroupName when _detail.GroupId is { } groupId:
                // 打ち込む欄は、押した名前の欄と同じ場所に置く（→実装メモ5.67）。
                _pendingTextEdit = new TextEditRequest(RectangleF.Inflate(target.Rect, -S(4f), -S(2f)), _detail.GroupName ?? string.Empty, groupId, Painter.Fonts.Aux.Size, Painter.Fonts.JapaneseFamilyName);
                break;

            case HitKind.GroupLink when _detail.GroupId is { } linkId:
                _emit(new DesktopCommand.OpenGroupPage(linkId));
                break;

            case HitKind.Person when target.Index < _detail.Companions.Count:
                _emit(new DesktopCommand.OpenUserPage(_detail.Companions[target.Index].UserId));
                break;
        }

        Dirty = true;
        return true;
    }

    /// <summary>
    /// ダブルクリック（2026-09-27のユーザー指定→5.65）。写真のサムネイルの上なら、その写真を開く。
    /// 写真を開くアプリの起動は時間がかかり、画面も替わるので、1回押しただけで開かないようにした。
    /// </summary>
    public bool DoubleClick(PointF point)
    {
        PointerMove(point);

        if (HitAt(point) is not { Kind: HitKind.Photo } target || _detail is null || target.Index >= _detail.Photos.Count)
            return false;

        _emit(new DesktopCommand.OpenPhoto(_detail.Photos[target.Index].Path));
        return true;
    }

    /// <summary>打ち込んだグループ名を確定する（ウィンドウの欄から）。空なら名前を外す。</summary>
    public void CommitGroupName(string groupId, string text)
        => _emit(new DesktopCommand.SetGroupName(groupId, string.IsNullOrWhiteSpace(text) ? null : text.Trim()));

    public bool IsClickable(PointF point) => HitAt(point) is not null;

    private bool IsEnabled(Target target) => target.Kind switch
    {
        HitKind.Return => _detail?.CanOpen(_returnAction) ?? false,
        HitKind.OpenPhotos => _detail?.LatestPhoto is not null,
        _ => true,
    };

    private Target? HitAt(PointF point)
    {
        foreach (var target in _targets)
        {
            if (target.Rect.Contains(point) && IsEnabled(target))
                return target;
        }

        return null;
    }

    // ------------------------------------------------------------------ 描画

    public void Render(Graphics graphics)
    {
        foreach (var card in _cards)
            DrawCard(graphics, card);

        if (_row is null || _detail is null)
        {
            DrawEmpty(graphics);
            Dirty = false;
            return;
        }

        foreach (var target in _targets)
            DrawTarget(graphics, target);

        DrawMarkNote(graphics);
        DrawGroup(graphics);
        DrawNoPhotos(graphics);
        DrawPeople(graphics);

        Dirty = false;
    }

    private void DrawTarget(Graphics graphics, Target target)
    {
        switch (target.Kind)
        {
            case HitKind.Return:
                Painter.DrawButton(graphics, target.Rect, ReturnButtonLabel(), _pointed == target, IsEnabled(target));
                break;

            case HitKind.Mark:
                DrawMarkButton(graphics, target);
                break;

            case HitKind.Photo:
                DrawThumbnail(graphics, target, _detail!.Photos[target.Index].Path);
                break;

            case HitKind.OpenPhotos when target.Index < 0:
                DrawFolderLink(graphics, target);
                break;

            case HitKind.OpenPhotos:
                DrawMorePhotos(graphics, target);
                break;
        }
    }

    private void DrawCard(Graphics graphics, Card card)
    {
        var rect = card.Rect;

        // 見出しのない枠（行を選んでいないときの案内）は、帯も線も描かない。
        Painter.DrawCard(graphics, rect, card.BandHeight);

        if (card.Title.Length == 0)
            return;

        if (card.IsVisit)
        {
            DrawVisitTitle(graphics, rect);
            return;
        }

        var titleFont = Painter.Fonts.Title;
        Painter.DrawFadingText(graphics, card.Title, titleFont, _style.Text, rect.X + S(SectionPadding), rect.Y + ((S(SectionTitleHeight) - titleFont.GetHeight(graphics)) / 2f), rect.Width - S(SectionPadding * 2f));
    }

    private void DrawEmpty(Graphics graphics)
    {
        var card = _cards[0].Rect;
        var font = Painter.Fonts.Aux;
        var y = card.Y + ((card.Height - font.GetHeight(graphics)) / 2f);

        graphics.DrawString(EmptyText, font, Painter.Brush(_style.Muted), card.X + S(SectionPadding), y, Painter.Format);
    }

    /// <summary>
    /// 先頭の枠の見出し（2026-09-27のユーザー指定→実装メモ5.68）。
    /// インスタンス番号（等幅）の後ろに、滞在中なら「滞在中」、続けてワールド名を日本語のフォントで並べる
    /// （インスタンスタイプは2026-09-30のユーザー指定で外した→5.102）。
    /// 「（いまここにいます）」を番号と同じ等幅のフォントで描いていたため、そこだけ別の字（代わりのフォント）になっていた。
    /// 入り切らないときは、ワールド名の末尾を透明へフェードさせて収める（→5.22）。
    /// 番号と日本語の字はベースラインをそろえる（大きさの違うフォントを上端でそろえると字面がずれる→5.37）。
    /// </summary>
    private void DrawVisitTitle(Graphics graphics, RectangleF rect)
    {
        var row = _row!;
        // 見出しの帯を1.3倍にしたので、字も同じだけ大きくする（→実装メモ5.69）。
        var mono = Painter.VisitTitleMono;
        var jp = Painter.VisitTitleJapanese;
        var gap = S(12f);
        var left = rect.X + S(SectionPadding);
        var right = rect.Right - S(SectionPadding);

        var monoY = rect.Y + ((S(VisitTitleHeight) - mono.GetHeight(graphics)) / 2f);
        // 数字は字面が帯の中央より1pxほど上に寄っていたので、行ごと少し下げる（→実装メモ5.74）。
        // 日本語はベースラインをそろえると、仮名・漢字が数字の上端より上へはみ出して上寄りに見えるので、さらにわずかに下げる。
        monoY += S(1f);
        var jpY = monoY + UiPainter.Ascent(mono) - UiPainter.Ascent(jp) + S(1f);

        graphics.DrawString(row.InstanceId, mono, Painter.Brush(row.IsCurrent ? _style.Accent : _style.Text), left, monoY, Painter.Format);

        var x = left + Painter.MeasureWidth(row.InstanceId, mono) + gap;

        if (row.IsCurrent)
        {
            graphics.DrawString(HereLabel, jp, Painter.Brush(_style.Accent), x, jpY, Painter.Format);
            x += Painter.MeasureWidth(HereLabel, jp) + gap;
        }

        // インスタンスタイプは出さない（パネルの行の3段目で見える→実装メモ5.102）。
        Painter.DrawFadingText(graphics, row.WorldName, jp, _style.Text, x, jpY, MathF.Max(0f, right - x));
    }

    /// <summary>先頭の枠の末尾の目印の案内（「ブックマークアイコン」の添え字はやめ、ここで説明する→実装メモ5.69）。</summary>
    private void DrawMarkNote(Graphics graphics)
    {
        var x = _visitCard.X + S(SectionPadding);
        var muted = Painter.Brush(_style.Muted);

        for (var i = 0; i < _markNote.Count; i++)
            graphics.DrawString(_markNote[i], Painter.Fonts.Absence, muted, x, _markNoteTop + (i * S(NoteLineHeight)), Painter.Format);
    }

    /// <summary>
    /// 行のボタンの文字（→5.53）。押せないときは理由を出す（「ここへ戻る」は滞在中のインスタンスでは押せない）。
    /// </summary>
    private string ReturnButtonLabel()
    {
        if (_detail!.CanOpen(_returnAction))
            return ReturnActions.ButtonLabel(_returnAction);

        return _returnAction == ReturnAction.VrChat && _detail.HereNow ? "いまこのインスタンスにいます" : "この行の場所は開けません";
    }

    /// <summary>目印の1つ。付いている印はその色の枠、指している印はアクセント色の枠（パネルのポップアップと同じ）。</summary>
    private void DrawMarkButton(Graphics graphics, Target target)
    {
        var mark = InstanceMarks.Choices[target.Index];
        var selected = _row!.Mark == mark;
        var pointed = _pointed == target;
        var color = _style.MarkColor(mark);

        if (selected)
            graphics.FillRectangle(Painter.Brush(Color.FromArgb(38, color)), target.Rect);
        else if (pointed)
            graphics.FillRectangle(Painter.Brush(_style.CurrentRow), target.Rect);

        Painter.DrawBorder(graphics, target.Rect, pointed ? _style.Accent : selected ? color : _style.Rule);

        var inset = target.Rect.Width * 0.2f;
        MarkPainter.Draw(graphics, mark, RectangleF.Inflate(target.Rect, -inset, -inset), selected || pointed ? color : Color.FromArgb(170, color));
    }

    private void DrawGroup(Graphics graphics)
    {
        if (_detail!.GroupId is not { } groupId || _groupField is not { } field || _groupLink is not { } link)
            return;

        var fonts = Painter.Fonts;
        var x = _groupCard.X + S(SectionPadding);
        var width = _groupCard.Width - S(SectionPadding * 2f);
        var pointed = _pointed == field;

        // 1段目の文字は欄の縦中央にそろえる。
        var y = field.Rect.Y + ((field.Rect.Height - fonts.Aux.GetHeight(graphics)) / 2f);
        graphics.DrawString(GroupNameLabel, fonts.Aux, Painter.Brush(_style.Text), x, y, Painter.Format);

        // 押せば打ち込めることが分かるよう、欄に薄い枠を付け、指している間はアクセント色の枠と塗りにする。
        if (pointed)
            graphics.FillRectangle(Painter.Brush(_style.CurrentRow), field.Rect);

        Painter.DrawBorder(graphics, field.Rect, pointed ? _style.Accent : _style.Rule);

        var name = _detail.GroupName ?? "未設定";
        var nameLeft = field.Rect.X + S(6f);
        Painter.DrawFadingText(graphics, name, fonts.Aux, _detail.GroupName is null ? _style.Muted : _style.Text, nameLeft, y, field.Rect.Right - S(6f) - nameLeft);

        // Group ID はふだん薄い色の字だけ、指すと字だけアクセント色にする（背景は変えない→実装メモ5.70・5.71）。
        var textY = link.Rect.Y + ((link.Rect.Height - fonts.Absence.GetHeight(graphics)) / 2f);
        Painter.DrawFadingText(graphics, groupId, fonts.Absence, _pointed == link ? _style.Accent : _style.Muted, x, textY, width);
    }

    /// <summary>写真がないときの写真の欄（カメラの印と一言）。写真があるときのサムネイルは <see cref="DrawThumbnail"/>。</summary>
    private void DrawNoPhotos(Graphics graphics)
    {
        if (_detail!.Photos.Count > 0)
            return;

        var x = _photosCard.X + S(SectionPadding);
        var y = _photosCard.Y + S(SectionTitleHeight) + S(8f);
        var icon = S(16f);
        IconPainter.DrawCamera(graphics, new RectangleF(x, y + ((S(LineHeight) - icon) / 2f) - S(1f), icon, icon), _style.Rule);

        graphics.DrawString("このセッションでは撮影していません", Painter.Fonts.Aux, Painter.Brush(_style.Muted), x + icon + S(8f), y, Painter.Format);
    }

    /// <summary>
    /// 写真のサムネイル（→5.55）。縦横比を保って枠の中へ収め、指している写真は枠の色を上げる。
    /// サムネイルがまだできていない写真は、カメラの印だけの枠で出す。
    /// </summary>
    private void DrawThumbnail(Graphics graphics, Target target, string path)
    {
        var rect = target.Rect;
        var pointed = _pointed == target;

        graphics.FillRectangle(Painter.Brush(_style.Header), rect);

        if (Thumbnails?.Invoke(path) is { } image)
        {
            var scale = MathF.Min(rect.Width / image.Width, rect.Height / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var state = graphics.Save();
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(image, rect.X + ((rect.Width - width) / 2f), rect.Y + ((rect.Height - height) / 2f), width, height);
            graphics.Restore(state);
        }
        else
        {
            // まだできていない（作っている途中・写真が見つからない）。
            var icon = MathF.Min(rect.Height * 0.4f, S(22f));
            IconPainter.DrawCamera(graphics, new RectangleF(rect.X + ((rect.Width - icon) / 2f), rect.Y + ((rect.Height - icon) / 2f), icon, icon), _style.Rule);
        }

        // 指している間の変化は Group ID のリンクと同じくらい控えめにする（2026-09-27のユーザー指定→実装メモ5.74）。
        // 枠を二重のアクセント色にしていたのをやめ、1本の枠の色を区切り線の色から薄い文字の色へ上げるだけにする。
        Painter.DrawBorder(graphics, rect, pointed ? _style.Muted : _style.Rule);
    }

    /// <summary>入り切らない写真の枠（「ほか n枚」）。押すとフォルダーを開く。</summary>
    private void DrawMorePhotos(Graphics graphics, Target target)
    {
        var pointed = _pointed == target;

        graphics.FillRectangle(Painter.Brush(pointed ? _style.CurrentRow : _style.Header), target.Rect);
        Painter.DrawBorder(graphics, target.Rect, pointed ? _style.Accent : _style.Rule);

        var font = Painter.Fonts.Aux;
        var text = $"ほか {target.Index}枚";
        var width = Painter.MeasureWidth(text, font);
        graphics.DrawString(text, font, Painter.Brush(pointed ? _style.Accent : _style.Muted), target.Rect.X + ((target.Rect.Width - width) / 2f), target.Rect.Y + ((target.Rect.Height - font.GetHeight(graphics)) / 2f), Painter.Format);
    }

    /// <summary>写真の欄の見出しの右の「フォルダーを開く」（文字だけの控えめなボタン）。</summary>
    private void DrawFolderLink(Graphics graphics, Target target)
    {
        var pointed = _pointed == target;
        var font = Painter.Fonts.Absence;
        var width = Painter.MeasureWidth(FolderLabel, font);

        if (pointed)
            graphics.FillRectangle(Painter.Brush(_style.CurrentRow), target.Rect);

        graphics.DrawString(FolderLabel, font, Painter.Brush(pointed ? _style.Accent : _style.Muted), target.Rect.X + ((target.Rect.Width - width) / 2f), target.Rect.Y + ((target.Rect.Height - font.GetHeight(graphics)) / 2f), Painter.Format);
    }

    /// <summary>
    /// 一緒にいた人を2列で並べる。こちらより先に出た人は控えめな色にする。
    /// 入り切らない分は「ほか n人」とまとめ、右寄せにする。
    /// </summary>
    private void DrawPeople(Graphics graphics)
    {
        var fonts = Painter.Fonts;
        var x = _peopleCard.X + S(SectionPadding);
        var top = _peopleCard.Y + S(SectionTitleHeight) + S(8f);
        var people = _detail!.Companions;
        var muted = Painter.Brush(_style.Muted);

        if (people.Count == 0)
        {
            graphics.DrawString("該当なし", fonts.Absence, muted, x, top, Painter.Format);
            return;
        }

        var (_, rows, shown, column) = _peopleLayout;

        // 名前はリンク。指している間だけ字をアクセント色にする（Group ID と同じ控えめな変化→実装メモ5.75）。
        for (var i = 0; i < shown; i++)
        {
            var person = people[i];
            var cx = x + ((i / rows) * column);
            var cy = top + ((i % rows) * S(PeopleLineHeight));
            var pointed = _pointed is { Kind: HitKind.Person } p && p.Index == i;
            var color = pointed ? _style.Accent : person.LeftAtUtc is null ? _style.Text : _style.Muted;

            Painter.DrawFadingText(graphics, person.Name, fonts.Aux, color, cx, cy, column - S(8f));
        }

        // 「ほか n人」はいつも右の列の最後の枠に入る。枠の右端へ寄せる（2026-09-28のユーザー指定→実装メモ5.86）。
        if (shown < people.Count)
        {
            var i = shown;
            var more = $"ほか {people.Count - shown}人";
            var right = x + (((i / rows) + 1) * column);
            graphics.DrawString(more, fonts.Aux, muted, right - Painter.MeasureWidth(more, fonts.Aux), top + ((i % rows) * S(PeopleLineHeight)), Painter.Format);
        }
    }

    // ------------------------------------------------------------------ 自動検証から見るもの

    /// <summary>部品の矩形。出していなければ null。</summary>
    public RectangleF? TargetRect(HitKind kind, int index = 0)
        => _targets.FirstOrDefault(t => t.Kind == kind && t.Index == index) is { Rect.Width: > 0 } t ? t.Rect : null;
}
