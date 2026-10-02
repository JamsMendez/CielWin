namespace CielWin.App.Tests.Composition;

/// <summary>
/// The scene wallpaper pauses while a fullscreen window covers the primary monitor and resumes once
/// it is gone. Polled on the watch tick; only a CHANGE is sent and traced, a failed send is retried.
/// </summary>
public sealed class FullscreenPauseWiringTests
{
    private static (CompositionHarness Harness, AppComposition Composition) WireScene()
    {
        var harness = new CompositionHarness();
        return (harness, harness.Wire(new Settings(WallpaperMode: WallpaperMode.Scene)));
    }

    [Fact]
    public void ACoveredDesktop_PausesTheScene_OnceAndTracesIt()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;

        harness.Covered = true;
        harness.Tick();
        harness.Tick();

        Assert.Equal([true], harness.Layer.Paused);
        Assert.Single(harness.Trace, line => line.Contains("scene-wallpaper paused", StringComparison.Ordinal));
    }

    [Fact]
    public void UncoveringTheDesktop_ResumesTheScene()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Covered = true;
        harness.Tick();

        harness.Covered = false;
        harness.Tick();

        Assert.Equal([true, false], harness.Layer.Paused);
        Assert.Contains(harness.Trace, line => line.Contains("scene-wallpaper resumed", StringComparison.Ordinal));
    }

    [Fact]
    public void ADesktopThatWasNeverCovered_SendsNothing()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;

        harness.Tick();
        harness.Tick();

        Assert.Empty(harness.Layer.Paused);
    }

    [Fact]
    public void WhenTheHostNeverAttaches_ACoveredDesktopSendsNothing()
    {
        var harness = new CompositionHarness { HostAttachResult = false, Covered = true };
        using var composition = harness.Wire(new Settings(WallpaperMode: WallpaperMode.Scene));

        harness.Tick();

        Assert.Empty(harness.Layer.Paused);
    }

    [Fact]
    public void InMiniMode_ACoveredDesktopNeverPausesAnything()
    {
        var harness = new CompositionHarness { Covered = true };
        using var composition = harness.Wire(new Settings(WallpaperMode: WallpaperMode.SceneMini));

        harness.Tick();

        Assert.Empty(harness.Layers);
        Assert.DoesNotContain(harness.Trace, line => line.Contains("paused", StringComparison.Ordinal));
    }

    [Fact]
    public void APauseThatFails_IsRetriedNextTick_AndOnlyTheFirstFailureOfAStreakIsTraced()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Layer.PauseFailuresLeft = 2;
        harness.Covered = true;

        harness.Tick();
        harness.Tick();
        Assert.Empty(harness.Layer.Paused);
        Assert.Single(harness.Trace, line => line.Contains("scene-wallpaper pause-failed", StringComparison.Ordinal));

        harness.Tick();

        Assert.Equal([true], harness.Layer.Paused);
    }

    [Fact]
    public void AModeSwitchBackToScene_StartsTheNewLayerUnpaused_AndPausesItIfStillCovered()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Covered = true;
        harness.Tick();

        harness.Tray!.Controller.SelectMode(WallpaperMode.SceneMini);
        harness.Tray.Controller.SelectMode(WallpaperMode.Scene);
        harness.Tick();

        Assert.Equal([true], harness.Layer.Paused);
    }
}
