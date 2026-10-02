using CielWin.App.Input;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// What the composition writes back to settings.conf, and the one case it must never write: a file
/// that exists but could not be read (<see cref="SettingsLoadResult.CanSave"/> false), because saving
/// then would overwrite the user's real file with defaults.
/// </summary>
public sealed class SettingsPersistenceWiringTests
{
    private static readonly Settings MiniSettings = new(WallpaperMode: WallpaperMode.SceneMini, MiniPosition: MiniPosition.TopLeft);

    [Fact]
    public void Startup_WritesNothing()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(new Settings());

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void EverySaveCarriesTheWholeRecord_NotJustTheChangedField()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings with { HttpServerPort = 50000, WallpaperScene = WallpaperScene.Idle });

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Equal(
            MiniSettings with { HttpServerPort = 50000, WallpaperScene = WallpaperScene.Idle, MiniPosition = MiniPosition.TopCenter },
            Assert.Single(harness.Saves));
    }

    [Fact]
    public void TraySceneSwitch_SwitchesTheActiveSceneAndPersistsIt()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperScene: WallpaperScene.Processing));

        harness.Tray!.Controller.SelectScene(WallpaperScene.Explorer);

        Assert.Equal([WallpaperScene.Explorer], harness.Layer.Switches);
        Assert.Equal(WallpaperScene.Explorer, Assert.Single(harness.Saves).WallpaperScene);
        Assert.Equal(WallpaperScene.Explorer, harness.Tray.Controller.Scene);
    }

    [Fact]
    public void TraySceneSwitch_InMiniMode_SwitchesTheMiniWindow()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings);

        harness.Tray!.Controller.SelectScene(WallpaperScene.Raphael);

        Assert.Equal([WallpaperScene.Raphael], harness.Mini.Switches);
        Assert.Equal(WallpaperScene.Raphael, Assert.Single(harness.Saves).WallpaperScene);
    }

    [Fact]
    public void TraySceneSwitch_ToTheCurrentScene_WritesNothing()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperScene: WallpaperScene.Idle));

        harness.Tray!.Controller.SelectScene(WallpaperScene.Idle);

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void UnreadableFile_IsTracedAtStartup()
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(new Settings(), SettingsLoadStatus.Unreadable);

        Assert.Contains(harness.Trace, line => line.Contains("settings-file unreadable", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableFile_HttpSceneSwitch_SwitchesButNeverSaves()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(), SettingsLoadStatus.Unreadable);

        harness.Server.Options.HandleSceneSwitch("idle");

        Assert.Equal([WallpaperScene.Idle], harness.Layer.Switches);
        Assert.Empty(harness.Saves);
        Assert.Contains(harness.Trace, line => line.Contains("settings-file save-skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableFile_TraySceneSwitch_SwitchesAndRemembersInMemoryButNeverSaves()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(), SettingsLoadStatus.Unreadable);

        harness.Tray!.Controller.SelectScene(WallpaperScene.Raphael);

        Assert.Equal(WallpaperScene.Raphael, harness.Tray.Controller.Scene);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void UnreadableFile_MiniMove_MovesButNeverSaves()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(MiniSettings, SettingsLoadStatus.Unreadable);

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Equal(2, harness.Mini.Moves.Count);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void UnreadableFile_ModeSwitch_SwitchesButNeverSaves()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(), SettingsLoadStatus.Unreadable);

        harness.Tray!.Controller.SelectMode(WallpaperMode.SceneMini);

        Assert.Single(harness.Minis);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void MissingFile_CanStillBeSaved()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(), SettingsLoadStatus.Missing);

        harness.Tray!.Controller.SelectScene(WallpaperScene.Raphael);

        Assert.Single(harness.Saves);
    }

    [Fact]
    public void ASaveThatThrows_IsTraced_AndTheChangeStillTakesEffect()
    {
        var harness = new CompositionHarness();
        var host = harness.Build();
        using var composition = AppComposition.Wire(
            new SettingsLoadResult(new Settings(), SettingsLoadStatus.Loaded),
            _ => throw new IOException("disk full"),
            host);

        harness.Tray!.Controller.SelectScene(WallpaperScene.Idle);

        Assert.Equal([WallpaperScene.Idle], harness.Layer.Switches);
        Assert.Equal(WallpaperScene.Idle, harness.Tray.Controller.Scene);
        Assert.Contains(harness.Trace, line => line.Contains("settings-file persist-failed", StringComparison.Ordinal));
    }
}
