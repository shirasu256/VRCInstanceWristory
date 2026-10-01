using System.Drawing;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Core.Visits;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// パネルの縦幅（2026-09-19のユーザー指定）。
/// 7.5行は上限で、行がそれより少ないときはその行のぶんまで詰める。
/// 行が増えるときは下端を固定して上へ伸ばす。
/// </summary>
public class PanelHeightTests
{
    private static readonly PanelStyle Style = new();

    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LogTimeConverter Time = new(EngineHarness.Tokyo);

    /// <summary>標準の高さになる行を指定した数だけ作り、実測する。</summary>
    private static List<RowLayout> Layouts(PanelRenderer renderer, int count)
    {
        var rows = Enumerable
            .Range(0, count)
            .Select(i => RowFormatter.Build(Record($"{i:00000}", i), Time, isCurrent: false, Now))
            .ToList();

        return renderer.Measure(rows);
    }

    private static VisitRecord Record(string instanceId, int minutesAgo) => new()
    {
        EventId = $"s1@{instanceId}",
        SourceSessionId = "s1",
        SuccessByteOffset = minutesAgo,
        SessionOrder = 0,
        LocationKey = $"wrld_x:{instanceId}",
        WorldId = "wrld_x",
        InstanceId = instanceId,
        AccessType = AccessType.Public,
        WorldName = "ワールド",
        VisitedAtUtc = Now - TimeSpan.FromMinutes(minutesAgo),
        VisitOrdinal = 1,
    };

    [Fact]
    public void 行が上限に満たないときは表示領域をその行のぶんまで詰める()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Layouts(renderer, 3);
        var content = layouts.Sum(l => l.Height);

        Assert.True(renderer.SetViewportForContent(content));

        Assert.Equal(3 * Style.RowHeight, renderer.ViewportHeight);
        Assert.Equal(Style.HeaderHeight + (3 * Style.RowHeight) + Style.FooterHeight, renderer.Height);
        Assert.True(renderer.Height < Style.MaxHeight, "3行なら上限より低いはず");
    }

    [Fact]
    public void 行が増えても7点5行ぶんで頭打ちになる()
    {
        using var renderer = new PanelRenderer(Style);

        // 8行で上限（7.5行ぶん）に達し、それ以上増えても高さは変わらない。
        renderer.SetViewportForContent(Layouts(renderer, 8).Sum(l => l.Height));
        Assert.Equal(Style.MaxViewportHeight, renderer.ViewportHeight);

        Assert.False(renderer.SetViewportForContent(Layouts(renderer, 30).Sum(l => l.Height)));
        Assert.Equal(Style.MaxHeight, renderer.Height);

        // ちょうど7行のときは上限より低い（8行目の半分ぶんだけ詰まる）。
        renderer.SetViewportForContent(Layouts(renderer, 7).Sum(l => l.Height));
        Assert.Equal(7 * Style.RowHeight, renderer.ViewportHeight);
        Assert.Equal(Style.MaxViewportHeight - (Style.RowHeight / 2), renderer.ViewportHeight);
    }

    [Fact]
    public void 行がなくてもテクスチャは1行ぶんの高さを残す()
    {
        using var renderer = new PanelRenderer(Style);

        renderer.SetViewportForContent(0f);

        Assert.Equal(Style.RowHeight, renderer.ViewportHeight);
        Assert.True(renderer.Height > 0);
    }

    [Fact]
    public void 渡す画素はいまの高さぶんだけになる()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Layouts(renderer, 3);
        renderer.SetViewportForContent(layouts.Sum(l => l.Height));
        renderer.Render(layouts, 0f);

        var pixels = renderer.GetPixels();
        Assert.Equal(Style.Width * renderer.Height * 4, pixels.Length);

        static byte AlphaAt(byte[] pixels, PanelStyle style, int x, int y)
            => pixels[((y * style.Width) + x) * 4 + 3];

        // 詰めた高さの中でも、見出し・表示領域・下部案内がすべて塗られている
        // （パネルはオーバーレイ1枚なので、透明のまま残す場所はない→実装メモ5.35）。
        Assert.Equal(Style.BackgroundAlpha, AlphaAt(pixels, Style, 480, Style.HeaderHeight / 2));
        Assert.Equal(Style.BackgroundAlpha, AlphaAt(pixels, Style, 480, renderer.Height - (Style.FooterHeight / 2)));
        Assert.Equal(Style.BackgroundAlpha, AlphaAt(pixels, Style, 480, Style.ViewportTop + (renderer.ViewportHeight / 2)));
    }

    [Fact]
    public void 高さが変わっても下端は動かず上へ伸びる()
    {
        const float width = 0.14f;
        var metersPerPixel = width / Style.Width;

        // 上限の高さでは、掴んで決めた位置のままずらさない。
        Assert.Equal(0f, PanelGeometry.BottomAnchorOffsetMeters(Style, width, Style.MaxHeight), 6);

        static float Bottom(PanelStyle style, float width, int height)
            => PanelGeometry.BottomAnchorOffsetMeters(style, width, height) - (height * (width / style.Width) / 2f);

        var full = Bottom(Style, width, Style.MaxHeight);

        foreach (var rows in new[] { 1, 3, 5, 7 })
        {
            var height = Style.HeightFor(rows * Style.RowHeight);

            // 下端は上限のときと同じ位置。
            Assert.Equal(full, Bottom(Style, width, height), 6);

            // 中心は下がり、上端はそのぶん下へ来る（＝行が増えると上へ伸びる）。
            var offset = PanelGeometry.BottomAnchorOffsetMeters(Style, width, height);
            Assert.True(offset < 0f, "縮んだぶん中心は下がるはず");
            Assert.Equal(-(Style.MaxHeight - height) / 2f * metersPerPixel, offset, 6);
        }
    }

    [Fact]
    public void 詰めた表示領域ではスクロールしない()
    {
        using var renderer = new PanelRenderer(Style);

        var layouts = Layouts(renderer, 3);
        var content = layouts.Sum(l => l.Height);
        renderer.SetViewportForContent(content);

        // 行の下絵は詰めた表示領域と同じ高さになり、全体が一度に見える。
        Assert.Equal(renderer.ViewportHeight, renderer.RenderRows(layouts));
        Assert.Equal(renderer.ViewportHeight, PanelGeometry.RowsTextureHeight(content, renderer.ViewportHeight));
    }
}
