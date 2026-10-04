using CielWin.App.Alerts;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// S3: the one global frame-rate cap (<c>frame-rate</c>, 30 or 60) through the whole composition. The
/// persisted rate reaches both surfaces; the tray's Frame rate choice rebuilds the current surface at
/// the new rate (the mode-switch path), persists it at once, and an alert that was showing comes back
/// on the new page for its remaining time without sounding again.
/// </summary>
public sealed class FrameRateWiringTests
{
    private static readonly Settings SceneSettings = new(WallpaperMode: WallpaperMode.Scene, WallpaperScene: WallpaperScene.Idle);
    private static readonly Settings MiniSettings = new(
        WallpaperMode: WallpaperMode.SceneMini, WallpaperScene: WallpaperScene.Raphael, MiniPosition: MiniPosition.BottomLeft);

    [Fact]
    public void TheDefaultRate_IsSixty_OnTheWallpaperLayer()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);

        Assert.Equal(60, harness.Layer.FrameRate);
        Assert.Equal(60, harness.Tray!.Controller.FrameRate);
    }

    [Fact]
    public void ThePersistedRate_ReachesTheWallpaperLayer()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings with { FrameRate = 30 });

        Assert.Equal(30, harness.Layer.FrameRate);
        Assert.Equal(30, harness.Tray!.Controller.FrameRate);
    }

    [Fact]
    public void ThePersistedRate_ReachesTheMiniWindow()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings with { FrameRate = 30 });

        Assert.Equal(30, harness.Mini.FrameRate);
    }

    [Fact]
    public void TrayRateChange_InSceneMode_RebuildsTheWallpaperAtTheNewRate_AndPersistsIt()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);
        var oldLayer = harness.Layer;
        var oldHost = harness.Host;

        harness.Tray!.Controller.SelectFrameRate(30);

        Assert.Equal(1, oldLayer.DisposeCalls);
        Assert.Equal(1, oldHost.DisposeCalls);
        Assert.Equal(2, harness.Layers.Count);
        Assert.Equal(30, harness.Layer.FrameRate);
        Assert.Equal(WallpaperScene.Idle, harness.Layer.InitialScene);
        Assert.Equal(1, harness.Layer.Preloads);
        Assert.Equal(30, harness.Saves[^1].FrameRate);
        Assert.Equal(WallpaperMode.Scene, harness.Saves[^1].WallpaperMode);
        Assert.Equal(30, harness.Tray.Controller.FrameRate);
        Assert.Contains("frame-rate switched fps=30", harness.Trace);
    }

    [Fact]
    public void TrayRateChange_InMiniMode_RebuildsTheMiniAtTheSamePlaceAndScene()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings);
        var oldMini = harness.Mini;

        harness.Tray!.Controller.SelectFrameRate(30);

        Assert.Equal(1, oldMini.DisposeCalls);
        Assert.Equal(2, harness.Minis.Count);
        Assert.Equal(30, harness.Mini.FrameRate);
        var show = Assert.Single(harness.Mini.Shows);
        Assert.Equal(WallpaperScene.Raphael, show.Scene);
        Assert.Equal(oldMini.Shows[0].Bounds, show.Bounds);
        Assert.Equal(30, harness.Saves[^1].FrameRate);
    }

    [Fact]
    public void TrayRateChange_ToTheCurrentRate_IsANoOp()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);

        harness.Tray!.Controller.SelectFrameRate(60);

        Assert.Single(harness.Layers);
        Assert.Equal(0, harness.Layer.DisposeCalls);
        Assert.Empty(harness.Saves);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(120)]
    public void TrayRateChange_ToAnythingButThirtyOrSixty_IsRefused(int fps)
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);

        harness.Tray!.Controller.SelectFrameRate(fps);

        Assert.Single(harness.Layers);
        Assert.Empty(harness.Saves);
        Assert.Equal(60, harness.Tray.Controller.FrameRate);
    }

    [Fact]
    public void TrayRateChange_RunsOnTheUiThread_NotInsideTheMenusOwnMessage()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);
        harness.DeferUi = true;

        harness.Tray!.Controller.SelectFrameRate(30);

        Assert.Single(harness.Layers);
        harness.RunUi();
        Assert.Equal(2, harness.Layers.Count);
    }

    [Fact]
    public void AModeSwitch_KeepsTheChosenRate()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);
        harness.Tray!.Controller.SelectFrameRate(30);

        harness.Tray.Controller.SelectMode(WallpaperMode.SceneMini);

        Assert.Equal(30, harness.Mini.FrameRate);
        Assert.Equal(30, harness.Saves[^1].FrameRate);
    }

    [Fact]
    public void ARateChange_WithAnUnreadableSettingsFile_StillRebuilds_ButNeverSaves()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings, SettingsLoadStatus.Unreadable);

        harness.Tray!.Controller.SelectFrameRate(30);

        Assert.Equal(30, harness.Layer.FrameRate);
        Assert.Empty(harness.Saves);
        Assert.Contains("settings-file save-skipped reason=unreadable", harness.Trace);
    }

    [Fact]
    public void ARateChangeWhileAnAlertShows_ReshowsItsRemainingTimeSilently()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);
        harness.Server.Options.HandleAlert("warning:1 duration:10");
        var oldLayer = harness.Layer;
        Assert.Single(oldLayer.Starts);
        harness.Clock.Advance(TimeSpan.FromSeconds(4));

        harness.Tray!.Controller.SelectFrameRate(30);
        harness.Tick();

        var alert = Assert.Single(harness.Layer.Starts);
        Assert.NotSame(oldLayer, harness.Layer);
        Assert.Equal(6000, alert.DurationMilliseconds);
        Assert.Equal([AlertKind.Warning], harness.Sounds.Played);
    }

    [Fact]
    public void ARateChangeWhileAHeldWarningShows_KeepsItUp_WithoutSoundingAgain()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings);
        harness.Server.Options.HandleAlert("warning:1 duration:0");
        Assert.Single(harness.Mini.Alerts);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        harness.Tray!.Controller.SelectFrameRate(30);
        harness.Tick();

        Assert.Single(harness.Mini.Alerts);
        Assert.Equal(30, harness.Mini.FrameRate);
        Assert.Equal([AlertKind.Warning], harness.Sounds.Played);
    }
}
