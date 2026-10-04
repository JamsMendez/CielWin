using CielWin.App.Tests.Composition;
using CielWin.Interop;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// The one local HTTP server: started on <c>http-server-port</c> with both routes when
/// <c>http-server = on</c>, never created (no token read, no port bound) when off, and a failure to
/// build or start it never takes the rest of the app down.
/// </summary>
public sealed class HttpAlertCompositionWiringTests
{
    [Fact]
    public void ServerOff_NoServerIsCreated_AndTheTokenIsNeverLoaded()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(new Settings(HttpServerEnabled: false));

        Assert.Empty(harness.ServerRequests);
        Assert.Equal(0, harness.TokenLoads);
        Assert.Contains(harness.Trace, line => line.Contains("http-server disabled", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerOn_StartsOneServerOnTheSettingsPortWithTheLoadedTokenAndBothRoutes()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(new Settings(HttpServerEnabled: true, HttpServerPort: 51234));

        var options = Assert.Single(harness.ServerRequests);
        Assert.Equal(51234, options.Port);
        Assert.Equal("token-value", options.Token);
        Assert.NotNull(options.HandleAlert);
        Assert.NotNull(options.HandleSceneSwitch);
        Assert.Equal(1, harness.Server.StartCalls);
    }

    [Fact]
    public void ServerOn_ServerDiagnosticsReachTheTrace()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings());

        harness.Server.Options.Diagnostic("alert http: failed to start listening");

        Assert.Contains("alert http: failed to start listening", harness.Trace);
    }

    [Fact]
    public void NullToken_DoesNotStartTheServer_AndTracesWithoutTheToken()
    {
        var harness = new CompositionHarness { Token = null };

        using var composition = harness.Wire(new Settings());

        Assert.Empty(harness.ServerRequests);
        Assert.Contains(harness.Trace, line => line.Contains("http-server token unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerFactoryThrows_IsTraced_AndComposingContinues()
    {
        var harness = new CompositionHarness { ServerFactoryThrows = true };

        using var composition = harness.Wire(new Settings());

        Assert.Contains(harness.Trace, line => line.Contains("http-server start-failed error=InvalidOperationException", StringComparison.Ordinal));
        Assert.NotNull(harness.Tray);
        Assert.Single(harness.Layers);
    }

    [Fact]
    public void ServerIsDisposedOnShutdown()
    {
        var harness = new CompositionHarness();
        var composition = harness.Wire(new Settings());

        composition.Dispose();

        Assert.Equal(1, harness.Server.DisposeCalls);
    }

    [Fact]
    public void SceneRoute_AcceptsAtOnce_ThenSwitchesTheWallpaperAndPersistsOnTheUiThread()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperScene: WallpaperScene.Processing));
        harness.DeferUi = true;

        var accepted = harness.Server.Options.HandleSceneSwitch("explorer");

        Assert.True(accepted);
        Assert.Empty(harness.Layer.Switches);
        Assert.Empty(harness.Saves);

        harness.RunUi();

        Assert.Equal([WallpaperScene.Explorer], harness.Layer.Switches);
        Assert.Equal(WallpaperScene.Explorer, harness.Saves[^1].WallpaperScene);
    }

    [Fact]
    public void SceneRoute_ANameOutsideTheEnum_IsRefused()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings());

        Assert.False(harness.Server.Options.HandleSceneSwitch("nebula"));
        Assert.Empty(harness.Layer.Switches);
    }

    [Fact]
    public void SceneRoute_ASwitchThatThrows_IsTracedAndNotPersisted()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings());
        harness.Layer.ThrowOnSwitch = true;

        Assert.True(harness.Server.Options.HandleSceneSwitch("idle"));

        Assert.Empty(harness.Saves);
        Assert.Contains(harness.Trace, line => line.Contains("scene-switch switch-failed source=http", StringComparison.Ordinal));
    }

    [Fact]
    public void AlertRoute_AValidCommandRepliesOk()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings());

        Assert.Equal("ok id=1", harness.Server.Options.HandleAlert("warning:1"));
    }

    [Fact]
    public void ClearRoute_IsWired_AndEndsTheHeldWarningAtOnce()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings());
        Assert.NotNull(harness.Server.Options.HandleAlertClear);

        Assert.Equal("ok id=1", harness.Server.Options.HandleAlert("warning:1 duration:0"));
        Assert.Single(harness.Layer.Starts);

        Assert.Equal(AlertReplyProtocol.OkReply, harness.Server.Options.HandleAlertClear!(null));

        Assert.Equal(1, harness.Layer.Ends);
        Assert.Contains("alert 1 cleared", harness.Trace);
    }

    [Fact]
    public void AHeldWarning_ShowsForTheHoldMaxFromTheSettings()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(AlertHoldMaxSeconds: 90));

        harness.Server.Options.HandleAlert("warning:1 duration:0");

        Assert.Equal(90000, Assert.Single(harness.Layer.Starts).DurationMilliseconds);
    }

    [Fact]
    public void AlertRoute_AMalformedCommandRepliesWithAnError()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings());

        var reply = harness.Server.Options.HandleAlert("bogus");

        Assert.StartsWith(AlertReplyProtocol.FormatError(string.Empty), reply, StringComparison.Ordinal);
        Assert.Empty(harness.Layer.Starts);
    }
}
