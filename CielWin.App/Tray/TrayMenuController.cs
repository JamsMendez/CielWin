namespace CielWin.App.Tray;

/// <summary>
/// Pure, Win32-free tray menu behavior. Owns no state: it reports what the injected getters say (the
/// scene can also change over HTTP while the menu is closed) and forwards every click, so it is
/// testable with local closures. <see cref="TrayIconHost"/> is the thin WinForms wrapper.
/// </summary>
public sealed class TrayMenuController(
    Func<WallpaperMode> getMode, Action<WallpaperMode> selectMode,
    Func<WallpaperScene> getScene, Action<WallpaperScene> selectScene,
    Func<bool> getAlertSounds, Action toggleAlertSounds,
    Action exit)
{
    /// <summary>Every mode, in declaration order: the mode submenu's items.</summary>
    public static IReadOnlyList<WallpaperMode> Modes { get; } = Enum.GetValues<WallpaperMode>();

    /// <summary>Every bundled scene, in declaration order: the scene submenu's items.</summary>
    public static IReadOnlyList<WallpaperScene> Scenes { get; } = Enum.GetValues<WallpaperScene>();

    public WallpaperMode Mode => getMode();

    public WallpaperScene Scene => getScene();

    public bool AlertSoundsEnabled => getAlertSounds();

    public void SelectMode(WallpaperMode mode) => selectMode(mode);

    public void SelectScene(WallpaperScene scene) => selectScene(scene);

    public void ToggleAlertSounds() => toggleAlertSounds();

    public void Exit() => exit();
}
