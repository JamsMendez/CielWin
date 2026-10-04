using CielWin.App.Alerts;

namespace CielWin.App.Tray;

/// <summary>
/// Pure, Win32-free tray menu behavior. Owns no state: it reports what the injected getters say (the
/// scene can also change over HTTP while the menu is closed) and forwards every click, so it is
/// testable with local closures. <see cref="TrayIconHost"/> is the thin WinForms wrapper.
/// </summary>
public sealed class TrayMenuController(
    Func<WallpaperMode> getMode, Action<WallpaperMode> selectMode,
    Func<WallpaperScene> getScene, Action<WallpaperScene> selectScene,
    Func<int> getFrameRate, Action<int> selectFrameRate,
    Func<bool> getAlertSounds, Action toggleAlertSounds,
    Func<AlertKind, bool> hasAlertSound, Action<AlertKind> importAlertSound, Action<AlertKind> removeAlertSound,
    Action exit)
{
    /// <summary>Every mode, in declaration order: the mode submenu's items.</summary>
    public static IReadOnlyList<WallpaperMode> Modes { get; } = Enum.GetValues<WallpaperMode>();

    /// <summary>Every bundled scene, in declaration order: the scene submenu's items.</summary>
    public static IReadOnlyList<WallpaperScene> Scenes { get; } = Enum.GetValues<WallpaperScene>();

    /// <summary>Every frame-rate cap, lowest first: the frame-rate submenu's items.</summary>
    public static IReadOnlyList<int> FrameRates { get; } = Settings.FrameRates;

    public WallpaperMode Mode => getMode();

    public WallpaperScene Scene => getScene();

    public int FrameRate => getFrameRate();

    public bool AlertSoundsEnabled => getAlertSounds();

    public void SelectMode(WallpaperMode mode) => selectMode(mode);

    public void SelectScene(WallpaperScene scene) => selectScene(scene);

    public void SelectFrameRate(int fps) => selectFrameRate(fps);

    public void ToggleAlertSounds() => toggleAlertSounds();

    public void ImportAlertSound(AlertKind kind) => importAlertSound(kind);

    public void RemoveAlertSound(AlertKind kind) => removeAlertSound(kind);

    /// <summary>
    /// Whether <paramref name="entry"/> shows right now: a remove entry only while its kind has an
    /// imported sound, the mute toggle only while either kind has one; everything else always.
    /// </summary>
    public bool IsVisible(TrayMenuEntry entry) => entry switch
    {
        TrayMenuEntry.RemoveFailedSound => hasAlertSound(AlertKind.Failed),
        TrayMenuEntry.RemoveWarningSound => hasAlertSound(AlertKind.Warning),
        TrayMenuEntry.AlertSounds => hasAlertSound(AlertKind.Failed) || hasAlertSound(AlertKind.Warning),
        _ => true,
    };

    public void Exit() => exit();
}
