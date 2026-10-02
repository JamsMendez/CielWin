using System.IO;
using CielWin.App.Alerts;
using CielWin.App.Composition;
using CielWin.App.Tray;
using CielWin.App.Wallpaper;
using CielWin.Interop;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// Every outside-world seam of <see cref="AppComposition"/> as an in-memory fake, so the wiring tests
/// run without a desktop, a WebView2 runtime, a port or a settings file.
/// </summary>
internal sealed class CompositionHarness
{
    public static readonly PrimaryDisplayInfo DefaultDisplay = new(
        new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));

    public List<string> Trace { get; } = [];
    public List<Settings> Saves { get; } = [];
    public List<FakeWallpaperHost> Hosts { get; } = [];
    public List<FakeWallpaperThread> Threads { get; } = [];
    public List<FakeSceneLayer> Layers { get; } = [];
    public List<FakeMiniWindow> Minis { get; } = [];
    public List<FakeHttpServer> Servers { get; } = [];
    public List<HttpServerOptions> ServerRequests { get; } = [];
    public FakeHotkeys Hotkeys { get; } = new();
    public FakeTray? Tray { get; private set; }
    public FakeAlertSoundPlayer Sounds { get; } = new();

    /// <summary>A per-harness temporary folder, created only when a test imports; tests that import delete it.</summary>
    public string SoundsDirectory { get; } = Path.Combine(Path.GetTempPath(), $"cielwin-harness-sounds-{Guid.NewGuid():N}");

    /// <summary>What the next "Import ... sound" dialog returns; null is a cancelled dialog.</summary>
    public string? PickedSoundFile { get; set; }
    public List<AlertKind> SoundPicks { get; } = [];
    public bool SoundPickThrows { get; set; }
    public ManualClock Clock { get; } = new();
    public List<Action> UiQueue { get; } = [];

    /// <summary>When true, UI work is queued until <see cref="RunUi"/>; otherwise it runs inline.</summary>
    public bool DeferUi { get; set; }

    public bool Covered { get; set; }
    public Func<bool>? CoveredOverride { get; set; }
    public PrimaryDisplayInfo Display { get; set; } = DefaultDisplay;
    public Func<PrimaryDisplayInfo>? DisplayOverride { get; set; }
    public string? Token { get; set; } = "token-value";
    public int TokenLoads { get; private set; }
    public bool ServerFactoryThrows { get; set; }
    public bool HoldWallpaperThread { get; set; }
    public bool HostAttachResult { get; set; } = true;
    public bool MiniShowResult { get; set; } = true;
    public bool MiniReady { get; set; } = true;
    public bool LayerFactoryThrows { get; set; }
    public int ShutdownCalls { get; private set; }
    public TimeSpan? ScheduledInterval { get; private set; }
    public bool TickStopped { get; private set; }
    private Action? _tick;

    public FakeWallpaperHost Host => Hosts[^1];
    public FakeWallpaperThread Thread => Threads[^1];
    public FakeSceneLayer Layer => Layers[^1];
    public FakeMiniWindow Mini => Minis[^1];
    public FakeHttpServer Server => Servers[^1];

    public void Tick() => (_tick ?? throw new InvalidOperationException("No tick scheduled."))();

    public void RunUi()
    {
        while (UiQueue.Count > 0)
        {
            var work = UiQueue[0];
            UiQueue.RemoveAt(0);
            work();
        }
    }

    public AppComposition Wire(Settings settings, SettingsLoadStatus status = SettingsLoadStatus.Loaded) =>
        AppComposition.Wire(new SettingsLoadResult(settings, status), Saves.Add, Build());

    public CompositionHost Build() => new()
    {
        Trace = Trace.Add,
        OnUiThread = work =>
        {
            if (DeferUi) UiQueue.Add(work);
            else work();
        },
        Schedule = (interval, callback) =>
        {
            ScheduledInterval = interval;
            _tick = callback;
            return new Callback(() => TickStopped = true);
        },
        IsPrimaryMonitorCovered = () => CoveredOverride?.Invoke() ?? Covered,
        ReadPrimaryDisplay = () => DisplayOverride?.Invoke() ?? Display,
        CreateWallpaperHost = () =>
        {
            var host = new FakeWallpaperHost(this);
            Hosts.Add(host);
            return host;
        },
        CreateWallpaperThread = () =>
        {
            var thread = new FakeWallpaperThread(this);
            Threads.Add(thread);
            return thread;
        },
        CreateSceneLayer = (surface, scene) =>
        {
            if (LayerFactoryThrows) throw new InvalidOperationException("no WebView2 runtime");
            var layer = new FakeSceneLayer(surface, scene);
            Layers.Add(layer);
            return layer;
        },
        CreateMiniWindow = () =>
        {
            var mini = new FakeMiniWindow(this);
            Minis.Add(mini);
            return mini;
        },
        LoadHttpToken = () =>
        {
            TokenLoads++;
            return Token;
        },
        CreateHttpServer = options =>
        {
            ServerRequests.Add(options);
            if (ServerFactoryThrows) throw new InvalidOperationException("cannot bind");
            var server = new FakeHttpServer(options);
            Servers.Add(server);
            return server;
        },
        CreateHotkeys = () => Hotkeys,
        BuildTray = controller => Tray = new FakeTray(controller),
        Shutdown = () => ShutdownCalls++,
        AlertSoundLibrary = new AlertSoundLibrary(SoundsDirectory),
        PickAlertSoundFile = kind =>
        {
            SoundPicks.Add(kind);
            if (SoundPickThrows) throw new InvalidOperationException("no desktop");
            return PickedSoundFile;
        },
        CreateAlertSoundPlayer = resolve =>
        {
            Sounds.Resolve = resolve;
            return Sounds;
        },
        Clock = Clock,
    };

    public sealed class Callback(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

internal sealed class FakeWallpaperHost(CompositionHarness harness) : ISceneWallpaperHost
{
    public int AttachCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public bool IsCompositionReady { get; set; } = true;
    public nint Hwnd => 42;
    public int CompositionGeneration => 1;

    public bool TryAttach()
    {
        AttachCalls++;
        return harness.HostAttachResult;
    }

    public object? AddCompositionOverlayVisual() => new object();
    public void RemoveCompositionOverlayVisual() { }
    public void CommitComposition() { }
    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeWallpaperThread(CompositionHarness harness) : IWallpaperThread
{
    private readonly Queue<Action> _pending = new();
    public int DisposeCalls { get; private set; }
    public int PendingCount => _pending.Count;

    public void Post(Action work)
    {
        if (harness.HoldWallpaperThread) _pending.Enqueue(work);
        else work();
    }

    public bool Invoke(Action work)
    {
        work();
        return true;
    }

    public void RunPending()
    {
        while (_pending.Count > 0) _pending.Dequeue()();
    }

    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeSceneLayer(ICompositionOverlaySurface surface, WallpaperScene scene) : ISceneLayer
{
    public ICompositionOverlaySurface Surface { get; } = surface;
    public WallpaperScene InitialScene { get; } = scene;
    public int Preloads { get; private set; }
    public List<AlertShowRequest> Starts { get; } = [];
    public int Ends { get; private set; }
    public List<WallpaperScene> Switches { get; } = [];
    public List<bool> Paused { get; } = [];
    public int DisposeCalls { get; private set; }
    public int StartFailuresLeft { get; set; }
    public int PauseFailuresLeft { get; set; }

    public void Preload() => Preloads++;

    public void Start(AlertShowRequest request)
    {
        if (StartFailuresLeft > 0)
        {
            StartFailuresLeft--;
            throw new InvalidOperationException("start failed");
        }

        Starts.Add(request);
    }

    public bool ThrowOnSwitch { get; set; }

    public void End() => Ends++;

    public void SwitchScene(WallpaperScene scene)
    {
        if (ThrowOnSwitch) throw new InvalidOperationException("switch failed");
        Switches.Add(scene);
    }

    public void SetScenePaused(bool paused)
    {
        if (PauseFailuresLeft > 0)
        {
            PauseFailuresLeft--;
            throw new InvalidOperationException("post failed");
        }

        Paused.Add(paused);
    }

    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeMiniWindow(CompositionHarness harness) : IMiniSceneWindow
{
    public List<(WallpaperScene Scene, Rectangle Bounds)> Shows { get; } = [];
    public List<WallpaperScene> Switches { get; } = [];
    public List<Rectangle> Moves { get; } = [];
    public List<AlertShowRequest> Alerts { get; } = [];
    public int Hides { get; private set; }
    public int DisposeCalls { get; private set; }
    public bool SwitchResult { get; set; } = true;
    public bool ThrowOnMove { get; set; }

    /// <summary>Records the move (the native window did move) and then throws, as a partial move would.</summary>
    public bool ThrowAfterMove { get; set; }
    public bool IsReady => harness.MiniReady;

    public bool Show(WallpaperScene scene, Rectangle bounds)
    {
        Shows.Add((scene, bounds));
        return harness.MiniShowResult;
    }

    public bool SwitchScene(WallpaperScene scene)
    {
        Switches.Add(scene);
        return SwitchResult;
    }

    public void MoveTo(Rectangle bounds)
    {
        if (ThrowOnMove) throw new InvalidOperationException("move failed");
        Moves.Add(bounds);
        if (ThrowAfterMove) throw new InvalidOperationException("move failed after moving");
    }

    public void ShowAlert(AlertShowRequest request) => Alerts.Add(request);
    public void HideAlert() => Hides++;
    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeHttpServer(HttpServerOptions options) : IHttpCommandServer
{
    public HttpServerOptions Options { get; } = options;
    public int StartCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public bool ThrowOnDispose { get; set; }
    public void Start() => StartCalls++;

    public void Dispose()
    {
        DisposeCalls++;
        if (ThrowOnDispose) throw new InvalidOperationException("dispose failed");
    }
}

internal sealed class FakeHotkeys : IHotkeyRegistrar
{
    public List<(int Id, HotkeyModifiers Modifiers, uint VirtualKey)> Registrations { get; } = [];
    public bool RegisterResult { get; set; } = true;
    public int DisposeCalls { get; private set; }
    public event Action<int>? Pressed;

    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        Registrations.Add((id, modifiers, virtualKey));
        return RegisterResult;
    }

    public void Press(int id) => Pressed?.Invoke(id);
    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeTray(TrayMenuController controller) : IDisposable
{
    public TrayMenuController Controller { get; } = controller;
    public int DisposeCalls { get; private set; }
    public void Dispose() => DisposeCalls++;
}

internal sealed class FakeAlertSoundPlayer : IAlertSoundPlayer
{
    public List<AlertKind> Played { get; } = [];

    /// <summary>The file each play resolved to through the composition's lookup (null: that kind has no sound).</summary>
    public List<string?> PlayedFiles { get; } = [];
    public bool Throws { get; set; }
    public Func<AlertKind, string?>? Resolve { get; set; }

    public void Play(AlertKind kind)
    {
        if (Throws) throw new InvalidOperationException("no audio device");
        Played.Add(kind);
        PlayedFiles.Add(Resolve?.Invoke(kind));
    }
}

internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}
