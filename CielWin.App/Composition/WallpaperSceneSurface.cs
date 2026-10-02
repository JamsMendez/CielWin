using CielWin.App.Alerts;
using CielWin.Interop;

namespace CielWin.App.Composition;

/// <summary>
/// <see cref="WallpaperMode.Scene"/>: the scene page composited behind the desktop icons. Owns one
/// wallpaper host (attached on its own thread), that thread, and the WebView2 scene layer rendering
/// into the host's overlay visual.
/// </summary>
/// <remarks>
/// <para>
/// Attach is retried on every watch tick, never just once at startup: Explorer may not have built its
/// desktop windows yet when CielWin starts at logon, and <see cref="ISceneWallpaperHost.TryAttach"/>
/// is also the re-attach after an Explorer restart or a slideshow-created wallpaper layer. At most one
/// attach is queued at a time, so a slow host thread never builds a backlog.
/// </para>
/// <para>
/// The scene pauses while a fullscreen window covers the primary monitor (it is invisible then) and
/// resumes when uncovered. Only a change is sent and traced; a failed send is retried by the next
/// tick and only the first failure of a streak is traced.
/// </para>
/// </remarks>
internal sealed class WallpaperSceneSurface(
    ISceneWallpaperHost host,
    IWallpaperThread thread,
    Func<ICompositionOverlaySurface, WallpaperScene, ISceneLayer> createLayer,
    WallpaperScene scene,
    Action<string> trace) : ISceneSurface
{
    private WallpaperScene _scene = scene;
    private ISceneLayer? _layer;
    // Written on the wallpaper thread, read on the UI thread.
    private volatile bool _attached;
    private volatile bool _attachPending;
    private volatile bool _disposed;
    private bool? _lastTracedAttached;
    private bool _paused;
    private bool _pauseFailing;

    public void Start()
    {
        RequestAttach();
        try
        {
            var layer = createLayer(host, _scene);
            try
            {
                layer.Preload();
            }
            catch
            {
                layer.Dispose();
                throw;
            }

            _layer = layer;
        }
        catch (Exception error)
        {
            trace($"scene-layer create-failed error={error.GetType().Name}");
        }
    }

    public bool SwitchScene(WallpaperScene scene)
    {
        _scene = scene;
        // Without a layer only the scene is recorded; there is nothing on screen to switch.
        _layer?.SwitchScene(scene);
        return true;
    }

    public bool CanShowAlerts(bool primaryMonitorCovered) =>
        _layer is not null && _attached && host.IsCompositionReady && !primaryMonitorCovered;

    public void ShowAlert(AlertShowRequest request) => _layer?.Start(request);

    public void HideAlert() => _layer?.End();

    public void Tick(bool primaryMonitorCovered)
    {
        RequestAttach();
        UpdatePause(primaryMonitorCovered);
    }

    private void RequestAttach()
    {
        if (_disposed || _attachPending)
        {
            return;
        }

        _attachPending = true;
        thread.Post(() =>
        {
            _attachPending = false;
            if (_disposed)
            {
                return;
            }

            // TryAttach is documented never to throw; a throw is still traced, not let loose on the
            // thread that owns the host window.
            bool attached;
            try
            {
                attached = host.TryAttach();
            }
            catch (Exception error)
            {
                trace($"scene-wallpaper attach-failed error={error.GetType().Name}");
                attached = false;
            }

            _attached = attached;
            if (_lastTracedAttached != attached)
            {
                _lastTracedAttached = attached;
                trace($"scene-wallpaper attached={attached}");
            }
        });
    }

    private void UpdatePause(bool covered)
    {
        if (_layer is null || !_attached || covered == _paused)
        {
            return;
        }

        try
        {
            _layer.SetScenePaused(covered);
        }
        catch (Exception error)
        {
            if (!_pauseFailing)
            {
                trace($"scene-wallpaper {(covered ? "pause" : "resume")}-failed error={error.GetType().Name}");
            }

            _pauseFailing = true;
            return;
        }

        _pauseFailing = false;
        _paused = covered;
        trace(covered ? "scene-wallpaper paused reason=fullscreen" : "scene-wallpaper resumed");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Layer first: its visual lives in the host's composition tree.
        Guard("layer", () => _layer?.Dispose());
        _layer = null;
        Guard("host", () =>
        {
            if (!thread.Invoke(host.Dispose))
            {
                trace("scene-wallpaper host-dispose not-confirmed");
            }
        });
        Guard("thread", thread.Dispose);
    }

    private void Guard(string part, Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            trace($"scene-wallpaper dispose-failed part={part} error={error.GetType().Name}");
        }
    }
}
