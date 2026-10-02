using CielWin.App.Alerts;
using CielWin.App.Tray;
using CielWin.App.Wallpaper;
using CielWin.Interop;

namespace CielWin.App.Composition;

/// <summary>
/// Everything <see cref="AppComposition"/> needs from the outside world, as delegates and small
/// interfaces. Production fills it with the real desktop, WebView2, HTTP listener and tray
/// (<see cref="ProductionComposition"/>); tests fill it with in-memory fakes.
/// </summary>
/// <remarks>
/// Every factory is called on the UI thread. The wallpaper host is created there but attached,
/// re-attached and disposed on the <see cref="IWallpaperThread"/> it comes with.
/// </remarks>
public sealed class CompositionHost
{
    /// <summary>One line per lifecycle event. Must be safe from any thread and never throw.</summary>
    public required Action<string> Trace { get; init; }

    /// <summary>Posts work to the UI thread (the owner of the tray, the mini window and WebView2).</summary>
    public required Action<Action> OnUiThread { get; init; }

    /// <summary>Runs a callback on the UI thread every interval until the returned handle is disposed.</summary>
    public required Func<TimeSpan, Action, IDisposable> Schedule { get; init; }

    /// <summary>Whether a fullscreen window covers the primary monitor right now.</summary>
    public required Func<bool> IsPrimaryMonitorCovered { get; init; }

    /// <summary>The primary monitor's bounds and work area, in physical pixels, read live.</summary>
    public required Func<PrimaryDisplayInfo> ReadPrimaryDisplay { get; init; }

    public required Func<ISceneWallpaperHost> CreateWallpaperHost { get; init; }

    /// <summary>The thread that owns one wallpaper host: attach, re-attach and dispose run on it.</summary>
    public required Func<IWallpaperThread> CreateWallpaperThread { get; init; }

    /// <summary>The WebView2 scene layer rendering into the wallpaper host's overlay visual.</summary>
    public required Func<ICompositionOverlaySurface, WallpaperScene, ISceneLayer> CreateSceneLayer { get; init; }

    public required Func<IMiniSceneWindow> CreateMiniWindow { get; init; }

    /// <summary>The HTTP bearer token, or <see langword="null"/> when it cannot be read or created.</summary>
    public required Func<string?> LoadHttpToken { get; init; }

    public required Func<HttpServerOptions, IHttpCommandServer> CreateHttpServer { get; init; }

    public required Func<IHotkeyRegistrar> CreateHotkeys { get; init; }

    public required Func<TrayMenuController, IDisposable> BuildTray { get; init; }

    /// <summary>Ends the process (the WPF application's shutdown).</summary>
    public required Action Shutdown { get; init; }

    /// <summary>Where the alert queue reads time from.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>The primary monitor, physical pixels.</summary>
public readonly record struct PrimaryDisplayInfo(Rectangle Bounds, Rectangle WorkArea);

/// <summary>What the one local HTTP server is built with: both routes always present.</summary>
/// <param name="HandleAlert">Translated alert command text in, reply text out (server thread).</param>
/// <param name="HandleSceneSwitch">Validated lowercase scene name in; non-blocking (server thread).</param>
public sealed record HttpServerOptions(
    int Port, string Token, Func<string, string> HandleAlert, Func<string, bool> HandleSceneSwitch,
    Action<string> Diagnostic);

/// <summary>
/// The scene wallpaper's WebView2 layer, as the composition drives it. Production wraps
/// <see cref="WebViewAlertLayerController"/>; every member runs on the UI thread.
/// </summary>
public interface ISceneLayer : IDisposable
{
    void Preload();

    void Start(AlertShowRequest request);

    void End();

    void SwitchScene(WallpaperScene scene);

    void SetScenePaused(bool paused);
}

/// <summary>The wallpaper host's owning thread (production: <see cref="MtaActionThread"/>).</summary>
public interface IWallpaperThread : IDisposable
{
    /// <summary>Queues work and returns at once.</summary>
    void Post(Action work);

    /// <summary>Runs work and waits for it; false when it was not run or timed out.</summary>
    bool Invoke(Action work);
}
