using CielWin.App.Input;
using CielWin.App.Tests.Composition;
using CielWin.Interop;

namespace CielWin.App.Tests;

/// <summary>
/// The composition root as a whole: what it builds per mode, the watch tick, live mode switching
/// from the tray, exit and shutdown. Every outside-world seam is a fake (<see cref="CompositionHarness"/>).
/// </summary>
public sealed class AppCompositionTests
{
    private static readonly Settings SceneSettings = new(WallpaperMode: WallpaperMode.Scene, WallpaperScene: WallpaperScene.Idle);
    private static readonly Settings MiniSettings = new(WallpaperMode: WallpaperMode.SceneMini, WallpaperScene: WallpaperScene.Raphael);

    [Fact]
    public void SceneMode_AttachesTheWallpaperHostOnItsThread_AndPreloadsTheLayerWithThePersistedScene()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(SceneSettings);

        Assert.Single(harness.Hosts);
        Assert.Equal(1, harness.Host.AttachCalls);
        var layer = Assert.Single(harness.Layers);
        Assert.Same(harness.Host, layer.Surface);
        Assert.Equal(WallpaperScene.Idle, layer.InitialScene);
        Assert.Equal(1, layer.Preloads);
        Assert.Empty(harness.Minis);
    }

    [Fact]
    public void SceneMode_StartsOnTheUiThread_NotInlineDuringWiring()
    {
        var harness = new CompositionHarness { DeferUi = true };

        using var composition = harness.Wire(SceneSettings);
        Assert.Empty(harness.Layers);

        harness.RunUi();

        Assert.Single(harness.Layers);
    }

    [Fact]
    public void SceneMode_WhenTheLayerCannotBeCreated_TracesAndKeepsRunning()
    {
        var harness = new CompositionHarness { LayerFactoryThrows = true };

        using var composition = harness.Wire(SceneSettings);
        harness.Tick();

        Assert.Contains(harness.Trace, line => line.Contains("scene-layer create-failed", StringComparison.Ordinal));
    }

    [Fact]
    public void MiniMode_ShowsTheMiniWindow_AndBuildsNoWallpaperHostOrLayer()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(MiniSettings);

        var mini = Assert.Single(harness.Minis);
        Assert.Equal(WallpaperScene.Raphael, Assert.Single(mini.Shows).Scene);
        Assert.Empty(harness.Hosts);
        Assert.Empty(harness.Layers);
    }

    [Fact]
    public void Wire_SchedulesTheWatchTick_AtTheWatchInterval()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(SceneSettings);

        Assert.Equal(AppComposition.WatchInterval, harness.ScheduledInterval);
    }

    [Fact]
    public void TheWatchTick_ReattachesTheWallpaperHost_SoAnExplorerRestartOrALateDesktopIsRecovered()
    {
        var harness = new CompositionHarness { HostAttachResult = false };
        using var composition = harness.Wire(SceneSettings);

        harness.HostAttachResult = true;
        harness.Tick();
        harness.Tick();

        Assert.Equal(3, harness.Host.AttachCalls);
    }

    [Fact]
    public void TheWatchTick_NeverQueuesASecondAttachBehindOneThatHasNotRunYet()
    {
        var harness = new CompositionHarness { HoldWallpaperThread = true };
        using var composition = harness.Wire(SceneSettings);

        harness.Tick();
        harness.Tick();

        Assert.Equal(1, harness.Thread.PendingCount);
    }

    [Fact]
    public void Wire_RegistersAltMAndAltShiftM()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(SceneSettings);

        Assert.Equal(
            [
                (MiniPositionHotkeys.ClockwiseId, HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, MiniPositionHotkeys.VirtualKeyM),
                (MiniPositionHotkeys.CounterClockwiseId, HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat, MiniPositionHotkeys.VirtualKeyM),
            ],
            harness.Hotkeys.Registrations);
    }

    [Fact]
    public void AHotkeyThatCannotBeRegistered_IsTraced_AndStartupContinues()
    {
        var harness = new CompositionHarness();
        harness.Hotkeys.RegisterResult = false;

        using var composition = harness.Wire(SceneSettings);

        Assert.Equal(2, harness.Trace.Count(line => line.Contains("hotkey register-failed", StringComparison.Ordinal)));
        Assert.NotNull(harness.Tray);
    }

    [Fact]
    public void TrayModeSwitch_SceneToMini_DisposesTheWallpaperShowsTheMiniAndPersistsTheMode()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);

        harness.Tray!.Controller.SelectMode(WallpaperMode.SceneMini);

        Assert.Equal(1, harness.Layer.DisposeCalls);
        Assert.Equal(1, harness.Host.DisposeCalls);
        Assert.Equal(1, harness.Thread.DisposeCalls);
        var show = Assert.Single(harness.Mini.Shows);
        Assert.Equal(WallpaperScene.Idle, show.Scene);
        Assert.Equal(WallpaperMode.SceneMini, harness.Tray.Controller.Mode);
        Assert.Equal(WallpaperMode.SceneMini, harness.Saves[^1].WallpaperMode);
    }

    [Fact]
    public void TrayModeSwitch_MiniToScene_DisposesTheMiniAndAttachesANewWallpaper()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings);

        harness.Tray!.Controller.SelectMode(WallpaperMode.Scene);

        Assert.Equal(1, harness.Mini.DisposeCalls);
        Assert.Equal(1, harness.Host.AttachCalls);
        Assert.Equal(WallpaperScene.Raphael, harness.Layer.InitialScene);
        Assert.Equal(WallpaperMode.Scene, harness.Saves[^1].WallpaperMode);
    }

    [Fact]
    public void TrayClicks_RunOnTheUiThread_NotInsideTheMenusOwnMessage()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);
        harness.DeferUi = true;

        harness.Tray!.Controller.SelectScene(WallpaperScene.Raphael);
        harness.Tray.Controller.SelectMode(WallpaperMode.SceneMini);
        Assert.Empty(harness.Layer.Switches);
        Assert.Empty(harness.Minis);

        harness.RunUi();

        Assert.Equal([WallpaperScene.Raphael], harness.Layer.Switches);
        Assert.Single(harness.Minis);
    }

    [Fact]
    public void TrayModeSwitch_ToTheCurrentMode_IsANoOp()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);

        harness.Tray!.Controller.SelectMode(WallpaperMode.Scene);

        Assert.Single(harness.Hosts);
        Assert.Equal(0, harness.Layer.DisposeCalls);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void TrayExit_ShutsTheAppDown()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(SceneSettings);

        harness.Tray!.Controller.Exit();

        Assert.Equal(1, harness.ShutdownCalls);
    }

    [Fact]
    public void Dispose_SceneMode_ReleasesEverythingExactlyOnce()
    {
        var harness = new CompositionHarness();
        var composition = harness.Wire(SceneSettings);

        composition.Dispose();
        composition.Dispose();

        Assert.True(harness.TickStopped);
        Assert.Equal(1, harness.Server.DisposeCalls);
        Assert.Equal(1, harness.Hotkeys.DisposeCalls);
        Assert.Equal(1, harness.Tray!.DisposeCalls);
        Assert.Equal(1, harness.Layer.DisposeCalls);
        Assert.Equal(1, harness.Host.DisposeCalls);
        Assert.Equal(1, harness.Thread.DisposeCalls);
    }

    [Fact]
    public void Dispose_MiniMode_DisposesTheMiniWindow()
    {
        var harness = new CompositionHarness();
        var composition = harness.Wire(MiniSettings);

        composition.Dispose();

        Assert.Equal(1, harness.Mini.DisposeCalls);
    }

    [Fact]
    public void Dispose_WhenOnePartThrows_StillReleasesTheRest()
    {
        var harness = new CompositionHarness();
        var composition = harness.Wire(SceneSettings);
        harness.Server.ThrowOnDispose = true;

        composition.Dispose();

        Assert.Equal(1, harness.Hotkeys.DisposeCalls);
        Assert.Equal(1, harness.Tray!.DisposeCalls);
        Assert.Equal(1, harness.Host.DisposeCalls);
        Assert.Contains(harness.Trace, line => line.Contains("dispose-failed part=http-server", StringComparison.Ordinal));
    }
}
