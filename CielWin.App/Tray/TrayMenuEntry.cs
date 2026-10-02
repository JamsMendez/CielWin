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

    /// <summary>Ends CielWin.</summary>
    Exit,
}
