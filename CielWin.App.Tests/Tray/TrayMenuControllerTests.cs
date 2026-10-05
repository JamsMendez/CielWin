using CielWin.App.Alerts;
using CielWin.App.Tray;

namespace CielWin.App.Tests.Tray;

/// <summary>
/// <see cref="TrayMenuController"/>: a pure, delegate-based pass-through. It owns no state; it
/// reports what the injected getters say and forwards every click.
/// </summary>
public sealed class TrayMenuControllerTests
{
    private WallpaperMode _mode = WallpaperMode.Scene;
    private WallpaperScene _scene = WallpaperScene.Processing;
    private readonly List<WallpaperMode> _modeSelections = [];
    private readonly List<WallpaperScene> _sceneSelections = [];
    private int _frameRate = 60;
    private readonly List<int> _frameRateSelections = [];
    private bool _alertSounds = true;
    private int _alertSoundToggles;
    private readonly HashSet<AlertKind> _imported = [];
    private readonly List<AlertKind> _imports = [];
    private readonly List<AlertKind> _removals = [];
    private int _exits;

    private TrayMenuController Create() => new(
        () => _mode, _modeSelections.Add,
        () => _scene, _sceneSelections.Add,
        () => _frameRate, _frameRateSelections.Add,
        () => _alertSounds, () => _alertSoundToggles++,
        _imported.Contains, _imports.Add, _removals.Add,
        () => _exits++);

    [Fact]
    public void ModeAndScene_ReflectTheInjectedGetters_NotInternalState()
    {
        var controller = Create();
        Assert.Equal(WallpaperMode.Scene, controller.Mode);
        Assert.Equal(WallpaperScene.Processing, controller.Scene);

        _mode = WallpaperMode.SceneMini;
        _scene = WallpaperScene.Raphael;

        Assert.Equal(WallpaperMode.SceneMini, controller.Mode);
        Assert.Equal(WallpaperScene.Raphael, controller.Scene);
    }

    [Fact]
    public void SelectMode_ForwardsTheChoice()
    {
        var controller = Create();

        controller.SelectMode(WallpaperMode.SceneMini);

        Assert.Equal([WallpaperMode.SceneMini], _modeSelections);
    }

    [Fact]
    public void SelectScene_ForwardsTheChoice()
    {
        var controller = Create();

        controller.SelectScene(WallpaperScene.Explorer);

        Assert.Equal([WallpaperScene.Explorer], _sceneSelections);
    }

    [Fact]
    public void FrameRate_ReflectsTheInjectedGetter()
    {
        var controller = Create();
        Assert.Equal(60, controller.FrameRate);

        _frameRate = 30;

        Assert.Equal(30, controller.FrameRate);
    }

    [Fact]
    public void SelectFrameRate_ForwardsTheChoice()
    {
        var controller = Create();

        controller.SelectFrameRate(30);

        Assert.Equal([30], _frameRateSelections);
    }

    [Fact]
    public void TheMenuOffersThirtyThenSixtyFps()
    {
        Assert.Equal([30, 60], TrayMenuController.FrameRates);
    }

    [Fact]
    public void AlertSoundsEnabled_ReflectsTheInjectedGetter()
    {
        var controller = Create();
        Assert.True(controller.AlertSoundsEnabled);

        _alertSounds = false;

        Assert.False(controller.AlertSoundsEnabled);
    }

    [Fact]
    public void ToggleAlertSounds_ForwardsTheClick()
    {
        var controller = Create();

        controller.ToggleAlertSounds();

        Assert.Equal(1, _alertSoundToggles);
    }

    [Fact]
    public void ImportAndRemove_ForwardTheKind()
    {
        var controller = Create();

        controller.ImportAlertSound(AlertKind.Warning);
        controller.RemoveAlertSound(AlertKind.Failed);

        Assert.Equal([AlertKind.Warning], _imports);
        Assert.Equal([AlertKind.Failed], _removals);
    }

    [Fact]
    public void WithNoSoundImported_OnlyTheImportEntriesOfTheSoundGroupShow()
    {
        var controller = Create();

        Assert.True(controller.IsVisible(TrayMenuEntry.ImportFailedSound));
        Assert.True(controller.IsVisible(TrayMenuEntry.ImportWarningSound));
        Assert.False(controller.IsVisible(TrayMenuEntry.RemoveFailedSound));
        Assert.False(controller.IsVisible(TrayMenuEntry.RemoveWarningSound));
        Assert.False(controller.IsVisible(TrayMenuEntry.AlertSounds));
    }

    [Fact]
    public void WithOnlyTheFailedSound_RemoveFailedAndTheToggleShow()
    {
        var controller = Create();
        _imported.Add(AlertKind.Failed);

        Assert.True(controller.IsVisible(TrayMenuEntry.RemoveFailedSound));
        Assert.False(controller.IsVisible(TrayMenuEntry.RemoveWarningSound));
        Assert.True(controller.IsVisible(TrayMenuEntry.AlertSounds));
    }

    [Fact]
    public void WithOnlyTheWarningSound_RemoveWarningAndTheToggleShow()
    {
        var controller = Create();
        _imported.Add(AlertKind.Warning);

        Assert.False(controller.IsVisible(TrayMenuEntry.RemoveFailedSound));
        Assert.True(controller.IsVisible(TrayMenuEntry.RemoveWarningSound));
        Assert.True(controller.IsVisible(TrayMenuEntry.AlertSounds));
    }

    [Fact]
    public void TheOtherEntries_AlwaysShow()
    {
        var controller = Create();

        Assert.True(controller.IsVisible(TrayMenuEntry.Mode));
        Assert.True(controller.IsVisible(TrayMenuEntry.Scene));
        Assert.True(controller.IsVisible(TrayMenuEntry.FrameRate));
        Assert.True(controller.IsVisible(TrayMenuEntry.Exit));
    }

    [Fact]
    public void Exit_InvokesTheExitDelegate_ExactlyOnce()
    {
        var controller = Create();

        controller.Exit();

        Assert.Equal(1, _exits);
    }

    [Fact]
    public void TheMenuOffersEveryModeAndEveryScene_InDeclarationOrder()
    {
        Assert.Equal(Enum.GetValues<WallpaperMode>(), TrayMenuController.Modes);
        Assert.Equal(Enum.GetValues<WallpaperScene>(), TrayMenuController.Scenes);
    }
}
