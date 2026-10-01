using System.Drawing;
using System.Numerics;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;
using VRCInstanceWristory.Vr;

namespace VRCInstanceWristory.Tests;

/// <summary>
/// マイクアイコンの位置の合わせ（2026-09-30のユーザー指定「予告アイコンがマイクアイコンとずれる」→実装メモ5.92）。
/// </summary>
public class MicOffsetTests
{
    [Fact]
    public void 合わせを入れると8方向がそろって同じだけ動く()
    {
        var offset = new Vector2(3f, -1.5f);

        foreach (var position in ResetWarningPositions.Order)
        {
            var before = ResetWarningOverlay.OffsetFor(position, 1f);
            var after = ResetWarningOverlay.OffsetFor(position, 1f, offset);

            Assert.Equal(0.03f, after.X - before.X, 4);
            Assert.Equal(-0.015f, after.Y - before.Y, 4);
            Assert.Equal(before.Z, after.Z);
        }

        // 合わせなし（0, 0）は、利用者が実機で合わせた値（VRCMicOverlay の既定から 左1.5cm・下5.5cm）を焼き込んだマイクの想定を基準にする（→実装メモ5.93）。
        Assert.Equal(new Vector3(-0.385f, -0.315f, -0.92f), ResetWarningOverlay.MicCenter);
        var left = ResetWarningOverlay.OffsetFor(ResetWarningPosition.Left, 1f);
        Assert.Equal(-0.432f, left.X, 3);
        Assert.Equal(-0.315f, left.Y, 3);
    }

    [Fact]
    public void マイクの図の縦幅は1cm広い6cmと想定する()
    {
        Assert.Equal(0.06f, ResetWarningOverlay.MicGlyphHeightMeters);

        // 上・下は、マイクの中心から 縦幅の半分(3cm)＋間(0.8cm)＋アイコンの図の半分 だけ離す。
        var mic = ResetWarningOverlay.MicCenter;
        var half = ResetWarningOverlay.SizeMeters * ResetWarningOverlay.GlyphRatio / 2f;
        Assert.Equal(0.03f + ResetWarningOverlay.GapMeters + half, ResetWarningOverlay.OffsetFor(ResetWarningPosition.Top, 1f).Y - mic.Y, 4);
        Assert.Equal(-(0.03f + ResetWarningOverlay.GapMeters + half), ResetWarningOverlay.OffsetFor(ResetWarningPosition.Bottom, 1f).Y - mic.Y, 4);
    }

    [Fact]
    public void 合わせの書式は向きとセンチメートル()
    {
        Assert.Equal("0.0 cm", ResetWarningOptions.FormatMicOffset(0f, vertical: false));
        Assert.Equal("右 1.5 cm", ResetWarningOptions.FormatMicOffset(1.5f, vertical: false));
        Assert.Equal("左 0.5 cm", ResetWarningOptions.FormatMicOffset(-0.5f, vertical: false));
        Assert.Equal("上 2.0 cm", ResetWarningOptions.FormatMicOffset(2f, vertical: true));
        Assert.Equal("下 10.0 cm", ResetWarningOptions.FormatMicOffset(-10f, vertical: true));
    }

    private static SettingsView Layout(List<DesktopCommand> commands, DesktopSettings? settings = null)
    {
        var view = new SettingsView(new PanelStyle(), settings ?? DesktopSettings.From(new AppSettings { ResetWarningEnabled = true }), commands.Add);
        view.Layout(new PointF(0f, 0f), 1f, columns: 1);
        return view;
    }

    private static void Click(SettingsView view, SettingsView.HitKind kind, int index)
    {
        var rect = view.TargetRect(kind, index)!.Value;
        view.PointerDown(new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)));
        view.PointerUp();
    }

    [Fact]
    public void マイクアイコン位置は表示位置のすぐ下にあり_0点5cmずつ動く()
    {
        var commands = new List<DesktopCommand>();
        using var view = Layout(commands);
        var first = SettingsView.WarningStepperFirst;

        var position = view.TargetRect(SettingsView.HitKind.StepperPlus, first)!.Value;
        var horizontal = view.TargetRect(SettingsView.HitKind.StepperPlus, first + 5)!.Value;
        var vertical = view.TargetRect(SettingsView.HitKind.StepperPlus, first + 6)!.Value;
        var size = view.TargetRect(SettingsView.HitKind.StepperPlus, first + 1)!.Value;

        Assert.True(position.Y < horizontal.Y && horizontal.Y < vertical.Y && vertical.Y < size.Y);
        Assert.Equal("0.0 cm", view.StepperText(first + 5));

        Click(view, SettingsView.HitKind.StepperPlus, first + 5);
        Click(view, SettingsView.HitKind.StepperMinus, first + 6);

        var changes = commands.Cast<DesktopCommand.ChangeSettings>().ToList();
        Assert.Equal(0.5f, changes[0].Settings.WarningMicOffsetXCm);
        Assert.Equal(-0.5f, changes[1].Settings.WarningMicOffsetYCm);
        Assert.All(changes, c => Assert.Equal(SettingsField.ResetWarning, c.Fields));
        Assert.Equal("右 0.5 cm", view.StepperText(first + 5));
        Assert.Equal("下 0.5 cm", view.StepperText(first + 6));
    }

    [Fact]
    public void 予告アイコンを表示しない間はマイクアイコン位置も動かせない()
    {
        var commands = new List<DesktopCommand>();
        using var view = Layout(commands, DesktopSettings.From(new AppSettings()));

        Click(view, SettingsView.HitKind.StepperPlus, SettingsView.WarningStepperFirst + 5);
        Assert.Empty(commands);
    }

    [Fact]
    public void マイクアイコン位置を保存して読み直せ_範囲外は0へ戻す()
    {
        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        new AppSettings { ResetWarningMicOffsetXCm = 2.5f, ResetWarningMicOffsetYCm = -1f }.SaveFields(path, SettingsField.ResetWarning);

        var log = new CollectingDiagnostics();
        var loaded = AppSettings.Load(path, log);
        loaded.Validate(log);

        Assert.Equal(2.5f, loaded.ResetWarningMicOffsetXCm);
        Assert.Equal(-1f, loaded.ResetWarningMicOffsetYCm);
        Assert.DoesNotContain(log.Messages, m => m.StartsWith("WARN", StringComparison.Ordinal));

        var wrong = new AppSettings { ResetWarningMicOffsetXCm = 50f };
        wrong.Validate(new CollectingDiagnostics());
        Assert.Equal(0f, wrong.ResetWarningMicOffsetXCm);
    }

    [Fact]
    public void 奥行きは1点15mで_見える方向と見かけの大きさは基準のまま()
    {
        Assert.Equal(1.15f, ResetWarningOverlay.DepthMeters);
        Assert.Equal(1.25f, ResetWarningOverlay.DepthScale, 4);

        foreach (var position in ResetWarningPositions.Order)
        {
            var reference = ResetWarningOverlay.OffsetFor(position, 1f, new Vector2(1f, -2f));
            var placed = ResetWarningOverlay.PlacementFor(position, 1f, new Vector2(1f, -2f));

            Assert.Equal(-1.15f, placed.Z, 4);

            // 目（原点）から見た方向が同じ。
            Assert.True(Vector3.Dot(Vector3.Normalize(reference), Vector3.Normalize(placed)) > 0.99999f);
        }

        // 幅も同じ倍率なので、見かけの大きさ（幅÷奥行き）は変わらない。
        Assert.Equal(ResetWarningOverlay.SizeMeters / 0.92f, ResetWarningOverlay.WidthFor(1f) / ResetWarningOverlay.DepthMeters, 4);
        Assert.Equal(ResetWarningOverlay.WidthFor(1f) * 1.4f, ResetWarningOverlay.WidthFor(1.4f), 4);
    }

    [Fact]
    public void 奥行きの合わせは距離だけを変え_見える方向と見かけの大きさは変えない()
    {
        Assert.Equal("0 cm", ResetWarningOptions.FormatMicDepth(0f));
        Assert.Equal("奥 10 cm", ResetWarningOptions.FormatMicDepth(10f));
        Assert.Equal("手前 5 cm", ResetWarningOptions.FormatMicDepth(-5f));

        var near = ResetWarningOverlay.PlacementFor(ResetWarningPosition.BottomLeft, 1f, default, -20f);
        var far = ResetWarningOverlay.PlacementFor(ResetWarningPosition.BottomLeft, 1f, default, 30f);

        Assert.Equal(-0.95f, near.Z, 4);
        Assert.Equal(-1.45f, far.Z, 4);
        Assert.True(Vector3.Dot(Vector3.Normalize(near), Vector3.Normalize(far)) > 0.99999f);
        Assert.Equal(ResetWarningOverlay.WidthFor(1f, -20f) / 0.95f, ResetWarningOverlay.WidthFor(1f, 30f) / 1.45f, 4);
    }

    [Fact]
    public void 奥行きの部品は縦のすぐ下にあり_5cmずつ動き_保存できる()
    {
        var commands = new List<DesktopCommand>();
        using var view = Layout(commands);
        var first = SettingsView.WarningStepperFirst;

        var vertical = view.TargetRect(SettingsView.HitKind.StepperPlus, first + 6)!.Value;
        var depth = view.TargetRect(SettingsView.HitKind.StepperPlus, first + 7)!.Value;
        var size = view.TargetRect(SettingsView.HitKind.StepperPlus, first + 1)!.Value;
        Assert.True(vertical.Y < depth.Y && depth.Y < size.Y);

        Click(view, SettingsView.HitKind.StepperPlus, first + 7);
        var change = Assert.IsType<DesktopCommand.ChangeSettings>(Assert.Single(commands));
        Assert.Equal(5f, change.Settings.WarningMicOffsetZCm);
        Assert.Equal("奥 5 cm", view.StepperText(first + 7));

        using var tempFile = new TempFile("settings");
        var path = tempFile.Path;

        var settings = new AppSettings();
        change.Settings.ApplyTo(settings, change.Fields);
        settings.SaveFields(path, SettingsField.ResetWarning);
        Assert.Equal(5f, AppSettings.Load(path, new CollectingDiagnostics()).ResetWarningMicOffsetZCm);

        var wrong = new AppSettings { ResetWarningMicOffsetZCm = 80f };
        wrong.Validate(new CollectingDiagnostics());
        Assert.Equal(0f, wrong.ResetWarningMicOffsetZCm);
    }
}
