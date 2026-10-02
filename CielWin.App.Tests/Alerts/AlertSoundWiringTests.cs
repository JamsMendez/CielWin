using CielWin.App.Alerts;
using CielWin.App.Tests.Composition;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// The alert sound through the whole composition: one sound per accepted alert, none when the same
/// alert is shown again on the surface a mode switch brings up, and no built-in sound shipped.
/// </summary>
public sealed class AlertSoundWiringTests
{
    [Fact]
    public void AnAcceptedAlert_PlaysItsSound_WhenItShows()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperMode: WallpaperMode.Scene));

        harness.Server.Options.HandleAlert("warning:1 failed:1");
        harness.Tick();

        Assert.Single(harness.Layer.Starts);
        Assert.Equal([AlertKind.Failed], harness.Sounds.Played);
    }

    [Fact]
    public void AModeSwitchWhileAnAlertShows_ReshowsItSilently()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperMode: WallpaperMode.Scene));
        harness.Server.Options.HandleAlert("warning:1 duration:10");

        harness.Tray!.Controller.SelectMode(WallpaperMode.SceneMini);
        harness.Tick();

        Assert.Single(harness.Mini.Alerts);
        Assert.Equal([AlertKind.Warning], harness.Sounds.Played);
    }

    [Fact]
    public void SoundsSwitchedOffInSettings_PlayNothing_ButTheAlertStillShows()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(AlertSoundsEnabled: false));

        harness.Server.Options.HandleAlert("failed:1");

        Assert.Single(harness.Layer.Starts);
        Assert.Empty(harness.Sounds.Played);
    }

    [Fact]
    public void TheTrayToggle_MutesAndPersists_ThenUnmutesAndPersists()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperScene: WallpaperScene.Idle));
        Assert.True(harness.Tray!.Controller.AlertSoundsEnabled);

        harness.Tray.Controller.ToggleAlertSounds();

        Assert.False(harness.Tray.Controller.AlertSoundsEnabled);
        var muted = Assert.Single(harness.Saves);
        Assert.Equal(new Settings(WallpaperScene: WallpaperScene.Idle, AlertSoundsEnabled: false), muted);

        harness.Server.Options.HandleAlert("warning:1 duration:1");
        Assert.Empty(harness.Sounds.Played);

        harness.Tray.Controller.ToggleAlertSounds();
        Assert.True(harness.Tray.Controller.AlertSoundsEnabled);
        Assert.True(harness.Saves[^1].AlertSoundsEnabled);
        Assert.Equal(2, harness.Saves.Count);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Tick();
        harness.Server.Options.HandleAlert("failed:1");
        Assert.Equal([AlertKind.Failed], harness.Sounds.Played);
    }

    [Fact]
    public void NoSoundShipsWithTheApp()
    {
        var resources = typeof(IAlertSoundPlayer).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(resources, name => name.Contains(".Sounds.", StringComparison.OrdinalIgnoreCase));
    }
}
