using System.Diagnostics;
using System.Drawing;
using CielWin.Interop;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using CielWin.App.Wallpaper;
using Microsoft.Web.WebView2.Core;

namespace CielWin.App.Alerts;

/// <summary>
/// The permanently preloaded WebView2 composition layer that hosts the scene wallpaper page and
/// forwards alerts to it. Construct on the owning STA; call <see cref="Preload"/> once its
/// dispatcher is pumping, drive alerts with <see cref="Start"/>/<see cref="End"/>, switch the scene
/// live with <see cref="SwitchScene"/>, and pause drawing while the desktop is covered with
/// <see cref="SetScenePaused"/>.
/// </summary>
/// <remarks>
/// One controller is created at startup and kept alive, hidden until the page reports ready, for
/// the process's life; <see cref="Start"/>/<see cref="End"/> only show/hide the alert overlay
/// INSIDE the already-navigated scene page. <see cref="AlertLayerPreloadState"/> is the pure state
/// machine behind this (host identity/backoff/readiness/pending-show), unit-tested directly; this
/// class is the thin WebView2-specific shell around it -- real environment/controller/navigation
/// stay a hardware-only concern.
/// <para>
/// Every lifecycle event is traced through the optional <paramref name="trace"/> delegate --
/// <see cref="AlertLayerTrace"/> owns the exact wording and is unit-tested directly.
/// </para>
/// </remarks>
public sealed class WebViewAlertLayerController : IDisposable
{
    private readonly ICompositionOverlaySurface _host;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _poll;
    private readonly Action<string>? _trace;
    private readonly AlertLayerPreloadState _state;
    // Every scene page shares one virtual host mapping (see CreateAsync), so only the scene SEGMENT
    // of the navigated URL needs to change to switch scenes. That segment comes from SceneFolderName,
    // a closed switch over a compile-time enum, so raw text can never reach the Navigate URL as an
    // unvalidated scene segment.
    // MUTABLE: SwitchScene updates it live, after CreateAsync has already navigated once.
    // CreateAsync's own Navigate call reads THIS field, never the constructor's scene parameter, which
    // also makes a later recreate (TearDown from a host change, then Poll calling CreateAsync again)
    // navigate to whatever scene is CURRENT -- TearDown never touches this field.
    private WallpaperScene _currentScene;
    // The frame-rate cap every navigation carries, fixed for this controller's life: a rate change
    // rebuilds the whole layer (AppComposition), so it never changes under a live page.
    private readonly int _fps;
    // The DESIRED pause state of the scene page, kept here (not only posted) because a page that is
    // not ready yet, or is recreated/re-navigated later (Explorer restart, process failure, scene
    // switch), starts out running and must be told again once ready.
    private bool _scenePaused;
    // What the live page was last successfully told (a fresh page starts running = false). Kept apart
    // from the desired state so a FAILED post is retried by the caller's next SetScenePaused call
    // instead of being suppressed as "unchanged".
    private bool _scenePostedPaused;
    private CoreWebView2Environment? _environment;
    private CoreWebView2CompositionController? _controller;
    private int _generation;
    private nint _hwnd;
    private bool _disposed;
    private bool _creating;
    private bool _preloading;
    private bool _navigationCompleted;
    private bool _pageReportedReady;
    private int _epoch;

    // Set right before Navigate() so OnNavigationCompleted can report elapsed time since THAT call,
    // not since the whole creation started (environment/controller creation already has its own
    // separately-traced timings).
    private Stopwatch? _navigateStopwatch;

    // Tracks in-flight/abandoned navigation ids so the abort completion of a host Navigate-over-Navigate
    // is ignored regardless of event order (see AlertLayerNavigation).
    private readonly AlertLayerNavigation _navigation = new();

    /// <param name="host">The composition surface hosting the layer: the desktop wallpaper host or the mini window.</param>
    /// <param name="trace">Receives one line per lifecycle event; see <see cref="AlertLayerTrace"/>.</param>
    /// <param name="clock">Test seam for the backoff/pending-show clock.</param>
    /// <param name="scene">The scene to navigate to first.</param>
    /// <param name="fps">The frame-rate cap (<c>frame-rate</c>), 30 or 60; anything else is 60.</param>
    public WebViewAlertLayerController(ICompositionOverlaySurface host, Action<string>? trace = null,
        Func<DateTimeOffset>? clock = null, WallpaperScene scene = WallpaperScene.Processing,
        int fps = 60)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("A WPF UI STA is required.");
        _host = host;
        _trace = trace;
        _currentScene = scene;
        _fps = fps;
        _state = new AlertLayerPreloadState(clock);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => Poll(), _dispatcher);
    }

    /// <summary>
    /// Begins keeping one WebView2 controller alive PERMANENTLY: creates the environment/composition
    /// controller, adds the overlay visual, and navigates to the scene page, hidden until it is ready. The
    /// 250ms poll then keeps running for the process's life -- not just while an alert is active --
    /// watching for a host identity change (Explorer restart) or a lost composition surface, and
    /// recreating (with the existing exponential backoff) when either happens. Idempotent.
    /// </summary>
    public void Preload()
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_preloading) return;
        _preloading = true;
        _poll.Start();
        Poll();
    }

    /// <summary>
    /// Shows <paramref name="request"/>'s tiles, laid out in its grid with its gap -- or, while the
    /// page is not yet ready, remembers it as a pending show (see <see
    /// cref="AlertLayerPreloadState.RequestShow"/>).
    /// </summary>
    public void Start(AlertShowRequest request)
    {
        CheckAccess();
        if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
            throw new InvalidOperationException("A pumped WPF UI STA is required to start the alert layer.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Tiles.Count == 0)
            throw new ArgumentException("At least one tile is required.", nameof(request));
        if (request.Tiles.Any(tile => tile is not ("warning" or "failed")))
            throw new ArgumentOutOfRangeException(nameof(request), "Every tile must be 'warning' or 'failed'.");
        if (request.Columns <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Columns must be positive.");
        if (request.Rows <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Rows must be positive.");
        if (request.Gap < 0) throw new ArgumentOutOfRangeException(nameof(request), "Gap must not be negative.");
        if (request.DurationMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Duration must be positive.");
        _trace?.Invoke(AlertLayerTrace.Show(request));
        // Safety net: the queue must never lose a Start because production forgot to call Preload
        // first. Calling it here when it was already called is a no-op.
        Preload();
        // Ready while already visible still re-shows (a fresh Start must never be swallowed as a
        // no-op): AlertLayerPreloadState.RequestShow always returns a value while Ready, regardless
        // of Visible -- see its own tests.
        if (_state.RequestShow(request) is { } show) PostShow(show);
    }

    /// <summary>
    /// Switches the live scene without tearing down or recreating the WebView2 controller -- the HTTP
    /// route's whole point is a live switch with no restart. Requesting the CURRENT scene again is a
    /// no-op.
    /// </summary>
    /// <remarks>
    /// When the controller has not been created yet (the host is not attached, or <see
    /// cref="CreateAsync"/> has not run), only <see cref="_currentScene"/> is updated: the next
    /// <see cref="CreateAsync"/> call reads it directly, so no separate "pending scene" state is needed.
    /// </remarks>
    public void SwitchScene(WallpaperScene scene)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentScene == scene) return;

        _currentScene = scene;
        if (_controller is null) return;

        // Mirrors the Navigate step of CreateAsync: reset the ready-state flags the SAME way, so the
        // "ready" handshake runs again for the new page instead of treating this controller as already
        // ready for a page it has not actually loaded yet.
        _navigationCompleted = false;
        _pageReportedReady = false;
        // The new page loads without the alert on screen; requeue it for its remaining time so
        // TryMarkReady re-shows it once the new page is ready.
        _state.PageReloading();
        _navigateStopwatch = Stopwatch.StartNew();
        _state.NavigationStarted();
        _navigation.BeforeHostNavigate();
        _controller.CoreWebView2.Navigate(SceneUrl(_currentScene, fps: _fps));
    }

    /// <summary>The scene the controller navigates to next (and is showing, once ready).</summary>
    internal WallpaperScene CurrentScene => _currentScene;

    /// <summary>
    /// Pauses (<paramref name="paused"/> true) or resumes the scene page while a fullscreen window
    /// covers the desktop. Safe at any time: while the page is not ready the state is just remembered
    /// and <see cref="TryMarkReady"/> posts it once the page can receive it. A failed post propagates
    /// to the caller (which traces it) and is re-attempted by the next call with the same value.
    /// </summary>
    public void SetScenePaused(bool paused)
    {
        CheckAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scenePaused = paused;
        if (_navigationCompleted && _pageReportedReady && _scenePostedPaused != _scenePaused) PostScenePause();
    }

    private void PostScenePause()
    {
        if (_controller is null) return;
        _controller.CoreWebView2.PostWebMessageAsJson(
            _scenePaused ? AlertLayerMessages.Pause : AlertLayerMessages.Resume);
        _scenePostedPaused = _scenePaused;
    }

    public void End() => End("end");

    private void End(string reason)
    {
        CheckAccess();
        _trace?.Invoke(AlertLayerTrace.Hide());
        _state.Hide();
        if (_controller is null) return;
        try
        {
            // The page IS the wallpaper and stays visible permanently once ready: End tells the page
            // to hide its own alert overlay, it never hides the WebView2 layer itself.
            _controller.CoreWebView2.PostWebMessageAsJson(AlertLayerMessages.Hide);
        }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error($"end-{reason}", ex)); }
    }

    private void PostShow(AlertShowRequest request)
    {
        if (_controller is null) return;
        try
        {
            _controller.CoreWebView2.PostWebMessageAsJson(AlertLayerMessages.Show(request));
            _controller.IsVisible = true;
        }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("post-show", ex)); }
    }

    private void Poll()
    {
        if (!_preloading) return;
        try
        {
            var hwnd = _host.Hwnd;
            var generation = _host.CompositionGeneration;
            if (_state.HostChanged(hwnd, generation) && _controller is not null)
            {
                TearDown("host-changed");
            }
            if (_controller is not null && _state.NavigationTimedOut)
            {
                // A navigation that never completes leaves a live controller that is never ready and
                // that nothing else would replace: treat it like a process failure.
                _trace?.Invoke(AlertLayerTrace.NavigationTimeout(_navigateStopwatch?.ElapsedMilliseconds ?? 0));
                RecoverFromRuntimeFailure("navigation-timeout", dropEnvironment: true);
                return;
            }
            if (!_host.IsCompositionReady || hwnd == 0)
            {
                if (_controller is not null) TearDown("host-not-ready");
                return;
            }
            if (_controller is null && !_creating && _state.CanCreate)
                _ = CreateAsync(hwnd, generation);
            else if (_controller is not null)
                Resize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _trace?.Invoke(AlertLayerTrace.Error("poll", ex));
            _state.Failed();
            TearDown("poll-failed");
        }
    }

    private async Task CreateAsync(nint hwnd, int generation)
    {
        _creating = true;
        _navigationCompleted = false;
        _pageReportedReady = false;
        var epoch = _epoch;
        var stopwatch = Stopwatch.StartNew();
        _trace?.Invoke(AlertLayerTrace.CreateStart(hwnd, generation));
        CoreWebView2CompositionController? candidate = null;
        bool visualAdded = false;
        try
        {
            _environment ??= await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CielWin", "WebView2Scene"));
            _trace?.Invoke(AlertLayerTrace.EnvironmentReady(stopwatch.ElapsedMilliseconds));
            if (!StillCurrent(epoch, hwnd, generation)) return;
            var options = _environment.CreateCoreWebView2ControllerOptions();
            options.DefaultBackgroundColor = Color.Transparent;
            candidate = await _environment.CreateCoreWebView2CompositionControllerAsync(hwnd, options);
            _trace?.Invoke(AlertLayerTrace.ControllerReady(stopwatch.ElapsedMilliseconds));
            if (!StillCurrent(epoch, hwnd, generation)) return;
            candidate.DefaultBackgroundColor = Color.Transparent;
            var visual = _host.AddCompositionOverlayVisual();
            if (visual is null)
            {
                _trace?.Invoke(AlertLayerTrace.NoOverlayVisual());
                _state.Failed();
                return;
            }
            visualAdded = true;
            if (!StillCurrent(epoch, hwnd, generation)) return;
            candidate.RootVisualTarget = visual;
            _host.CommitComposition();
            // The scene host serves the WHOLE Wallpaper\Web folder (the CielScenes submodule), since every
            // scene page loads Wallpaper\Web\shared\... siblings, and adapts each page to WebView2 on the
            // way out (CSP, mini transparency) -- see SceneWebServer.
            SceneWebServer.Attach(candidate.CoreWebView2, SceneWebServer.WebRoot);
            candidate.CoreWebView2.WebMessageReceived += OnMessage;
            candidate.CoreWebView2.NavigationStarting += OnNavigationStarting;
            candidate.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            candidate.CoreWebView2.ProcessFailed += OnProcessFailed;
            _controller = candidate;
            candidate = null;
            visualAdded = false;
            _hwnd = hwnd;
            _generation = generation;
            Resize();
            // Preloaded and hidden -- show/hide is driven entirely by Start/End posting messages
            // once the page reports itself ready (see OnNavigationCompleted/OnMessage below).
            _controller.IsVisible = false;
            _state.Created();
            _navigateStopwatch = Stopwatch.StartNew();
            // The page loads idle and is driven by show/hide messages once it is ready. The scene
            // segment comes from the closed enum -> folder-name mapping (SceneUrl), so it can never
            // inject an unexpected path segment or query into this URL.
            _state.NavigationStarted();
            _navigation.BeforeHostNavigate();
            _controller.CoreWebView2.Navigate(SceneUrl(_currentScene, fps: _fps));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView alert creation failed: {ex}");
            _trace?.Invoke(AlertLayerTrace.Error("create", ex));
            _state.Failed();
            TearDown("create-failed", dropEnvironment: true);
        }
        finally
        {
            try { candidate?.Close(); }
            catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("create-cleanup-controller", ex)); }
            if (visualAdded)
            {
                try { _host.RemoveCompositionOverlayVisual(); _host.CommitComposition(); }
                catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("create-cleanup-visual", ex)); }
            }
            _creating = false;
        }
    }

    /// <summary>Whether an in-flight <see cref="CreateAsync"/> call is still the one that started it -- a host change or Dispose during an await bumps <see cref="_epoch"/> and invalidates it.</summary>
    private bool StillCurrent(int epoch, nint hwnd, int generation) =>
        epoch == _epoch && !_disposed && _preloading &&
        _host.IsCompositionReady && _host.Hwnd == hwnd && _host.CompositionGeneration == generation;

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        _navigation.Started(args.NavigationId);
        _trace?.Invoke(AlertLayerTrace.NavigationStarting(args.NavigationId));
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            if (_navigation.CompletedIsSuperseded(args.NavigationId))
            {
                _trace?.Invoke(AlertLayerTrace.NavigationSuperseded(args.NavigationId, args.WebErrorStatus));
                return;
            }
            _trace?.Invoke(AlertLayerTrace.NavigationCompleted(
                args.NavigationId, args.IsSuccess, args.WebErrorStatus, _navigateStopwatch?.ElapsedMilliseconds ?? 0));
            _state.NavigationFinished();
            if (!args.IsSuccess)
            {
                RecoverFromRuntimeFailure("navigation-failed", dropEnvironment: false);
                return;
            }
            _navigationCompleted = true;
            TryMarkReady();
        }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("navigation-completed", ex)); }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args)
    {
        try
        {
            _trace?.Invoke(AlertLayerTrace.ProcessFailed(args.ProcessFailedKind, args.Reason));
            if (!MiniProcessFailurePolicy.RequiresRecovery(args.ProcessFailedKind))
            {
                // WebView2 restarts its own helpers; tearing the page down would only burn the budget.
                _trace?.Invoke(AlertLayerTrace.ProcessFailureIgnored(args.ProcessFailedKind));
                return;
            }
            RecoverFromRuntimeFailure("process-failed", dropEnvironment: true);
        }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("process-failed", ex)); }
    }

    /// <summary>
    /// A live layer failed (dead process, hung or failed navigation): tear it down and let the poll
    /// recreate it with backoff, while the state's bounded budget allows. Once spent the layer stays
    /// down (traced) instead of recreating a renderer that keeps dying.
    /// </summary>
    private void RecoverFromRuntimeFailure(string reason, bool dropEnvironment)
    {
        if (!_state.RuntimeFailed()) _trace?.Invoke(AlertLayerTrace.RecoveryExhausted());
        TearDown(reason, dropEnvironment);
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var message = args.TryGetWebMessageAsString();
            if (message == "ready")
            {
                _pageReportedReady = true;
                TryMarkReady();
            }
            else if (message == "done")
            {
                _trace?.Invoke(AlertLayerTrace.Done());
                // The QUEUE owns ending the alert (the composition root calls End() once it
                // advances) -- this only reflects the page's own state. The page's "done" never hides
                // the WebView2 layer: it is the wallpaper, not a one-off alert.
                _state.PageDone();
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("message", ex)); }
    }

    /// <summary>Ready = navigation completed AND the page's own "ready" message received (feature doc). Applies any show that arrived while not yet ready, with its REMAINING duration.</summary>
    private void TryMarkReady()
    {
        if (!_navigationCompleted || !_pageReportedReady) return;
        _state.MarkReady();
        _trace?.Invoke(AlertLayerTrace.PageReady());
        // Becoming ready shows the layer on its own, with no Start ever required -- the page IS the
        // wallpaper. Runs again after every recreation (Explorer restart, process failure), so the
        // layer becomes visible again once the new controller is ready.
        if (_controller is not null)
        {
            _controller.IsVisible = true;
        }
        // A fresh page always starts running: tell it again if the desktop is covered right now.
        _scenePostedPaused = false;
        if (_scenePaused)
        {
            try { PostScenePause(); }
            catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("post-scene-pause", ex)); }
        }
        if (_state.ApplyPendingShowIfDue() is { } pending)
        {
            _trace?.Invoke(AlertLayerTrace.PendingShowApplied(pending));
            PostShow(pending);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int left, top, right, bottom;
    }

    private void Resize()
    {
        if (_controller is not null && GetClientRect(_hwnd, out var rect))
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>
    /// Tears down the controller/environment -- unlike the old per-alert model, this is NOT what
    /// <see cref="End"/> does. It only ever runs on <see cref="Dispose"/>, a host identity change, a
    /// lost composition surface, or a real creation/process/navigation failure. <paramref
    /// name="dropEnvironment"/> is true ONLY for a real creation/process failure (feature doc: "Keep
    /// _environment for the process lifetime unless creation failed/process failed") -- an ordinary
    /// host change reuses the same environment for the next controller.
    /// </summary>
    private void TearDown(string reason, bool dropEnvironment = false)
    {
        _state.ControllerLost();
        ++_epoch;
        _navigationCompleted = false;
        _pageReportedReady = false;
        _navigation.Clear();
        if (dropEnvironment) _environment = null;
        var old = _controller;
        _controller = null;
        if (old is null) return;
        _trace?.Invoke(AlertLayerTrace.Close(reason));
        try { old.CoreWebView2.WebMessageReceived -= OnMessage; }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("close-unsubscribe-message", ex)); }
        try { old.CoreWebView2.NavigationStarting -= OnNavigationStarting; }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("close-unsubscribe-navigation-starting", ex)); }
        try { old.CoreWebView2.NavigationCompleted -= OnNavigationCompleted; }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("close-unsubscribe-navigation", ex)); }
        try { old.CoreWebView2.ProcessFailed -= OnProcessFailed; }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("close-unsubscribe-process-failed", ex)); }
        try { old.Close(); }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("close-controller", ex)); }
        try { _host.RemoveCompositionOverlayVisual(); _host.CommitComposition(); }
        catch (Exception ex) { Debug.WriteLine(ex); _trace?.Invoke(AlertLayerTrace.Error("close-visual", ex)); }
    }

    private void CheckAccess()
    {
        if (!_dispatcher.CheckAccess()) throw new InvalidOperationException("Use the owning UI dispatcher.");
    }

    /// <summary>
    /// The ONLY place a <see cref="WallpaperScene"/> value becomes a folder name -- a closed switch
    /// over a compile-time-fixed enum, so no settings text (or anything else) can ever reach the
    /// Navigate URL as an unvalidated scene segment. An undefined enum value falls back to
    /// <c>"processing"</c>. Internal so tests can exercise this pure mapping directly.
    /// </summary>
    internal static string SceneFolderName(WallpaperScene scene) => scene switch
    {
        WallpaperScene.Explorer => "explorer",
        WallpaperScene.Idle => "idle",
        WallpaperScene.Raphael => "raphael",
        _ => "processing",
    };

    /// <summary>
    /// The exact URL <see cref="CreateAsync"/> and <see cref="SwitchScene"/> both navigate to for
    /// <paramref name="scene"/>. <paramref name="fps"/> is the global frame-rate cap (the scene pages
    /// parse the <c>fps</c> query param, see each scene's <c>js/render-loop.js</c>): only 30 or 60 ever reach
    /// the URL, anything else falls back to the default 60. <paramref name="variant"/> selects a page
    /// variant such as <c>mini</c>. Internal so tests can exercise it directly.
    /// </summary>
    internal static string SceneUrl(WallpaperScene scene, string? variant = null, int fps = 60) =>
        $"https://{SceneWebServer.HostName}/{SceneFolderName(scene)}/index.html?fps={(Settings.IsFrameRate(fps) ? fps : Settings.Default.FrameRate)}"
        + (variant is null ? "" : $"&variant={variant}");

    public void Dispose()
    {
        CheckAccess();
        if (_disposed) return;
        _poll.Stop();
        _state.Hide();
        TearDown("disposed", dropEnvironment: true);
        _disposed = true;
    }
}
