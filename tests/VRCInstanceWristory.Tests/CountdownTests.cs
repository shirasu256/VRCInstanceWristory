using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.History;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// 見出し右のカウントダウンとカウント延長のボタン（2026-09-18・19のユーザー指定。
/// 2026-09-25にボタンの名前を「カウントリセット」から改め、残り時間と左右を入れ替えた）。
///
/// 2026-09-23まではどちらも専用のオーバーレイで、「全フレームを1枚に並べた帯」から
/// 切り出す作りだった。これは <c>SetOverlayRaw</c> のちらつきを避けるためのもので、
/// いまはパネルへ直接描いている（→実装メモ5.35）。
/// </summary>
public class CountdownTests
{
    private static readonly PanelStyle Style = new();

    [Fact]
    public void 残り時間は切り上げでMM_SSにする()
    {
        Assert.Equal("60:00", Countdown.Format(TimeSpan.FromMinutes(60)));
        Assert.Equal("59:59", Countdown.Format(TimeSpan.FromSeconds(59 * 60 + 59)));
        Assert.Equal("01:00", Countdown.Format(TimeSpan.FromSeconds(60)));

        // 端数は切り上げる。0になった瞬間だけ 00:00。
        Assert.Equal("00:02", Countdown.Format(TimeSpan.FromSeconds(1.2)));
        Assert.Equal("00:01", Countdown.Format(TimeSpan.FromSeconds(0.4)));
        Assert.Equal("00:00", Countdown.Format(TimeSpan.Zero));
    }

    /// <summary>
    /// 上限は、設定で選べる保持時間の最大（180分）。以前は60分で丸めていたが、
    /// 保持時間を変えられるようにしたので（2026-09-26→実装メモ5.39）、既定の60分を超えても出せるようにした。
    /// </summary>
    [Fact]
    public void 範囲外の残り時間は上限と0へ丸める()
    {
        // 上限は900分（→実装メモ5.71）。
        Assert.Equal("5:00:00", Countdown.Format(TimeSpan.FromHours(5)));
        Assert.Equal("15:00:00", Countdown.Format(TimeSpan.FromHours(20)));
        Assert.Equal("00:00", Countdown.Format(TimeSpan.FromMinutes(-10)));
        Assert.Equal(HistoryStore.DefaultRetention, Countdown.Max);
    }

    [Fact]
    public void 対象に滞在している間は上限で止める()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        // 期限なし（対象に滞在中）。
        var staying = Snapshot(null);
        Assert.Equal(HistoryStore.DefaultRetention, staying.RetentionRemaining(now));
        Assert.Equal("60:00", Countdown.Format(staying.RetentionRemaining(now)));

        // 退出から10分経過（残り50分）。
        var counting = Snapshot(now.AddMinutes(50));
        Assert.Equal("50:00", Countdown.Format(counting.RetentionRemaining(now)));

        // 期限を過ぎていれば0で止める。
        var expired = Snapshot(now.AddMinutes(-5));
        Assert.Equal(TimeSpan.Zero, expired.RetentionRemaining(now));
    }

    /// <summary>
    /// 見出し右は、右端からボタン・残り時間・「ログリセットまで:」の順に並べる
    /// （2026-09-25のユーザー指定で、それまで右端にあった残り時間とボタンを入れ替えた）。
    /// </summary>
    [Fact]
    public void リセットは見出しの右端に_延長はその左に縦中央で置く()
    {
        using var renderer = new PanelRenderer(Style);

        var clear = renderer.ClearButtonRectFor("60:00");
        var button = renderer.ResetButtonRectFor("60:00");

        // 右端（「リセット」→実装メモ5.65）はスクロールの帯より内側。「延長」はその左で重ならない。
        Assert.Equal(Style.Width - Style.PaddingRight - Style.ScrollBarWidth, clear.Right, 3);
        Assert.True(button.Right < clear.Left);

        // 見出しの中に収まり、上下の中央にある。
        foreach (var rect in new[] { button, clear })
        {
            Assert.True(rect.Top >= 0f);
            Assert.True(rect.Bottom <= Style.HeaderHeight);
            Assert.Equal(Style.HeaderHeight / 2f, rect.Top + (rect.Height / 2f), 3);
        }

        Assert.Equal("延長", PanelRenderer.ResetButtonLabel);
        Assert.Equal("履歴リセット", PanelRenderer.ClearButtonLabel);

        // 「延長」は「履歴リセット」と同じ幅（→実装メモ5.66・5.74）。
        Assert.Equal(clear.Width, button.Width, 1);

        // 残り時間を出さないときはボタンも出さない。
        Assert.True(renderer.ResetButtonRectFor(null).IsEmpty);
        Assert.True(renderer.ClearButtonRectFor(null).IsEmpty);
    }

    /// <summary>
    /// 命中を見るときも、描くときと同じ見出しの状態（<see cref="PanelDecorations"/>）から矩形を決める。
    /// 描き手に前に描いた状態が残っていても、渡した状態だけで決まる。
    /// </summary>
    [Fact]
    public void ボタンの矩形は渡した見出しの状態だけで決まる()
    {
        using var renderer = new PanelRenderer(Style);
        renderer.Compose(0f, new PanelDecorations { Countdown = "60:00", CountdownStopped = true });

        var running = new PanelDecorations { Countdown = "60:00" };
        Assert.False(renderer.ResetButtonRectFor(running).IsEmpty);
        Assert.Equal(renderer.ResetButtonRect(), renderer.ResetButtonRectFor(running));

        Assert.True(renderer.ResetButtonRectFor(running with { CountdownStopped = true }).IsEmpty);
        Assert.True(renderer.ResetButtonRectFor(running with { AutoResetDisabled = true }).IsEmpty);
        Assert.False(renderer.ClearButtonRectFor(running with { AutoResetDisabled = true }).IsEmpty);
        Assert.True(renderer.CountdownLabelRectFor(running with { AutoResetDisabled = true }).IsEmpty);
    }

    [Fact]
    public void 履歴リセットの確認は行のないいちばん低いパネルにも収まる()
    {
        using var renderer = new PanelRenderer(Style);
        renderer.SetViewportForContent(0f);

        var box = PanelGeometry.ConfirmBoxRect(Style, renderer.Height);
        var cancel = PanelGeometry.ConfirmButtonRect(box, PanelGeometry.ConfirmCancel);
        var accept = PanelGeometry.ConfirmButtonRect(box, PanelGeometry.ConfirmAccept);

        Assert.True(box.Top >= 0f && box.Bottom <= renderer.Height);
        Assert.True(box.Left >= 0f && box.Right <= Style.Width);

        foreach (var rect in new[] { cancel, accept })
            Assert.True(box.Contains(rect));

        // 右端が「リセット」、その左が「キャンセル」。
        Assert.True(cancel.Right < accept.Left);
        Assert.Equal(PanelGeometry.ConfirmAccept, PanelGeometry.ConfirmItemAt(box, new PointF(accept.X + 4f, accept.Y + 4f)));
        Assert.Equal(PanelGeometry.ConfirmCancel, PanelGeometry.ConfirmItemAt(box, new PointF(cancel.X + 4f, cancel.Y + 4f)));
        Assert.Equal(-1, PanelGeometry.ConfirmItemAt(box, new PointF(box.X + 4f, box.Y + 4f)));
    }

    [Fact]
    public void 残り時間はボタンの左に置きその前に履歴リセットまでと添える()
    {
        using var renderer = new PanelRenderer(Style);

        var button = renderer.ResetButtonRectFor("60:00");
        var countdown = renderer.CountdownRectFor("60:00");
        var label = renderer.CountdownLabelRectFor("60:00");

        // 右から ボタン → 残り時間 → 「履歴リセットまで:」。重ならない。
        Assert.True(countdown.Right < button.Left);
        Assert.True(label.Right < countdown.Left);

        // どれも見出しの中に収まる。残り時間は上下の中央。
        foreach (var rect in new[] { button, countdown, label })
        {
            Assert.True(rect.Top >= 0f);
            Assert.True(rect.Bottom <= Style.HeaderHeight);
        }

        Assert.Equal(Style.HeaderHeight / 2f, countdown.Top + (countdown.Height / 2f), 3);
        Assert.Equal("履歴リセットまで:", PanelRenderer.CountdownLabel);

        // 等幅フォントなので、どの値でも幅は変わらない（値が変わっても数字も添えた文字も動かない）。
        Assert.Equal(countdown, renderer.CountdownRectFor("00:00"));
        Assert.Equal(countdown, renderer.CountdownRectFor("34:07"));
        Assert.Equal(label, renderer.CountdownLabelRectFor("34:07"));

        Assert.True(renderer.CountdownLabelRectFor(null).IsEmpty);
    }

    /// <summary>
    /// 見出し左の表題（件数と先頭の行の時刻）がいちばん長くなっても、右側の文字とは重ならない。
    /// </summary>
    [Fact]
    public void 見出しの左右の文字は重ならない()
    {
        using var renderer = new PanelRenderer(Style);

        var longest = PanelRenderer.HeaderTitle(999, "23:59");

        Assert.True(
            renderer.HeaderTitleRect(longest).Right < renderer.CountdownLabelRectFor("60:00").Left,
            $"表題（右端 {renderer.HeaderTitleRect(longest).Right}px）が「ログリセットまで:」（左端 {renderer.CountdownLabelRectFor("60:00").Left}px）に届いている");
    }

    /// <summary>
    /// 値が変わればカウントダウンの場所だけが変わる。
    /// 以前は帯の切り出し範囲を動かしていたが、いまはその場所を描き直している。
    /// </summary>
    [Fact]
    public void 残り時間は値が変わったところだけ描き変わる()
    {
        using var renderer = new PanelRenderer(Style);

        var rect = renderer.CountdownRectFor("60:00");

        var full = PanelPixels.Compose(renderer, decorations: new PanelDecorations { Countdown = "60:00" });
        var later = PanelPixels.Compose(renderer, decorations: new PanelDecorations { Countdown = "34:07" });
        var again = PanelPixels.Compose(renderer, decorations: new PanelDecorations { Countdown = "60:00" });

        // 値が変われば絵が変わる。
        Assert.NotEqual(PanelPixels.Crop(full, renderer.Width, rect), PanelPixels.Crop(later, renderer.Width, rect));

        // 同じ値なら何度組み立てても同じ絵（＝渡し直す必要がない）。
        Assert.Equal(full, again);

        // 変わるのはカウントダウンの矩形の中だけ。
        Assert.Equal(
            PanelPixels.Outside(full, renderer.Width, renderer.Height, rect),
            PanelPixels.Outside(later, renderer.Width, renderer.Height, rect));
    }

    [Fact]
    public void ボタンは指している間だけ見た目が変わる()
    {
        using var renderer = new PanelRenderer(Style);

        var rect = renderer.ResetButtonRectFor("60:00");

        var normal = PanelPixels.Compose(renderer, decorations: new PanelDecorations { Countdown = "60:00" });
        var pointed = PanelPixels.Compose(
            renderer,
            decorations: new PanelDecorations { Countdown = "60:00", ResetPointed = true });

        Assert.NotEqual(PanelPixels.Crop(normal, renderer.Width, rect), PanelPixels.Crop(pointed, renderer.Width, rect));

        // 変わるのはボタンの中だけ。
        Assert.Equal(
            PanelPixels.Outside(normal, renderer.Width, renderer.Height, rect),
            PanelPixels.Outside(pointed, renderer.Width, renderer.Height, rect));
    }

    /// <summary>
    /// ボタンとカウントダウンの縦幅を揃える（2026-09-19のユーザー指定）。
    /// ボタンは付加情報よりわずかに小さい文字、カウントダウンはわずかに大きい文字にしたうえで、
    /// 高さを同じにして、見出しの中で2つが同じ高さに見えるようにする。
    /// </summary>
    [Fact]
    public void ボタンとカウントダウンの縦幅は揃う()
    {
        using var renderer = new PanelRenderer(Style);

        Assert.Equal(renderer.CountdownFrameHeight, renderer.ButtonSize().Height, 3);
        Assert.Equal(renderer.CountdownRectFor("60:00").Height, renderer.ResetButtonRectFor("60:00").Height, 3);

        // カウントダウンは付加情報よりわずかに大きく、ボタンはわずかに小さい。
        Assert.True(Style.CountdownFontSize > Style.AuxFontSize);
        Assert.True(Style.ResetButtonFontSize < Style.AuxFontSize);

        // 「わずかに」の範囲（±25%以内）に収める。
        Assert.InRange(Style.CountdownFontSize / Style.AuxFontSize, 1f, 1.25f);
        Assert.InRange(Style.ResetButtonFontSize / Style.AuxFontSize, 0.75f, 1f);

        // 見出しには収まる。
        Assert.True(renderer.ButtonSize().Height < Style.HeaderHeight);
    }

    /// <summary>
    /// 枠線は4辺とも出ていること（2026-09-19の実機確認で左辺・上辺が欠けていた）。
    /// 1px幅のペンは線の中心が辺に乗るため、縁では半分が外へ出て消える。
    /// </summary>
    [Fact]
    public void ボタンの枠線は4辺とも描かれる()
    {
        using var renderer = new PanelRenderer(Style);

        var rect = renderer.ResetButtonRectFor("60:00");
        var pixels = PanelPixels.Compose(renderer, decorations: new PanelDecorations { Countdown = "60:00" });

        var left = (int)MathF.Round(rect.Left);
        var right = (int)MathF.Round(rect.Right) - 1;
        var top = (int)MathF.Round(rect.Top);
        var bottom = (int)MathF.Round(rect.Bottom) - 1;
        var middleX = (int)MathF.Round(rect.Left + (rect.Width / 2f));
        var middleY = (int)MathF.Round(rect.Top + (rect.Height / 2f));

        // 枠線は見出しの帯より明るい色で描かれる。
        var header = PanelPixels.At(pixels, renderer.Width, Style.PaddingLeft / 2, middleY);

        foreach (var (x, y) in new[] { (middleX, top), (middleX, bottom), (left, middleY), (right, middleY) })
        {
            var edge = PanelPixels.At(pixels, renderer.Width, x, y);
            Assert.True(
                edge.R + edge.G + edge.B > header.R + header.G + header.B,
                $"枠線が描かれていません（{x}, {y}）");
        }
    }

    private static EngineSnapshot Snapshot(DateTime? deadlineUtc)
        => new()
        {
            ClientRunning = true,
            Presence = Core.Visits.PresenceState.InTarget,
            Health = LogHealth.Ok,
            History = [],
            MenuPageOpen = true,
            AwaitingViewAngleClose = false,
            BaselineKnown = true,
            CheckpointHealthy = true,
            Generation = 1,
            RetentionDeadlineUtc = deadlineUtc,
        };
}
