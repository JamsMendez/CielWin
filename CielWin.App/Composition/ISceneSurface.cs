using CielWin.App.Alerts;

namespace CielWin.App.Composition;

/// <summary>
/// Where the scene is on screen right now: the desktop wallpaper (<see cref="WallpaperSceneSurface"/>)
/// or the mini window (<see cref="MiniSceneSurface"/>). Exactly one exists at a time; a mode switch
/// disposes it and starts the other. Every member runs on the UI thread.
/// </summary>
internal interface ISceneSurface : IDisposable
{
    /// <summary>Brings the surface up. Never throws; failures are traced.</summary>
    void Start();

    /// <summary>Switches the scene live. False when the surface cannot switch (nothing on screen).</summary>
    bool SwitchScene(WallpaperScene scene);

    /// <summary>Whether an alert started now would actually be seen.</summary>
    bool CanShowAlerts(bool primaryMonitorCovered);

    void ShowAlert(AlertShowRequest request);

    void HideAlert();

    /// <summary>The watch tick: keep-alive, pause, placement. Never throws.</summary>
    void Tick(bool primaryMonitorCovered);
}
