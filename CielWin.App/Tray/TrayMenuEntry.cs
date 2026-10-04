namespace CielWin.App.Tray;

/// <summary>
/// The items the tray menu offers, named so their ORDER can be stated once and tested.
/// <see cref="TrayIconHost"/> builds its menu by walking <see cref="TrayIconHost.MenuOrder"/>.
/// </summary>
public enum TrayMenuEntry
{
    /// <summary>Submenu: scene wallpaper or mini window, the current one checked.</summary>
    Mode,

    /// <summary>Submenu: every bundled scene, the current one checked.</summary>
    Scene,

    /// <summary>Submenu: the global frame-rate cap, 30 FPS or 60 FPS, the current one checked.</summary>
    FrameRate,

    /// <summary>Opens a file dialog and imports the sound a failed alert plays.</summary>
    ImportFailedSound,

    /// <summary>Opens a file dialog and imports the sound a warning alert plays.</summary>
    ImportWarningSound,

    /// <summary>Deletes the imported failed sound. Shown only while there is one.</summary>
    RemoveFailedSound,

    /// <summary>Deletes the imported warning sound. Shown only while there is one.</summary>
    RemoveWarningSound,

    /// <summary>
    /// Toggle: whether a new alert plays its sound, checked when it does. Shown only while at least
    /// one sound is imported (with none there is nothing to mute).
    /// </summary>
    AlertSounds,

    /// <summary>Ends CielWin.</summary>
    Exit,
}
