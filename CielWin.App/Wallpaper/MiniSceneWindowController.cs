using CielWin.App.Alerts;
using CielWin.Interop;
using CielWin.Interop.Win32;
using System.Windows.Threading;
using DrawingRectangle = System.Drawing.Rectangle;

namespace CielWin.App.Wallpaper;

/// <summary>
/// The mini scene window (<see cref="WallpaperMode.SceneMini"/>): a small, square, always-on-top,
/// click-through, see-through window showing the <c>?variant=mini</c> scene page.
/// </summary>
public interface IMiniSceneWindow : IDisposable
{
    /// <summary>
    /// True once the browser is attached and the page can be driven (moved, switched, sent alerts). False
    /// before that, after an attach failure, while a WebView2 process failure is being recovered, and
    /// after dispose. The window can already be on screen (empty) while this is still false.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Creates and places the window at <paramref name="bounds"/> (physical pixels) and starts attaching the
    /// scene. False if the window could not be created or placed; true only says the window exists -- see
    /// <see cref="IsReady"/>.
    /// </summary>
    bool Show(WallpaperScene scene, Rectangle bounds);

    /// <summary>Switches scene live. The current scene again is an accepted no-op returning true.</summary>
    bool SwitchScene(WallpaperScene scene);

    /// <summary>Moves the window (physical pixels) at once, never activating it. Cancels a running glide.</summary>
    void MoveTo(Rectangle bounds);

    /// <summary>
    /// Glides the window to <paramref name="bounds"/> (physical pixels) over <see
    /// cref="MiniSceneWindowController.GlideDuration"/>, never activating it. A glide already running is
    /// retargeted from where the window is now; with no known current bounds the window is placed at once.
    /// </summary>
    void GlideTo(Rectangle bounds);

    /// <summary>
    /// Raised (on the owning UI thread) when a glide could not land on its target: the window may be left
    /// anywhere along the way, so whoever tracks its placement must treat it as unknown and re-place it.
    /// </summary>
    event Action? PlacementLost;

    /// <summary>Forwards an alert to the page (posted as soon as the page is ready).</summary>
    void ShowAlert(AlertShowRequest request);

    /// <summary>Tells the page to hide its alert overlay.</summary>
    void HideAlert();
}

/// <summary>The WebView2 side of the mini window, behind a seam so the controller is testable without one.</summary>
public interface IMiniSceneBrowser : IDisposable
{
    /// <summary>Raised when the current page finished navigating AND reported itself ready.</summary>
    event Action? Ready;

    /// <summary>
    /// Raised after the WebView2 process failed and the browser tore its controller down; the argument is a
    /// trace line naming the failure. The owner decides whether to re-attach.
    /// </summary>
    event Action<string>? Failed;

    /// <summary>Creates a transparent composition controller on <paramref name="surface"/>. False on failure.</summary>
    Task<bool> AttachAsync(ICompositionOverlaySurface surface, DrawingRectangle viewport);

    void Navigate(string url);

    void Resize(DrawingRectangle viewport);

    void PostMessage(string json);
}

/// <summary>
/// Owns the mini window's surface and browser. All members run on the owning UI thread. Placement is
/// never activating (the window's own extended styles see to that); the page viewport is the window's
/// physical pixel size, square. The page runs at the frame-rate cap the controller was built with
/// (<c>frame-rate</c>, 30 or 60); a rate change replaces the whole window.
/// </summary>
/// <remarks>
/// Cursor dodge: with a dodge desktop and a poll timer, the cursor is read every
/// <see cref="MiniDodge.PollInterval"/> while the window is shown, and the window glides aside when it comes
/// near and back once it has gone (<see cref="MiniDodger"/>). Home is the bounds the owner last placed
/// (<see cref="Show"/>, <see cref="MoveTo"/>, <see cref="GlideTo"/>); each of those cancels a dodge. A dodge
/// never reaches the owner, so the saved position never changes for one.
/// </remarks>
public sealed class MiniSceneWindowController : IMiniSceneWindow
{
    private readonly Func<IMiniSceneSurface> _surfaceFactory;
    private readonly IMiniSceneBrowser _browser;
    private readonly Action<string>? _trace;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private IMiniSceneSurface? _surface;
    private WallpaperScene _scene;
    private readonly int _fps;
    private DrawingRectangle _viewport;
    // At most this many re-attaches after WebView2 process failures in a burst: a browser that keeps
    // dying gives up (traced) instead of looping.
    private const int MaxRecoveries = 2;

    /// <summary>
    /// A failure at least this long after the previous one starts a new burst: the recovery counter is
    /// reset, so a window that ran stably for this long gets its full budget back.
    /// </summary>
    public static readonly TimeSpan StableWindow = TimeSpan.FromMinutes(10);

    private readonly Func<DateTimeOffset> _clock;
    private readonly IFrameSource? _frames;
    // Where the window sits now (a glide frame included); null before it is placed or after a move threw.
    private Rectangle? _current;
    private MiniGlide? _glide;
    private int _recoveries;
    private DateTimeOffset? _lastFailureAt;
    private bool _attached;
    // The PAGE reported ready (after each navigation). Not IsReady: that one means the browser is attached.
    private bool _pageReady;
    private string? _pendingAlert;
    private bool _disposed;
    private readonly IMiniDodgeDesktop? _dodgeDesktop;
    private readonly Func<TimeSpan, Action, IDisposable>? _schedulePoll;
    private readonly MiniDodger _dodger = new();
    // Where the owner placed the window: the dodge's home. Null before the first show.
    private Rectangle? _home;
    private IDisposable? _poll;
    private bool _dodgeFailing;

    /// <param name="dodgeDesktop">Cursor and work area for the cursor dodge; null: no dodge.</param>
    /// <param name="schedulePoll">Runs a callback on this thread every interval until disposed; null: no dodge.</param>
    public MiniSceneWindowController(
        Func<IMiniSceneSurface> surfaceFactory, IMiniSceneBrowser browser, Action<string>? trace = null,
        Func<DateTimeOffset>? clock = null, IFrameSource? frames = null, int fps = 60,
        IMiniDodgeDesktop? dodgeDesktop = null, Func<TimeSpan, Action, IDisposable>? schedulePoll = null)
    {
        _fps = fps;
        _dodgeDesktop = dodgeDesktop;
        _schedulePoll = schedulePoll;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _frames = frames;
        _surfaceFactory = surfaceFactory;
        _browser = browser;
        _trace = trace;
        _browser.Ready += OnReady;
        _browser.Failed += OnFailed;
    }

    public bool IsReady => _attached && !_disposed;

    public event Action? PlacementLost;

    /// <summary>How long a <see cref="GlideTo"/> takes: short enough to never feel sluggish.</summary>
    public static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// The real window and WebView2 (its own <c>WebView2Mini</c> user-data folder), gliding on WPF render
    /// frames and polling the cursor for the dodge on a background-priority dispatcher timer. Construct on
    /// the UI STA.
    /// </summary>
    public static MiniSceneWindowController CreateProduction(Action<string>? trace = null, int fps = 60)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        return new(() => new Win32MiniSceneWindow(), new WebView2MiniSceneBrowser(), trace,
            frames: new CompositionTargetFrameSource(), fps: fps, dodgeDesktop: new Win32MiniDodgeDesktop(),
            schedulePoll: (interval, callback) =>
            {
                var timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => callback(), dispatcher);
                timer.Start();
                return new TimerStopper(timer);
            });
    }

    private sealed class TimerStopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    /// <summary>
    /// The page viewport for a window of <paramref name="bounds"/>: origin at zero and SQUARE (the
    /// shorter side), in physical pixels. Rasterization scale is pinned to 1 by the browser, so CSS
    /// pixels equal these physical pixels at any DPI.
    /// </summary>
    public static DrawingRectangle ViewportFor(Rectangle bounds)
    {
        var side = Math.Max(0, Math.Min(bounds.Width, bounds.Height));
        return new DrawingRectangle(0, 0, side, side);
    }

    public bool Show(WallpaperScene scene, Rectangle bounds)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A stale glide must never override the bounds this show places, even when the switch throws.
        StopGlide();
        if (_surface is not null)
        {
            SwitchScene(scene);
            MoveTo(bounds);
            return true;
        }

        _scene = scene;
        _viewport = ViewportFor(bounds);
        var surface = _surfaceFactory();
        if (!surface.TryCreate(bounds))
        {
            _trace?.Invoke("mini-window: create failed");
            surface.Dispose();
            return false;
        }

        _surface = surface;
        // The window is created hidden; Place is what shows it (topmost, no activation).
        if (!surface.Place(bounds))
        {
            _trace?.Invoke("mini-window: place failed");
            _surface = null;
            surface.Dispose();
            return false;
        }

        _current = bounds;
        _home = bounds;
        StartDodge();

        // Still usable, but other topmost windows coming to the foreground can then cover it.
        if (!surface.ReassertsTopmost) _trace?.Invoke("mini-window: foreground hook unavailable");
        _ = AttachAsync(surface);
        return true;
    }

    public bool SwitchScene(WallpaperScene scene)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scene == scene) return true;
        _scene = scene;
        // Before the browser is attached only the scene is recorded: the first navigation reads it.
        if (_attached) NavigateToScene();
        return true;
    }

    public void MoveTo(Rectangle bounds)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dodger.Cancel();
        _home = bounds;
        PlaceNow(bounds);
    }

    public void GlideTo(Rectangle bounds)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A dodge is dropped; the glide starts where the window is, aside or not.
        _dodger.Cancel();
        _home = bounds;
        GlideNow(bounds);
    }

    private void PlaceNow(Rectangle bounds)
    {
        StopGlide();
        _current = null; // unknown until Place returns: a throwing Place may have moved the window
        if (_surface is null) return;
        _surface.Place(bounds);
        _current = bounds;
        var viewport = ViewportFor(bounds);
        if (viewport == _viewport) return;
        _viewport = viewport;
        if (_attached) _browser.Resize(viewport);
    }

    private void GlideNow(Rectangle bounds)
    {
        if (_frames is null || _surface is null || _current is not { } current || current == bounds)
        {
            PlaceNow(bounds);
            return;
        }

        // Retargeting starts from the bounds last placed, so the window never jumps back or ahead.
        var running = _glide is not null;
        _glide = new MiniGlide(current, bounds, _clock(), GlideDuration);
        if (!running) _frames.Start(OnFrame);
    }

    public void ShowAlert(AlertShowRequest request)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The window is the whole canvas: lay the mosaic out on all of it, not on a monitor work area.
        Post(AlertLayerMessages.Show(request with
        {
            WorkAreaLeft = 0, WorkAreaTop = 0, WorkAreaWidth = 0, WorkAreaHeight = 0,
        }));
    }

    public void HideAlert()
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pendingAlert = null;
        if (_pageReady) _browser.PostMessage(AlertLayerMessages.Hide);
    }

    public void Dispose()
    {
        CheckAccess();
        if (_disposed) return;
        StopGlide();
        _poll?.Dispose();
        _poll = null;
        _disposed = true;
        _browser.Ready -= OnReady;
        _browser.Failed -= OnFailed;
        _browser.Dispose();
        _surface?.Dispose();
        _surface = null;
    }

    private async Task AttachAsync(IMiniSceneSurface surface)
    {
        bool attached;
        try
        {
            attached = await _browser.AttachAsync(surface, _viewport);
        }
        catch (Exception ex)
        {
            _trace?.Invoke($"mini-window: attach failed: {ex.Message}");
            return;
        }

        if (_disposed) return;
        if (!attached)
        {
            _trace?.Invoke("mini-window: attach failed");
            return;
        }

        _attached = true;
        NavigateToScene();
    }

    private void OnFailed(string reason)
    {
        if (_disposed) return;
        _attached = false;
        _pageReady = false;
        _trace?.Invoke($"mini-window: {reason}");
        if (_surface is not { } surface) return;
        var now = _clock();
        if (_lastFailureAt is { } last && now - last >= StableWindow) _recoveries = 0;
        _lastFailureAt = now;
        if (_recoveries >= MaxRecoveries)
        {
            _trace?.Invoke("mini-window: recovery exhausted, staying down");
            return;
        }

        _recoveries++;
        _trace?.Invoke($"mini-window: recovering attempt={_recoveries}");
        _ = AttachAsync(surface);
    }

    /// <summary>
    /// One glide frame: places the interpolated bounds (no viewport resize), and on the last frame lands on
    /// the target through <see cref="MoveTo"/>, which resizes the viewport once if the size changed. Runs
    /// inside the dispatcher, so nothing escapes: a failure is traced and the window is put on the target; if
    /// even that fails, <see cref="PlacementLost"/> is raised so the owner re-places the window later.
    /// </summary>
    private void OnFrame()
    {
        if (_disposed || _glide is not { } glide || _surface is not { } surface)
        {
            StopGlide();
            return;
        }

        try
        {
            var now = _clock();
            if (glide.IsComplete(now))
            {
                PlaceNow(glide.To);
                return;
            }

            var bounds = glide.At(now);
            _current = null;
            surface.Place(bounds);
            _current = bounds;
        }
        catch (Exception ex)
        {
            _trace?.Invoke($"mini-window: glide-frame-failed error={ex.GetType().Name}");
            try
            {
                PlaceNow(glide.To);
            }
            catch (Exception retry)
            {
                StopGlide();
                _trace?.Invoke($"mini-window: glide-land-failed error={retry.GetType().Name}");
                ReportPlacementLost();
            }
        }
    }

    private void StartDodge()
    {
        if (_dodgeDesktop is null || _schedulePoll is null || _poll is not null) return;
        _poll = _schedulePoll(MiniDodge.PollInterval, OnDodgePoll);
    }

    /// <summary>
    /// One cursor reading. Runs inside the dispatcher, so nothing escapes: a failure forgets the dodge, is
    /// traced once per failing run, and raises <see cref="PlacementLost"/> so the owner re-places the window
    /// at home.
    /// </summary>
    private void OnDodgePoll()
    {
        if (_disposed || _surface is null || _home is not { } home || _dodgeDesktop is not { } desktop) return;
        try
        {
            var step = _dodger.Update(home, desktop.ReadCursor(), desktop.WorkAreaOf(home), _clock());
            _dodgeFailing = false;
            switch (step.Action)
            {
                case MiniDodgeAction.Dodge when step.Choice is { } choice:
                    GlideNow(choice.Bounds);
                    _trace?.Invoke($"mini-window: dodge direction={MiniDodge.Name(choice.Direction)}");
                    break;
                case MiniDodgeAction.Return:
                    GlideNow(home);
                    _trace?.Invoke("mini-window: dodge return");
                    break;
            }
        }
        catch (Exception ex)
        {
            _dodger.Cancel();
            if (_dodgeFailing) return;
            _dodgeFailing = true;
            _trace?.Invoke($"mini-window: dodge-failed error={ex.GetType().Name}");
            ReportPlacementLost();
        }
    }

    private void ReportPlacementLost()
    {
        try
        {
            PlacementLost?.Invoke();
        }
        catch (Exception ex)
        {
            _trace?.Invoke($"mini-window: placement-lost-handler-failed error={ex.GetType().Name}");
        }
    }

    private void StopGlide()
    {
        if (_glide is null) return;
        _glide = null;
        _frames?.Stop();
    }

    private void NavigateToScene()
    {
        _pageReady = false;
        _browser.Navigate(WebViewAlertLayerController.SceneUrl(_scene, "mini", _fps));
    }

    private void Post(string json)
    {
        if (_pageReady) _browser.PostMessage(json);
        else _pendingAlert = json;
    }

    private void OnReady()
    {
        _pageReady = true;
        if (_pendingAlert is not { } pending) return;
        _pendingAlert = null;
        _browser.PostMessage(pending);
    }

    private void CheckAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Use the owning UI thread.");
    }
}
