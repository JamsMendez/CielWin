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
    private bool _alertSounds = true;
    private int _alertSoundToggles;
    private int _exits;

    private TrayMenuController Create() => new(
        () => _mode, _modeSelections.Add,
        () => _scene, _sceneSelections.Add,
        () => _alertSounds, () => _alertSoundToggles++,
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
