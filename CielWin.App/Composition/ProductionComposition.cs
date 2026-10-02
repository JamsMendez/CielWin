using System.Runtime.InteropServices;
using System.Windows.Threading;
using CielWin.App.Alerts;
using CielWin.App.Tray;
using CielWin.App.Wallpaper;
using CielWin.Interop;
using CielWin.Interop.Win32;

namespace CielWin.App.Composition;

/// <summary>
/// Fills <see cref="CompositionHost"/> with the real desktop, WebView2, HTTP listener, hotkeys and
/// tray, reads settings.conf, and wires <see cref="AppComposition"/>. Call once, on the WPF UI thread.
/// </summary>
/// <remarks>
/// Not unit-tested as a whole: every piece it builds needs a live desktop. What it decides (settings
/// read, trace sink, startup failure trace) lives in <see cref="StartupSettings"/>,
/// <see cref="FileTrace"/> and <see cref="TraceStartupFailure{T}"/>, which are.
/// </remarks>
public static partial class ProductionComposition
{
    public static AppComposition Wire(Action shutdown)
    {
        var trace = new FileTrace(FileTrace.ResolveDefaultPath());
        return TraceStartupFailure(() => Wire(shutdown, trace), trace.Record);
    }

    /// <summary>
    /// Runs <paramref name="wire"/>; when it throws, traces the exception TYPE (never its message,
    /// which can hold a path) and rethrows it unchanged, so the startup crash keeps its semantics but
    /// leaves a line in trace.log. A throwing trace never replaces the original failure.
    /// </summary>
    internal static T TraceStartupFailure<T>(Func<T> wire, Action<string> trace)
    {
        try
        {
            return wire();
        }
        catch (Exception error)
        {
            try
            {
                trace($"startup wire-failed error={error.GetType().Name}");
            }
            catch
            {
            }

            throw;
        }
    }

    private static AppComposition Wire(Action shutdown, FileTrace trace)
    {
        var settingsPath = SettingsFile.ResolvePath();
        var loaded = StartupSettings.Load(settingsPath, trace.Record);
        var dispatcher = Dispatcher.CurrentDispatcher;

        var host = new CompositionHost
        {
            Trace = trace.Record,
            OnUiThread = work => dispatcher.BeginInvoke(work),
            Schedule = (interval, callback) =>
            {
                var timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => callback(), dispatcher);
                timer.Start();
                return new TimerStopper(timer);
            },
            IsPrimaryMonitorCovered = PrimaryMonitorFullscreenDetector.IsPrimaryMonitorCoveredByFullscreenWindow,
            ReadPrimaryDisplay = PrimaryMonitor.Read,
            CreateWallpaperHost = () => new Win32SceneWallpaperHost(),
            // MTA and pumping: the host window lives on this thread (see MtaActionThread).
            CreateWallpaperThread = () => new MtaWallpaperThread(new MtaActionThread(
                "CielWinSceneWallpaperHost",
                errorType => trace.Record($"scene-wallpaper-thread work-failed error={errorType}"))),
            CreateSceneLayer = (surface, scene) =>
                new WebViewSceneLayer(new WebViewAlertLayerController(surface, trace.Record, scene: scene)),
            CreateMiniWindow = () => MiniSceneWindowController.CreateProduction(trace.Record),
            LoadHttpToken = () => AlertHttpTokenFile.LoadOrCreate(trace.Record),
            CreateHttpServer = options => new LocalHttpCommandServer(
                options.Port, options.Token, options.HandleAlert, options.Diagnostic,
                handleWallpaperSceneSwitch: options.HandleSceneSwitch),
            CreateHotkeys = () => new Win32HotkeyRegistrar(
                errorType => trace.Record($"hotkey handler-failed error={errorType}")),
            BuildTray = controller => new TrayIconHost(controller, trace.Record),
            Shutdown = shutdown,
        };

        return AppComposition.Wire(
            loaded,
            settings => SettingsFile.Save(settingsPath, settings, type => trace.Record($"settings-file save-failed error={type}")),
            host,
            new SilentAlertSoundPlayer());
    }

    /// <summary>No sound ships with CielWin, so until one is imported every alert is silent.</summary>
    private sealed class SilentAlertSoundPlayer : IAlertSoundPlayer
    {
        public void Play(AlertKind kind)
        {
        }
    }

    private sealed class TimerStopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    private sealed class MtaWallpaperThread(MtaActionThread thread) : IWallpaperThread
    {
        public void Post(Action work) => thread.Post(work);

        public bool Invoke(Action work) => thread.Invoke(work);

        public void Dispose() => thread.Dispose();
    }

    private sealed class WebViewSceneLayer(WebViewAlertLayerController controller) : ISceneLayer
    {
        public void Preload() => controller.Preload();

        public void Start(AlertShowRequest request) => controller.Start(request);

        public void End() => controller.End();

        public void SwitchScene(WallpaperScene scene) => controller.SwitchScene(scene);

        public void SetScenePaused(bool paused) => controller.SetScenePaused(paused);

        public void Dispose() => controller.Dispose();
    }

    /// <summary>
    /// The primary monitor read fresh through <c>GetMonitorInfo</c> on every call (physical pixels in
    /// this per-monitor-aware process), so a moved or auto-hidden taskbar is seen at once.
    /// </summary>
    private static partial class PrimaryMonitor
    {
        private const uint MonitorDefaultToPrimary = 1;

        public static PrimaryDisplayInfo Read()
        {
            var monitor = MonitorFromWindow(0, MonitorDefaultToPrimary);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            {
                throw new InvalidOperationException("The primary monitor could not be read.");
            }

            return new PrimaryDisplayInfo(info.Monitor.ToRectangle(), info.Work.ToRectangle());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly Rectangle ToRectangle() => new(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public uint Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [LibraryImport("user32.dll")]
        private static partial nint MonitorFromWindow(nint hwnd, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    }
}
