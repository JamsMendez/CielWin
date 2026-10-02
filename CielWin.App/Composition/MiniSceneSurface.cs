using CielWin.App.Alerts;
using CielWin.App.Wallpaper;
using CielWin.Interop;

namespace CielWin.App.Composition;

/// <summary>
/// <see cref="WallpaperMode.SceneMini"/>: the small topmost scene window at one of eight positions of
/// the primary work area. The desktop background stays as Windows has it.
/// </summary>
/// <remarks>
/// The window is topmost, so a fullscreen window on the primary monitor does not hide it: alerts are
/// never held for coverage and the scene is never paused for it (as in CosmicWin). Placement is read
/// live: a taskbar that moves or auto-hides re-places the window on the next watch tick.
/// </remarks>
internal sealed class MiniSceneSurface(
    IMiniSceneWindow window,
    Func<PrimaryDisplayInfo> readDisplay,
    WallpaperScene scene,
    MiniPosition position,
    Action<string> trace) : ISceneSurface
{
    private bool _shown;
    /// <summary>Where the window was last confirmed to sit; null when unknown (a move threw).</summary>
    private Rectangle? _placed;
    private bool _displayReadFailing;

    /// <summary>Where the window sits (or would sit, before a successful show).</summary>
    public MiniPosition Position { get; private set; } = position;

    public void Start()
    {
        try
        {
            var bounds = Placement(Position);
            _shown = window.Show(scene, bounds);
            _placed = bounds;
            trace($"mini-window shown={_shown}");
        }
        catch (Exception error)
        {
            trace($"mini-window show-failed error={error.GetType().Name}");
        }
    }

    public bool SwitchScene(WallpaperScene scene) => _shown && window.SwitchScene(scene);

    public bool CanShowAlerts(bool primaryMonitorCovered) => _shown && window.IsReady;

    public void ShowAlert(AlertShowRequest request) => window.ShowAlert(request);

    public void HideAlert() => window.HideAlert();

    public void Tick(bool primaryMonitorCovered)
    {
        if (!_shown)
        {
            return;
        }

        Rectangle bounds;
        try
        {
            bounds = Placement(Position);
            _displayReadFailing = false;
        }
        catch (Exception error)
        {
            if (!_displayReadFailing)
            {
                trace($"mini-window display-read-failed error={error.GetType().Name}");
            }

            _displayReadFailing = true;
            return;
        }

        if (bounds != _placed)
        {
            window.MoveTo(bounds);
            _placed = bounds;
        }
    }

    /// <summary>
    /// Moves the window to <paramref name="next"/>. Refused (false, traced) while the window failed to
    /// show or its browser is not attached, so a position is never persisted for a window that is not
    /// really there. A failing display read or move is refused the same way: it runs as a posted
    /// dispatcher operation, where an escaping exception would end the app. A move that throws may
    /// already have moved the native window, so its placement becomes unknown and the next tick puts it
    /// back at the confirmed <see cref="Position"/>.
    /// </summary>
    public bool TryMoveTo(MiniPosition next)
    {
        if (!_shown || !window.IsReady)
        {
            trace("mini-position ignored reason=window-not-ready");
            return false;
        }

        try
        {
            var bounds = Placement(next);
            _placed = null; // unknown until the move returns: a throwing move may have moved the window
            window.MoveTo(bounds);
            _placed = bounds;
        }
        catch (Exception error)
        {
            trace($"mini-position move-failed error={error.GetType().Name}");
            return false;
        }

        Position = next;
        return true;
    }

    private Rectangle Placement(MiniPosition corner)
    {
        var display = readDisplay();
        return MiniWindowPlacement.Compute(display.WorkArea, display.Bounds.Height, corner);
    }

    public void Dispose() => window.Dispose();
}
