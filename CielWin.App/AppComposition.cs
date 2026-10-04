using CielWin.App.Alerts;
using CielWin.App.Composition;
using CielWin.App.Input;
using CielWin.App.Tray;
using CielWin.Interop;

namespace CielWin.App;

/// <summary>
/// The composition root: one scene surface (wallpaper or mini window), one local HTTP server, the
/// Alt+M / Alt+Shift+M hotkeys, the tray icon and the watch tick, wired over the seams in
/// <see cref="CompositionHost"/>. <see cref="ProductionComposition"/> supplies the real ones.
/// </summary>
/// <remarks>
/// <para>
/// Threading: everything runs on the UI thread except the two HTTP handlers, which run on the server
/// thread and only post work to the UI thread (the scene route must not block).
/// </para>
/// <para>
/// Persistence: every change (scene, mode, frame rate, mini position, alert sounds, imported sounds) goes through one
/// <see cref="SynchronizedSettingsStore"/>, which keeps the in-memory snapshot current. When the
/// settings file exists but could not be read (<see cref="SettingsLoadResult.CanSave"/> false), the
/// store's save is replaced by a trace for the whole session: saving would overwrite the user's real
/// file with defaults.
/// </para>
/// <para>
/// Hotkeys are registered in both modes, since the mode can be switched live from the tray; outside
/// <see cref="WallpaperMode.SceneMini"/> a press is traced and ignored.
/// </para>
/// </remarks>
public sealed class AppComposition : IDisposable
{
    /// <summary>The watch tick: wallpaper keep-alive, fullscreen pause, mini re-placement, alert timing.</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(400);

    private readonly CompositionHost _host;
    private readonly SynchronizedSettingsStore _store;
    private readonly AlertDriver _alerts;
    private readonly Action<string> _trace;
    private WallpaperMode _mode;
    private ISceneSurface? _surface;
    private IDisposable? _tick;
    private IHttpCommandServer? _server;
    private IHotkeyRegistrar? _hotkeys;
    private IDisposable? _tray;
    private volatile bool _disposed;
    private bool _coverCheckFailing;

    private AppComposition(SettingsLoadResult loaded, Action<Settings> save, CompositionHost host)
    {
        _host = host;
        _trace = host.Trace;
        _mode = loaded.Settings.WallpaperMode;
        _store = new SynchronizedSettingsStore(
            loaded.Settings,
            loaded.CanSave ? save : _ => _trace("settings-file save-skipped reason=unreadable"));
        _alerts = new AlertDriver(
            host.Clock, host.ReadPrimaryDisplay, host.CreateAlertSoundPlayer(ResolveAlertSound),
            () => _store.Current.AlertSoundsEnabled, _trace,
            TimeSpan.FromSeconds(loaded.Settings.AlertHoldMaxSeconds));
    }

    /// <summary>Builds and starts everything. Call on the UI thread.</summary>
    public static AppComposition Wire(SettingsLoadResult loaded, Action<Settings> save, CompositionHost host)
    {
        var composition = new AppComposition(loaded, save, host);
        composition.Start(loaded);
        return composition;
    }

    private void Start(SettingsLoadResult loaded)
    {
        if (!loaded.CanSave)
        {
            _trace("settings-file unreadable, changes this session will not be saved");
        }

        StartTray();
        StartHotkeys();
        // Posted, not run inline: WebView2 and the mini window need the dispatcher pumping.
        _host.OnUiThread(ActivateSurface);
        StartHttpServer(loaded.Settings);
        _tick = _host.Schedule(WatchInterval, OnTick);
    }

    private void StartTray()
    {
        try
        {
            // Clicks arrive as raw window messages; the work is posted so WebView2 always runs inside
            // a dispatcher operation (with its synchronization context), never inside the menu's own.
            _tray = _host.BuildTray(new TrayMenuController(
                () => _mode, mode => _host.OnUiThread(() => SelectMode(mode)),
                () => _store.Current.WallpaperScene, scene => _host.OnUiThread(() => SwitchScene(scene, "tray")),
                () => _store.Current.FrameRate, fps => _host.OnUiThread(() => SelectFrameRate(fps)),
                () => _store.Current.AlertSoundsEnabled, () => _host.OnUiThread(ToggleAlertSounds),
                kind => _store.Current.SoundFor(kind) is not null,
                kind => _host.OnUiThread(() => ImportAlertSound(kind)),
                kind => _host.OnUiThread(() => RemoveAlertSound(kind)),
                _host.Shutdown));
        }
        catch (Exception error)
        {
            _trace($"tray create-failed error={error.GetType().Name}");
        }
    }

    private void StartHotkeys()
    {
        try
        {
            _hotkeys = _host.CreateHotkeys();
            // Pressed arrives off the dispatcher (the keyboard hook's own thread, or a raw WM_HOTKEY
            // window message in the fallback): posted for the same reason as tray clicks.
            _hotkeys.Pressed += id => _host.OnUiThread(() => OnHotkey(id));
            Register(MiniPositionHotkeys.ClockwiseId, MiniPositionHotkeys.ClockwiseModifiers, "alt+m");
            Register(MiniPositionHotkeys.CounterClockwiseId, MiniPositionHotkeys.CounterClockwiseModifiers, "alt+shift+m");
        }
        catch (Exception error)
        {
            _trace($"hotkey create-failed error={error.GetType().Name}");
        }

        void Register(int id, HotkeyModifiers modifiers, string chord)
        {
            if (!_hotkeys!.Register(id, modifiers, MiniPositionHotkeys.VirtualKeyM))
            {
                _trace($"hotkey register-failed chord={chord} (owned by another app)");
            }
        }
    }

    private void StartHttpServer(Settings settings)
    {
        if (!settings.HttpServerEnabled)
        {
            _trace("http-server disabled (http-server = off): alerts and scene switching are off");
            return;
        }

        try
        {
            var token = _host.LoadHttpToken();
            if (token is null)
            {
                _trace("http-server token unavailable, server not started");
                return;
            }

            _server = _host.CreateHttpServer(new HttpServerOptions(
                settings.HttpServerPort, token, HandleAlert, HandleSceneSwitch, _trace, HandleAlertClear));
            _server.Start();
            // Start never throws: a port in use is reported by the server through the same trace.
            _trace($"http-server start requested port={settings.HttpServerPort}");
        }
        catch (Exception error)
        {
            _trace($"http-server start-failed error={error.GetType().Name}");
        }
    }

    private void ActivateSurface()
    {
        if (_disposed)
        {
            return;
        }

        var settings = _store.Current;
        var fps = settings.FrameRate;
        try
        {
            ISceneSurface surface = _mode == WallpaperMode.SceneMini
                ? new MiniSceneSurface(
                    _host.CreateMiniWindow(fps), _host.ReadPrimaryDisplay, settings.WallpaperScene,
                    settings.MiniPosition, _trace)
                : new WallpaperSceneSurface(
                    _host.CreateWallpaperHost(), _host.CreateWallpaperThread(),
                    (overlay, scene) => _host.CreateSceneLayer(overlay, scene, fps),
                    settings.WallpaperScene, _trace);
            _surface = surface;
            surface.Start();
        }
        catch (Exception error)
        {
            _trace($"surface create-failed mode={_mode} error={error.GetType().Name}");
        }
    }

    /// <summary>Tray: tears the current surface down and brings up the other one, live.</summary>
    private void SelectMode(WallpaperMode mode)
    {
        if (_disposed || mode == _mode)
        {
            return;
        }

        _mode = mode;
        ReplaceSurface();
        Persist(settings => settings with { WallpaperMode = mode });
        _trace($"mode switched mode={mode}");
    }

    /// <summary>
    /// Tray: the global frame-rate cap. The pages read it from their URL, so the current surface is
    /// rebuilt at the new rate, as a mode switch rebuilds it. The rate is recorded first (the rebuilt
    /// surface reads it) and saved at once, like the mode. Anything but 30 or 60, or the current rate,
    /// changes nothing.
    /// </summary>
    private void SelectFrameRate(int fps)
    {
        if (_disposed || !Settings.IsFrameRate(fps) || fps == _store.Current.FrameRate)
        {
            return;
        }

        Persist(settings => settings with { FrameRate = fps });
        ReplaceSurface();
        _trace($"frame-rate switched fps={fps}");
    }

    /// <summary>
    /// Tears the current surface down and brings up a new one for the current mode and settings. An
    /// alert inside its duration comes back on the new surface for its remaining time, never sounded
    /// again (<see cref="AlertDriver.SurfaceReplaced"/>).
    /// </summary>
    private void ReplaceSurface()
    {
        var previous = _surface;
        _surface = null;
        SafeDispose("surface", previous);
        _alerts.SurfaceReplaced();
        ActivateSurface();
    }

    /// <summary>Tray: flips whether new alerts play their sound, and persists it.</summary>
    private void ToggleAlertSounds()
    {
        if (_disposed)
        {
            return;
        }

        Persist(settings => settings with { AlertSoundsEnabled = !settings.AlertSoundsEnabled });
        _trace($"alert-sounds toggled enabled={_store.Current.AlertSoundsEnabled}");
    }

    /// <summary>The imported file <paramref name="kind"/> plays, or null when it has none.</summary>
    private string? ResolveAlertSound(AlertKind kind) =>
        _store.Current.SoundFor(kind) is { } name ? _host.AlertSoundLibrary.PathOf(name) : null;

    /// <summary>
    /// Tray: asks for a file, copies it into the sounds folder as <paramref name="kind"/>'s sound and
    /// persists its name. A cancelled dialog changes nothing; every failure is traced (reason code or
    /// exception TYPE, never a path) and leaves the previous sound in place.
    /// </summary>
    private void ImportAlertSound(AlertKind kind)
    {
        if (_disposed)
        {
            return;
        }

        var name = AlertSoundLibrary.KindName(kind);
        string? source;
        try
        {
            source = _host.PickAlertSoundFile(kind);
        }
        catch (Exception error)
        {
            _trace($"alert-sound pick-failed kind={name} error={error.GetType().Name}");
            return;
        }

        if (source is null)
        {
            return;
        }

        if (!AlertSoundLibrary.IsSupported(source))
        {
            _trace($"alert-sound import-rejected kind={name} reason=unsupported-format");
            return;
        }

        string fileName;
        try
        {
            fileName = _host.AlertSoundLibrary.Import(kind, source);
        }
        catch (Exception error)
        {
            _trace($"alert-sound import-failed kind={name} error={error.GetType().Name}");
            return;
        }

        Persist(settings => settings.WithSound(kind, fileName));
        _trace($"alert-sound imported kind={name}");
    }

    /// <summary>
    /// Tray: deletes <paramref name="kind"/>'s imported sound and clears its setting. The setting is
    /// cleared even when the delete fails (traced): the kind is silent either way, and the next
    /// import replaces the stray file.
    /// </summary>
    private void RemoveAlertSound(AlertKind kind)
    {
        if (_disposed)
        {
            return;
        }

        var name = AlertSoundLibrary.KindName(kind);
        try
        {
            _host.AlertSoundLibrary.Remove(kind);
        }
        catch (Exception error)
        {
            _trace($"alert-sound remove-failed kind={name} error={error.GetType().Name}");
        }

        Persist(settings => settings.WithSound(kind, null));
        _trace($"alert-sound removed kind={name}");
    }

    /// <summary>UI thread: switches the active surface's scene and persists it when it switched.</summary>
    private void SwitchScene(WallpaperScene scene, string source)
    {
        if (_disposed || scene == _store.Current.WallpaperScene)
        {
            return;
        }

        if (_surface is not { } surface)
        {
            _trace($"scene-switch refused source={source} reason=no-surface");
            return;
        }

        bool switched;
        try
        {
            switched = surface.SwitchScene(scene);
        }
        catch (Exception error)
        {
            _trace($"scene-switch switch-failed source={source} error={error.GetType().Name}");
            return;
        }

        if (!switched)
        {
            _trace($"scene-switch refused source={source} reason=surface-unavailable");
            return;
        }

        Persist(settings => settings with { WallpaperScene = scene });
    }

    /// <summary>HTTP server thread. Accepts a valid scene at once; the switch runs on the UI thread.</summary>
    private bool HandleSceneSwitch(string name)
    {
        if (_disposed || !TryParseScene(name, out var scene))
        {
            return false;
        }

        _host.OnUiThread(() => SwitchScene(scene, "http"));
        return true;
    }

    /// <summary>HTTP server thread. Queues the alert, then shows it from the UI thread without waiting for the tick.</summary>
    private string HandleAlert(string text)
    {
        var reply = _alerts.Accept(text);
        if (AlertHttpProtocol.IsAccepted(reply) && !_disposed)
        {
            _host.OnUiThread(() => UpdateAlerts(IsCovered()));
        }

        return reply;
    }

    /// <summary>HTTP server thread. Clears the alert (null: the held warning), then hides it from the UI thread at once.</summary>
    private string HandleAlertClear(int? id)
    {
        var reply = _alerts.Clear(id);
        if (!_disposed)
        {
            _host.OnUiThread(() => UpdateAlerts(IsCovered()));
        }

        return reply;
    }

    private void OnHotkey(int id)
    {
        if (_disposed || !MiniPositionHotkeys.TryStep(id, _store.Current.MiniPosition, out var next))
        {
            return;
        }

        if (_surface is not MiniSceneSurface mini)
        {
            _trace("mini-position ignored reason=not-mini-mode");
            return;
        }

        if (mini.TryMoveTo(next))
        {
            Persist(settings => settings with { MiniPosition = next });
        }
    }

    private void OnTick()
    {
        if (_disposed)
        {
            return;
        }

        var covered = IsCovered();
        try
        {
            _surface?.Tick(covered);
        }
        catch (Exception error)
        {
            _trace($"surface tick-failed error={error.GetType().Name}");
        }

        UpdateAlerts(covered);
    }

    private void UpdateAlerts(bool covered)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _alerts.Update(_surface, covered);
        }
        catch (Exception error)
        {
            _trace($"alert update-failed error={error.GetType().Name}");
        }
    }

    /// <summary>A failed check reads as "not covered"; only the first failure of a streak is traced.</summary>
    private bool IsCovered()
    {
        try
        {
            var covered = _host.IsPrimaryMonitorCovered();
            _coverCheckFailing = false;
            return covered;
        }
        catch (Exception error)
        {
            if (!_coverCheckFailing)
            {
                _trace($"cover-check-failed error={error.GetType().Name}");
            }

            _coverCheckFailing = true;
            return false;
        }
    }

    /// <summary>
    /// The change has already taken effect on screen; a failing save is traced, never thrown back
    /// into a tray click or a hotkey.
    /// </summary>
    private void Persist(Func<Settings, Settings> change)
    {
        try
        {
            _store.Update(change);
        }
        catch (Exception error)
        {
            _trace($"settings-file persist-failed error={error.GetType().Name}");
        }
    }

    /// <summary>
    /// The one place a validated lowercase scene name becomes a <see cref="WallpaperScene"/>. A closed
    /// switch, not <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>, which would accept
    /// numbers.
    /// </summary>
    private static bool TryParseScene(string name, out WallpaperScene scene)
    {
        switch (name)
        {
            case "processing":
                scene = WallpaperScene.Processing;
                return true;
            case "explorer":
                scene = WallpaperScene.Explorer;
                return true;
            case "idle":
                scene = WallpaperScene.Idle;
                return true;
            case "raphael":
                scene = WallpaperScene.Raphael;
                return true;
            default:
                scene = default;
                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SafeDispose("tick", _tick);
        SafeDispose("http-server", _server);
        SafeDispose("hotkeys", _hotkeys);
        SafeDispose("tray", _tray);
        SafeDispose("surface", _surface);
        _surface = null;
    }

    private void SafeDispose(string part, IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception error)
        {
            _trace($"dispose-failed part={part} error={error.GetType().Name}");
        }
    }
}
