using CielWin.App.Alerts;
using CielWin.App.Composition;
using CielWin.App.Tests.Composition;
using CielWin.Interop;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// Alerts in scene (wallpaper) mode: an accepted HTTP command reaches the preloaded scene layer at
/// once, laid out as the tile mosaic, held while a fullscreen window covers the desktop or the host is
/// not attached, ended when its duration runs out.
/// </summary>
public sealed class WebViewAlertCompositionWiringTests
{
    private static (CompositionHarness Harness, AppComposition Composition) WireScene(Action<CompositionHarness>? configure = null)
    {
        var harness = new CompositionHarness();
        configure?.Invoke(harness);
        return (harness, harness.Wire(new Settings(WallpaperMode: WallpaperMode.Scene)));
    }

    [Fact]
    public void AnAcceptedAlert_StartsTheLayerImmediately_WithoutWaitingForTheWatchTick()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;

        harness.Server.Options.HandleAlert("warning:1");

        var start = Assert.Single(harness.Layer.Starts);
        Assert.Equal(["warning"], start.Tiles);
    }

    [Fact]
    public void AMultiGroupCommand_ThreadsTheFullTileListGridAndGapToTheLayer()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;

        harness.Server.Options.HandleAlert("warning:2 failed:1");

        var start = Assert.Single(harness.Layer.Starts);
        Assert.Equal(["failed", "warning", "warning"], start.Tiles);
        Assert.Equal(2, start.Columns);
        Assert.Equal(2, start.Rows);
        Assert.Equal(AlertTileLayout.GapPixels, start.Gap);
    }

    [Fact]
    public void TheWorkAreaIsReadAtShowTime_RelativeToThePrimaryMonitor()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Display = new PrimaryDisplayInfo(new Rectangle(0, 0, 2560, 1440), new Rectangle(0, 48, 2560, 1440));

        harness.Server.Options.HandleAlert("failed:2");

        var start = Assert.Single(harness.Layer.Starts);
        Assert.Equal((0, 48, 2560, 1392), (start.WorkAreaLeft, start.WorkAreaTop, start.WorkAreaWidth, start.WorkAreaHeight));
    }

    [Fact]
    public void AWorkAreaReadThatThrows_DegradesToTheWholeCanvas_AndStillShowsTheAlert()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.DisplayOverride = () => throw new InvalidOperationException("no monitor");

        harness.Server.Options.HandleAlert("warning:1");

        var start = Assert.Single(harness.Layer.Starts);
        Assert.Equal((0, 0, 0, 0), (start.WorkAreaLeft, start.WorkAreaTop, start.WorkAreaWidth, start.WorkAreaHeight));
        Assert.Contains(harness.Trace, line => line.Contains("alert workarea-failed", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAlertEnds_OnceItsDurationHasRunOut()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Server.Options.HandleAlert("warning:1 duration:2");

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Tick();
        Assert.Equal(0, harness.Layer.Ends);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Tick();
        Assert.Equal(1, harness.Layer.Ends);
        Assert.Single(harness.Layer.Starts);
    }

    [Fact]
    public void ASecondCommandWhileShowing_StillRepliesOk_ButIsIgnored()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Server.Options.HandleAlert("warning:1");

        var reply = harness.Server.Options.HandleAlert("failed:1");

        Assert.Equal(AlertReplyProtocol.OkReply, reply);
        Assert.Single(harness.Layer.Starts);
    }

    [Fact]
    public void ACoveredDesktop_HoldsTheAlert_UntilUncovered()
    {
        var (harness, composition) = WireScene(h => h.Covered = true);
        using var _ = composition;

        harness.Server.Options.HandleAlert("warning:1");
        harness.Tick();
        Assert.Empty(harness.Layer.Starts);

        harness.Covered = false;
        harness.Tick();

        Assert.Single(harness.Layer.Starts);
    }

    [Fact]
    public void WhenTheHostNeverAttaches_AlertsStayHeld()
    {
        var (harness, composition) = WireScene(h => h.HostAttachResult = false);
        using var _ = composition;

        harness.Server.Options.HandleAlert("warning:1");
        harness.Tick();

        Assert.Empty(harness.Layer.Starts);
    }

    [Fact]
    public void WhenTheCompositionTreeIsNotReady_AlertsStayHeld()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Host.IsCompositionReady = false;

        harness.Server.Options.HandleAlert("warning:1");

        Assert.Empty(harness.Layer.Starts);
    }

    [Fact]
    public void AFailedLayerStart_DoesNotEscapeAndIsRetriedNextTick()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Layer.StartFailuresLeft = 1;

        harness.Server.Options.HandleAlert("warning:1");
        Assert.Empty(harness.Layer.Starts);
        Assert.Contains(harness.Trace, line => line.Contains("alert start-failed", StringComparison.Ordinal));

        harness.Tick();

        Assert.Single(harness.Layer.Starts);
    }

    [Fact]
    public void ACoverCheckThatThrows_ReadsAsUncovered_AndIsTraced()
    {
        var (harness, composition) = WireScene(h => h.CoveredOverride = () => throw new InvalidOperationException("boom"));
        using var _ = composition;

        harness.Server.Options.HandleAlert("warning:1");

        Assert.Single(harness.Layer.Starts);
        Assert.Contains(harness.Trace, line => line.Contains("cover-check-failed", StringComparison.Ordinal));
    }

    [Fact]
    public void AModeSwitchWhileAnAlertShows_ReshowsItsRemainingTimeOnTheNewSurface()
    {
        var (harness, composition) = WireScene();
        using var _ = composition;
        harness.Server.Options.HandleAlert("warning:1 duration:10");
        harness.Clock.Advance(TimeSpan.FromSeconds(4));

        harness.Tray!.Controller.SelectMode(WallpaperMode.SceneMini);
        harness.Tick();

        var alert = Assert.Single(harness.Mini.Alerts);
        Assert.Equal(6000, alert.DurationMilliseconds);
    }
}
