using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Modes;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>仕様8.1・8.2節の表示規則と受入項目 T04・T21・T22・T23・T30。</summary>
public class PresentationTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc);

    private static readonly LogTimeConverter Time = new(EngineHarness.Tokyo);

    private static VisitRecord Record(
        string id,
        AccessType type = AccessType.GroupPublic,
        string? group = Loc.GroupA,
        string? worldName = "ワールド",
        int? ordinal = 1,
        int minutesAgo = 0,
        int? leftMinutesAgo = null,
        int? people = null)
        => new()
        {
            EventId = $"s1@{id}{minutesAgo}",
            SourceSessionId = "s1",
            SuccessByteOffset = minutesAgo,
            SessionOrder = 0,
            LocationKey = $"wrld_x:{id}",
            WorldId = "wrld_x",
            InstanceId = id,
            AccessType = type,
            WorldName = worldName,
            GroupId = type is AccessType.GroupPublic or AccessType.GroupOnly ? group : null,
            VisitedAtUtc = Now - TimeSpan.FromMinutes(minutesAgo),
            LeftAtUtc = leftMinutesAgo is { } left ? Now - TimeSpan.FromMinutes(left) : null,
            VisitOrdinal = ordinal,
            PeopleCount = people,
        };

    [Fact]
    public void 行の内容は時刻と回数とワールド名と種類になる()
    {
        var row = RowFormatter.Build(Record("07254", minutesAgo: 44), Time, isCurrent: true, Now);

        Assert.Equal("07254", row.InstanceId);

        // 02:00 UTC - 44分 = 01:16 UTC = 10:16 JST。滞在中なので退出側は空ける。
        Assert.Equal("10:16 -", row.TimeText);
        Assert.Equal("1回目", row.OrdinalText);
        Assert.Equal("ワールド", row.WorldName);
        Assert.Equal("Group Public · grp_00000001-0000-4000-a000-000000000000", row.TypeText);
        Assert.True(row.IsCurrent);
    }

    /// <summary>時刻は「入室 - 退出」で出す（2026-09-20のユーザー指定）。</summary>
    [Fact]
    public void 退出済みの行は入室と退出の両方を表示する()
    {
        var row = RowFormatter.Build(Record("07254", minutesAgo: 44, leftMinutesAgo: 16), Time, isCurrent: false, Now);

        Assert.Equal("10:16 - 10:44", row.TimeText);
        Assert.Equal(RowFormatter.TimeSample.Length, row.TimeText.Length);
    }

    /// <summary>退出時の人数は回数の後ろに出す（2026-09-20のユーザー指定）。</summary>
    [Fact]
    public void 退出時の人数を人付きで表示する()
    {
        var row = RowFormatter.Build(Record("07254", leftMinutesAgo: 16, people: 12), Time, isCurrent: false, Now);

        Assert.Equal("12人", row.PeopleText);
    }

    /// <summary>まだ分からない人数は空欄にして、回数だけの行と同じ見た目にする。</summary>
    [Fact]
    public void 人数が不明なら何も表示しない()
    {
        var row = RowFormatter.Build(Record("07254"), Time, isCurrent: true, Now);

        Assert.Equal(string.Empty, row.PeopleText);
    }

    [Fact]
    public void 回数が不明なら回数不明と表示する()
    {
        var row = RowFormatter.Build(Record("07254", ordinal: null), Time, isCurrent: false, Now);

        Assert.Equal("回数不明", row.OrdinalText);
    }

    /// <summary>
    /// 1段目は `00:35 - 00:36 (9分前) 1回目 24人` の並び（2026-09-25のユーザー指定）。
    /// 「9分前」はそのインスタンスの退出時刻と現在時刻の差で、回数からは括弧を外した。
    /// </summary>
    [Fact]
    public void 退出済みの行は時刻の後ろに退出からの分数を出す()
    {
        var row = RowFormatter.Build(Record("07254", minutesAgo: 44, leftMinutesAgo: 9, people: 24), Time, isCurrent: false, Now);

        Assert.Equal("10:16 - 10:51", row.TimeText);
        Assert.Equal("(9分前)", row.AgoText);
        Assert.Equal("1回目", row.OrdinalText);
        Assert.Equal("24人", row.PeopleText);
    }

    /// <summary>まだ滞在している行は退出していないので、分数を出さない。</summary>
    [Fact]
    public void 滞在中の行には退出からの分数を出さない()
    {
        var row = RowFormatter.Build(Record("07254", minutesAgo: 44), Time, isCurrent: true, Now);

        Assert.Equal(string.Empty, row.AgoText);
    }

    /// <summary>
    /// 秒を切り捨てた「時:分」どうしの差にする。行に出ている退出時刻（HH:mm）と
    /// 手元の時計を見比べた値と一致し、どの行も毎分0秒にそろって1つ進む。
    /// </summary>
    [Fact]
    public void 退出からの分数は時分どうしの差で毎分0秒に進む()
    {
        var left = new DateTime(2026, 9, 25, 0, 36, 50, DateTimeKind.Utc);

        Assert.Equal(0, RowFormatter.MinutesAgo(left, left.AddSeconds(5)));
        Assert.Equal(9, RowFormatter.MinutesAgo(left, new DateTime(2026, 9, 25, 0, 45, 0, DateTimeKind.Utc)));
        Assert.Equal(9, RowFormatter.MinutesAgo(left, new DateTime(2026, 9, 25, 0, 45, 59, DateTimeKind.Utc)));
        Assert.Equal(10, RowFormatter.MinutesAgo(left, new DateTime(2026, 9, 25, 0, 46, 0, DateTimeKind.Utc)));

        // 時計のずれで退出が未来に見えても負にしない。
        Assert.Equal(0, RowFormatter.MinutesAgo(left, left.AddMinutes(-3)));
    }

    /// <summary>対象を渡り歩く間は古い行も残るので、60分を超える値も分のまま出す。</summary>
    [Fact]
    public void 退出からの分数は60分を超えても分で出す()
    {
        Assert.Equal("(0分前)", RowFormatter.FormatAgo(0));
        Assert.Equal("(59分前)", RowFormatter.FormatAgo(59));
        Assert.Equal("(76分前)", RowFormatter.FormatAgo(76));
        Assert.Equal("(135分前)", RowFormatter.FormatAgo(135));
    }

    /// <summary>見出しの「(全8件 00:14~)」に使う、行ごとの入室時刻。</summary>
    [Fact]
    public void 行は入室時刻をHHmmで持つ()
    {
        var row = RowFormatter.Build(Record("07254", minutesAgo: 44, leftMinutesAgo: 16), Time, isCurrent: false, Now);

        Assert.Equal("10:16", row.JoinText);
    }

    [Fact]
    public void ワールド名がなければワールド名不明と表示する()
    {
        var row = RowFormatter.Build(Record("07254", worldName: null), Time, isCurrent: false, Now);

        Assert.Equal("ワールド名不明", row.WorldName);
    }

    [Fact]
    public void T23_PublicにはGroupIDを表示しない()
    {
        var row = RowFormatter.Build(Record("24688", AccessType.Public, group: null), Time, isCurrent: false, Now);

        Assert.Equal("Public", row.TypeText);
    }

    [Fact]
    public void T23_GroupOnlyも種類と短縮GroupIDを表示する()
    {
        var row = RowFormatter.Build(Record("86688", AccessType.GroupOnly), Time, isCurrent: false, Now);

        Assert.Equal("Group · grp_00000001-0000-4000-a000-000000000000", row.TypeText);
    }

    [Fact]
    public void 現在の入室行だけに印が付く()
    {
        var records = new List<VisitRecord>
        {
            Record("07254", minutesAgo: 10),
            Record("07254", minutesAgo: 0),
        };

        var rows = RowFormatter.Build(records, records[1].EventId, Time, Now);

        Assert.False(rows[0].IsCurrent);
        Assert.True(rows[1].IsCurrent);
    }

    [Fact]
    public void T21_7行が完全に見えて8行目が半分見える()
    {
        var style = new PanelStyle();

        Assert.True(style.MaxViewportHeight >= style.RowHeight * style.FullyVisibleRows);
        Assert.Equal(7, style.FullyVisibleRows);
        Assert.Equal(7.5f, style.VisibleRows);

        // 表示領域は7.5行ぶん。8行目が半分だけ見えるので、続きがあることが分かる。
        Assert.Equal(style.RowHeight * 7, style.MaxViewportHeight - style.RowHeight / 2);
        Assert.Equal(style.MaxHeight, style.HeaderHeight + style.MaxViewportHeight + style.FooterHeight);
    }

    [Fact]
    public void T04_5桁と10文字のIDは1行に収まり縮小も折り返しもしない()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = new List<DisplayRow>
        {
            RowFormatter.Build(Record("07254"), Time, false, Now),
            RowFormatter.Build(Record("c1e88b6419"), Time, false, Now),
            RowFormatter.Build(Record("00001"), Time, false, Now),
            RowFormatter.Build(Record("A0b9"), Time, false, Now),
        };

        var layouts = renderer.Measure(rows);

        Assert.All(layouts, layout =>
        {
            Assert.Single(layout.IdLines);
            Assert.Equal(layout.Row.InstanceId, layout.IdLines[0]);
            Assert.Equal(style.RowHeight, layout.Height);
        });
    }

    [Fact]
    public void T21_長いIDは省略せず折り返して行を高くする()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var longId = new string('W', 40);
        var layout = renderer.Measure([RowFormatter.Build(Record(longId), Time, false, Now)])[0];

        Assert.Equal(longId, string.Concat(layout.IdLines));
        Assert.True(layout.Height > style.RowHeight);
    }

    /// <summary>
    /// 長いワールド名は末尾を省略記号で切らず、3段目のGroup IDと同じく
    /// 右端へ向けて透明にする（2026-09-20のユーザー指定）。
    /// </summary>
    [Fact]
    public void T21_長いワールド名は末尾を切らずに右端で透明へフェードする()
    {
        // 文字の画素を「背景より濃い」で見分けるので、背景の不透明度は既定（0.9）によらず0.85に固定する。
        var style = new PanelStyle { BackgroundOpacity = 0.85f };
        using var renderer = new PanelRenderer(style);

        var longName = string.Concat(Enumerable.Repeat("とても長いワールド名", 10));
        var layouts = renderer.Measure([RowFormatter.Build(Record("07254", worldName: longName), Time, false, Now)]);

        renderer.Render(layouts, 0f);
        var pixels = renderer.GetPixels();

        // 右列2段目（ワールド名）の帯だけを見る。文字の画素は背景より不透明になる。
        var block = style.AuxLineHeight * 3f;
        var lineTop = (int)(style.ViewportTop + (layouts[0].Height - block) / 2f + style.AuxLineHeight);
        var lineBottom = (int)(lineTop + style.AuxLineHeight);

        float Ink(int left, int width)
        {
            var max = 0f;
            for (var y = lineTop; y < lineBottom; y++)
                for (var x = left; x < left + width; x++)
                {
                    var alpha = pixels[((y * style.Width) + x) * 4 + 3];
                    max = Math.Max(max, alpha - style.BackgroundAlpha);
                }

            return max;
        }

        var right = style.AuxColumnLeft + style.AuxColumnWidth;

        // フェードの手前（56pxより左）は不透明のまま。省略していればここで文字が終わっている。
        var opaque = Ink(right - 120, 24);
        var fading = Ink(right - 32, 16);
        var edge = Ink(right - 6, 6);

        Assert.True(opaque > 30f, $"フェード前は不透明のはずが {opaque}");
        Assert.True(fading < opaque && fading > edge, $"途中が段階的でない（{opaque} → {fading} → {edge}）");
        Assert.True(edge < 8f, $"右端で透明になりきっていない（{edge}）");

        // 列の外へははみ出さない。
        Assert.Equal(0f, Ink(right, 8));
    }

    /// <summary>
    /// 1段目の「N分前」・回数・人数は、一覧の全行で左端をそろえる（2026-09-25→実装メモ5.36）。
    /// 欄の幅はいちばん長い値に合わせるので、3桁の「N分前」があっても後ろの欄へ食い込まず、
    /// 滞在中の行（「N分前」がない）でも回数の位置は変わらない。
    /// </summary>
    [Fact]
    public void 一段目の欄は行ごとに左端がそろう()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        var rows = new List<DisplayRow>
        {
            RowFormatter.Build(Record("07254", ordinal: 12, minutesAgo: 140, leftMinutesAgo: 135, people: 40), Time, false, Now),
            RowFormatter.Build(Record("29719", ordinal: 3, minutesAgo: 20, leftMinutesAgo: 9, people: 3), Time, false, Now),
            RowFormatter.Build(Record("86688", ordinal: 1, minutesAgo: 5), Time, true, Now),
        };

        Assert.Equal(["(135分前)", "(9分前)", ""], rows.Select(r => r.AgoText));

        var layouts = renderer.Measure(rows);
        var columns = renderer.FirstLineColumnsFor(layouts);

        // 左から 時刻 → N分前 → 回数 → 人数。
        Assert.Equal(style.AuxColumnLeft, columns.Time);
        Assert.True(columns.Time < columns.Ago && columns.Ago < columns.Ordinal && columns.Ordinal < columns.People);

        renderer.Render(layouts, 0f);
        var pixels = renderer.GetPixels();

        // 行 index の1段目で、[left, right) に文字の画素がある最も左・右のx。なければ null。
        (int Left, int Right)? Ink(int index, float left, float right)
        {
            var top = (int)(style.ViewportTop + (index * style.RowHeight) + ((style.RowHeight - (style.AuxLineHeight * 3f)) / 2f));
            int? first = null;
            var last = -1;

            for (var x = (int)left; x < (int)right; x++)
            {
                for (var y = top; y < top + style.AuxLineHeight; y++)
                {
                    if (pixels[(((y * style.Width) + x) * 4) + 3] <= style.BackgroundAlpha)
                        continue;

                    first ??= x;
                    last = x;
                    break;
                }
            }

            return first is { } f ? (f, last) : null;
        }

        // 「N分前」は退出した2行にだけ出て、3桁でも回数の欄の手前で終わる。
        Assert.NotNull(Ink(0, columns.Ago, columns.Ordinal));
        Assert.NotNull(Ink(1, columns.Ago, columns.Ordinal));
        Assert.Null(Ink(2, columns.Ago, columns.Ordinal));
        Assert.True(Ink(0, columns.Ago, columns.Ordinal)!.Value.Right < columns.Ordinal - 6f);

        // 回数の書き出しは3行とも同じ位置（字形の左の余白の違いだけ許す）。
        var starts = Enumerable.Range(0, 3).Select(i => Ink(i, columns.Ordinal - 2f, columns.People)!.Value.Left).ToList();
        Assert.True(starts.Max() - starts.Min() <= 2, $"回数の左端がそろっていない: {string.Join(", ", starts)}");
        Assert.True(starts.Min() >= columns.Ordinal - 1f);
    }

    /// <summary>
    /// 等幅の時刻と日本語の「(45分前)」は、上端ではなくベースラインでそろえる（2026-09-26のユーザー指定→5.37）。
    /// 数字の字面の下端はベースラインに乗るので、時刻の数字と「N分前」の数字で下端が一致する。
    /// </summary>
    [Fact]
    public void 一段目の時刻とN分前はベースラインがそろう()
    {
        // 文字の画素を「背景より濃い」で見分けるので、背景の不透明度は既定（0.9）によらず0.85に固定する。
        var style = new PanelStyle { BackgroundOpacity = 0.85f };
        using var renderer = new PanelRenderer(style);

        var layouts = renderer.Measure([RowFormatter.Build(Record("07254", minutesAgo: 20, leftMinutesAgo: 45), Time, false, Now)]);
        var columns = renderer.FirstLineColumnsFor(layouts);
        renderer.Render(layouts, 0f);
        var pixels = renderer.GetPixels();

        var top = (int)(style.ViewportTop + ((style.RowHeight - (style.AuxLineHeight * 3f)) / 2f));

        // [left, right) の文字の最も下の行。括弧は下へはみ出すので、数字だけの範囲を渡す。
        int Bottom(float left, float right)
        {
            var bottom = -1;
            for (var y = top; y < top + style.AuxLineHeight + 6; y++)
                for (var x = (int)left; x < (int)right; x++)
                    if (pixels[(((y * style.Width) + x) * 4) + 3] > style.BackgroundAlpha + 25)
                        bottom = y;

            return bottom;
        }

        // 時刻の先頭2桁と、「(45分前)」の「45」（括弧の直後の約2桁ぶん）。
        var time = Bottom(columns.Time, columns.Time + 24f);
        var ago = Bottom(columns.Ago + 8f, columns.Ago + 28f);

        Assert.True(time > 0 && ago > 0);
        Assert.InRange(ago - time, -1, 1);
    }

    [Fact]
    public void テクスチャはBGRAで取り出せる()
    {
        var style = new PanelStyle();
        using var renderer = new PanelRenderer(style);

        renderer.Render([], 0f);

        var bgra = renderer.GetPixels();
        Assert.Equal(style.Width * style.MaxHeight * 4, bgra.Length);

        // 文字も枠もない、履歴領域の中ほどの画素で比べる。
        var middle = ((style.ViewportTop + style.MaxViewportHeight / 2) * style.Width + style.Width / 2) * 4;
        var b = bgra[middle + 0];
        var g = bgra[middle + 1];
        var r = bgra[middle + 2];
        var a = bgra[middle + 3];

        // 背景の不透明度は背景画素にだけ適用し、重ね塗りで濃くしない。
        Assert.Equal(style.BackgroundAlpha, a);

        // GDI+ の内部表現による丸めを見込んで、色は近似で比べる。
        Assert.InRange(r, style.Surface.R - 2, style.Surface.R + 2);
        Assert.InRange(g, style.Surface.G - 2, style.Surface.G + 2);
        Assert.InRange(b, style.Surface.B - 2, style.Surface.B + 2);
    }

    // ---------------------------------------------------------------- PanelPresenter

    private static EngineSnapshot Snapshot(
        PresenceState presence,
        LogHealth health = LogHealth.Ok,
        bool clientRunning = true,
        int rows = 1,
        long generation = 1,
        bool worldsTabOpen = true,
        DateTime? retentionDeadlineUtc = null)
        => new()
        {
            RetentionDeadlineUtc = retentionDeadlineUtc,
            ClientRunning = clientRunning,
            Presence = presence,
            Health = health,
            History = Enumerable.Range(0, rows).Select(i => Record("id" + i, minutesAgo: i)).ToList(),
            CurrentEventId = null,
            MenuPageOpen = worldsTabOpen,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = generation,
        };

    [Fact]
    public void 見出しのカウントダウンへ残り時間を渡す()
    {
        var panel = new RecordingPanelTarget();
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var presenter = new PanelPresenter(panel, Time, NullDiagnostics.Instance, new ManualClock(now));

        // 対象インスタンスに滞在している間（期限なし）は上限の60分で止める。
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 1));
        Assert.Equal(Core.History.HistoryStore.DefaultRetention, panel.Countdowns[^1]);

        // 退出後は期限までの残り時間。
        presenter.Apply(Snapshot(PresenceState.InExcluded, generation: 2, retentionDeadlineUtc: now.AddMinutes(12)));
        Assert.Equal("12:00", Countdown.Format(panel.Countdowns[^1]));

        // 世代が同じでも毎回渡す（描き直すかどうかは表示側が内容を見て決める）。
        presenter.Apply(Snapshot(PresenceState.InExcluded, generation: 2, retentionDeadlineUtc: now.AddMinutes(12)));
        Assert.Equal(3, panel.Countdowns.Count);
    }

    [Fact]
    public void T22_同じ世代では描画内容を作り直さない()
    {
        var panel = new RecordingPanelTarget();

        // 分が変わると「N分前」のために作り直すので、時計は止めておく。
        var presenter = new PanelPresenter(panel, Time, NullDiagnostics.Instance, new ManualClock(Now));

        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 5));
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 5));
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 6));

        // 世代5で1回、世代6で1回。
        Assert.Equal(2, panel.Updates);
    }

    /// <summary>
    /// 退出済みの行の「N分前」は時間とともに進むので、世代が同じでも分が変わったら作り直す
    /// （2026-09-25のユーザー指定→実装メモ5.36）。同じ分のうちは作り直さない
    /// （行の下絵の描き直しは1回10msほどかかるため）。
    /// </summary>
    [Fact]
    public void 退出からの分数は世代が同じでも分が変わったら作り直す()
    {
        var panel = new RecordingPanelTarget();
        var clock = new ManualClock(Now.AddSeconds(10));
        var presenter = new PanelPresenter(panel, Time, NullDiagnostics.Instance, clock);

        var snapshot = new EngineSnapshot
        {
            ClientRunning = true,
            Presence = PresenceState.InExcluded,
            Health = LogHealth.Ok,
            History = [Record("07254", minutesAgo: 20, leftMinutesAgo: 9)],
            MenuPageOpen = true,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = 1,
        };

        presenter.Apply(snapshot);
        Assert.Equal("(9分前)", panel.LastRows[0].AgoText);

        // 同じ分のうちは作り直さない。
        clock.Advance(TimeSpan.FromSeconds(45));
        presenter.Apply(snapshot);
        Assert.Equal(1, panel.Updates);

        // 分が変われば、世代が同じでも作り直して1つ進める。
        clock.Advance(TimeSpan.FromSeconds(5));
        presenter.Apply(snapshot);
        Assert.Equal(2, panel.Updates);
        Assert.Equal("(10分前)", panel.LastRows[0].AgoText);
    }

    [Fact]
    public void T30_開いている間は位置を保ち閉じて開き直すと末尾へ戻す()
    {
        var panel = new RecordingPanelTarget();
        var presenter = new PanelPresenter(panel, Time, NullDiagnostics.Instance);

        // 初回表示は末尾から。
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 1));
        Assert.Equal(1, panel.TailResets);

        // タブを開いたままインスタンスを移動しても位置は保つ。
        presenter.Apply(Snapshot(PresenceState.Transitioning, generation: 2));
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 3));
        Assert.Equal(1, panel.TailResets);

        // 対象外インスタンスへ入っても、タブが開いたままなら位置を保つ。
        presenter.Apply(Snapshot(PresenceState.InExcluded, generation: 4));
        Assert.Equal(1, panel.TailResets);

        // 一時的な読み取りエラーからの復帰でも戻さない。
        presenter.Apply(Snapshot(PresenceState.InTarget, LogHealth.ReadError, generation: 5));
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 6));
        Assert.Equal(1, panel.TailResets);

        // タブを閉じて開き直すと最新（末尾）から。
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 7, worldsTabOpen: false));
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 8));
        Assert.Equal(2, panel.TailResets);

        // VRChatの終了をまたいだ場合も末尾へ。
        presenter.Apply(Snapshot(PresenceState.Unknown, clientRunning: false, generation: 9));
        presenter.Apply(Snapshot(PresenceState.InTarget, generation: 10));
        Assert.Equal(3, panel.TailResets);
    }

    [Fact]
    public void 履歴が空なら表示条件を満たさない()
    {
        var panel = new RecordingPanelTarget();
        var presenter = new PanelPresenter(panel, Time, NullDiagnostics.Instance);

        presenter.Apply(Snapshot(PresenceState.InTarget, rows: 0, generation: 1));

        Assert.False(presenter.ContentReady);
        Assert.Equal(0, panel.TailResets);
    }
}
