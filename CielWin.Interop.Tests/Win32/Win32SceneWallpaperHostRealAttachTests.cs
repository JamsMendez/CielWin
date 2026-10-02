using System.Runtime.InteropServices;
using CielWin.Interop.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// First real exercise of <see cref="Win32SceneWallpaperHost"/> against an actual desktop session.
/// Attaches for real and asserts on observable Win32 state: <c>GetParent</c>, <c>IsWindowVisible</c>,
/// <c>GWL_STYLE</c>, z-order against <c>SHELLDLL_DefView</c>.
/// </summary>
/// <remarks>
/// <see cref="RequiresDesktopSessionFactAttribute"/> keeps every fact here skipped unless the
/// maintainer opted into desktop tests (see <see cref="DesktopGate"/>).
/// </remarks>
[Trait("Category", "RequiresDesktop")]
[Collection(RealDesktopCollection.Name)]
public sealed unsafe class Win32SceneWallpaperHostRealAttachTests
{
    private const uint WsChild = 0x40000000;
    private const uint WsExNoActivate = 0x08000000;

    [RequiresDesktopSessionFact]
    public void TryAttach_AttachesTheHostWindowBehindTheDesktopIcons()
    {
        using var host = new Win32SceneWallpaperHost();

        var attached = host.TryAttach();

        Assert.True(attached, "TryAttach should succeed on a real interactive desktop session.");
        Assert.NotEqual(0, host.Hwnd);
        Assert.NotEqual(0, host.TaskbarMessageHwnd);

        HWND taskbarReceiver = new(host.TaskbarMessageHwnd);
        Assert.Equal(HWND.Null, PInvoke.GetParent(taskbarReceiver));
        Assert.False(PInvoke.IsWindowVisible(taskbarReceiver).Value != 0);

        HWND hwnd = new(host.Hwnd);
        Assert.Equal(ResolveExpectedDesktopParent(), PInvoke.GetParent(hwnd));
        Assert.True(PInvoke.IsWindowVisible(hwnd).Value != 0);

        var style = unchecked((uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE));
        Assert.Equal(WsChild, style & WsChild);
    }

    /// <summary>
    /// On a monitor whose taskbar is not docked bottom, sizing the host to the work area would leave
    /// the scene short of the monitor's edge; the host must span the whole monitor (<c>rcMonitor</c>).
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TryAttach_SizesTheHostWindowToTheWholePrimaryMonitorNotJustTheWorkArea()
    {
        using var host = new Win32SceneWallpaperHost();

        Assert.True(host.TryAttach());

        HWND hwnd = new(host.Hwnd);
        Assert.True(PInvoke.GetWindowRect(hwnd, out RECT actual));

        HMONITOR primary = PInvoke.MonitorFromWindow(HWND.Null, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO mi = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Assert.True(PInvoke.GetMonitorInfo(primary, ref mi));

        Assert.Equal(mi.rcMonitor.left, actual.left);
        Assert.Equal(mi.rcMonitor.top, actual.top);
        Assert.Equal(mi.rcMonitor.right, actual.right);
        Assert.Equal(mi.rcMonitor.bottom, actual.bottom);
    }

    /// <summary>
    /// The host used to be a visible, activatable popup that took the foreground right after start,
    /// which made the covered-desktop check hold every alert. It must be non-activatable on first
    /// creation AND when recreated after an Explorer restart.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TryAttach_CreatesAndRecreatesTheHostWindowAsNonActivatable()
    {
        using var host = new Win32SceneWallpaperHost();

        Assert.True(host.TryAttach());
        AssertNonActivatable(new HWND(host.Hwnd));

        Assert.True(PInvoke.DestroyWindow(new HWND(host.Hwnd)));
        Assert.True(host.TryAttach());
        AssertNonActivatable(new HWND(host.Hwnd));
    }

    /// <summary>
    /// Links the detector's host exclusion to the REAL class name the host registers, so a change to
    /// the host's class-name format cannot silently stop the exclusion from matching while the
    /// detector's own unit test (which hardcodes the format) keeps passing.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TheLiveHostWindowClass_IsExcludedFromCoverage()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());

        Span<char> buffer = stackalloc char[256];
        int written;
        fixed (char* pBuffer = buffer)
        {
            written = PInvoke.GetClassName(new HWND(host.Hwnd), pBuffer, buffer.Length);
        }

        Assert.True(written > 0, "GetClassName should read the live host window's class.");
        string className = new(buffer[..written]);

        // exStyle 0 and isShellWindow false on purpose: they isolate the class-name rule. The live
        // window's real values could let the test pass through another exclusion path instead.
        Assert.True(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage(className, exStyle: 0, isShellWindow: false),
            $"The live host class '{className}' should be excluded from coverage.");
    }

    private static void AssertNonActivatable(HWND hwnd)
    {
        var exStyle = unchecked((uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE));
        Assert.Equal(WsExNoActivate, exStyle & WsExNoActivate);
        Assert.True(PInvoke.IsWindowVisible(hwnd).Value != 0);
        Assert.NotEqual(hwnd, PInvoke.GetForegroundWindow());
    }

    private static HWND ResolveExpectedDesktopParent()
    {
        HWND progman = PInvoke.FindWindow(null, "Program Manager");
        Assert.NotEqual(HWND.Null, progman);

        var exStyle = unchecked((uint)PInvoke.GetWindowLong(progman, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE));
        if (DesktopLayoutDetector.IsRaisedDesktop(exStyle))
        {
            return progman;
        }

        HWND ownerOfDefView = FindTopLevelOwningDefView();
        Assert.NotEqual(HWND.Null, ownerOfDefView);

        HWND workerW = PInvoke.FindWindowEx(HWND.Null, ownerOfDefView, "WorkerW", null);
        Assert.NotEqual(HWND.Null, workerW);
        return workerW;
    }

    private static HWND FindTopLevelOwningDefView()
    {
        HWND found = HWND.Null;
        PInvoke.EnumWindows(
            (hwnd, _) =>
            {
                HWND defView = PInvoke.FindWindowEx(hwnd, HWND.Null, "SHELLDLL_DefView", null);
                if (!defView.IsNull)
                {
                    found = hwnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// A second <c>TryAttach</c> (the keep-alive / <c>TaskbarCreated</c> retry path) reruns only the
    /// parent/z-order half: the same window and the same composition tree survive it.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TryAttach_CalledASecondTime_ReattachesWithoutRecreatingTheWindowOrTheCompositionTree()
    {
        using var host = new Win32SceneWallpaperHost();

        Assert.True(host.TryAttach());
        var firstHwnd = host.Hwnd;
        var firstGeneration = host.CompositionGeneration;

        Assert.True(host.TryAttach());

        Assert.Equal(firstHwnd, host.Hwnd);
        Assert.Equal(firstGeneration, host.CompositionGeneration);
    }

    /// <summary>
    /// Reproduces an Explorer restart destroying the host window together with its Progman parent --
    /// <c>DestroyWindow</c> from the SAME thread that created it (this test thread, via
    /// <see cref="Win32SceneWallpaperHost.TryAttach"/>) stands in for that, since only the owning
    /// thread may legally destroy a window. The retry must recover a brand-new, live, correctly
    /// parented window instead of failing forever against the dead handle.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TryAttach_AfterTheHostWindowIsDestroyed_RecreatesAndReparentsIt()
    {
        using var host = new Win32SceneWallpaperHost();

        Assert.True(host.TryAttach());
        var firstHwnd = host.Hwnd;

        Assert.True(PInvoke.DestroyWindow(new HWND(firstHwnd)));

        var reattached = host.TryAttach();

        Assert.True(reattached, "TryAttach should recover a destroyed host window instead of failing forever.");

        var secondHwnd = host.Hwnd;
        Assert.NotEqual(firstHwnd, secondHwnd);

        HWND newHwnd = new(secondHwnd);
        Assert.True(PInvoke.IsWindow(newHwnd), "The recreated window should be alive.");
        Assert.Equal(ResolveExpectedDesktopParent(), PInvoke.GetParent(newHwnd));
    }

    /// <summary>
    /// Explorer announces a restart by broadcasting <c>TaskbarCreated</c>; the hidden top-level
    /// receiver must answer it by re-running the attach against the same host. Sent synchronously
    /// from the owning thread, which stands in for the broadcast.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TaskbarCreated_AfterTheHostWindowIsDestroyed_ReattachesTheHost()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());
        var firstHwnd = host.Hwnd;
        Assert.True(PInvoke.DestroyWindow(new HWND(firstHwnd)));

        uint taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        Assert.NotEqual(0u, taskbarCreated);
        PInvoke.SendMessage(new HWND(host.TaskbarMessageHwnd), taskbarCreated, default, default);

        Assert.NotEqual(firstHwnd, host.Hwnd);
        Assert.True(PInvoke.IsWindow(new HWND(host.Hwnd)));
        Assert.Equal(ResolveExpectedDesktopParent(), PInvoke.GetParent(new HWND(host.Hwnd)));
    }

    /// <summary>
    /// The DirectComposition target/root visual tree is built the moment attach succeeds, with no
    /// D3D device or swapchain behind it.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TryAttach_BuildsACompositionTreeReadyForAnOverlayVisual()
    {
        using var host = new Win32SceneWallpaperHost();

        Assert.True(host.TryAttach());

        Assert.True(host.IsCompositionReady);
        Assert.True(host.CompositionGeneration >= 1);
    }

    /// <summary>
    /// The seam the WebView2 composition layer uses: add ONE overlay visual, get it back as
    /// <see cref="object"/>, commit, then remove it again, idempotently.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void AddCompositionOverlayVisual_ThenRemove_NeverThrows()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());

        var overlay = host.AddCompositionOverlayVisual();
        Assert.NotNull(overlay);

        Assert.Null(Record.Exception(host.CommitComposition));
        Assert.Null(Record.Exception(host.RemoveCompositionOverlayVisual));

        // Idempotent: removing twice (nothing left to remove the second time) still never throws.
        Assert.Null(Record.Exception(host.RemoveCompositionOverlayVisual));
    }

    /// <summary>
    /// The composition tree is window-bound: destroying the host window must rebuild it (bumping
    /// <see cref="Win32SceneWallpaperHost.CompositionGeneration"/>) on the SAME composition device.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void TryAttach_AfterTheHostWindowIsDestroyed_RebuildsTheCompositionTreeAndBumpsGeneration()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());

        var firstHwnd = host.Hwnd;
        var firstGeneration = host.CompositionGeneration;
        Assert.True(host.IsCompositionReady);

        Assert.True(PInvoke.DestroyWindow(new HWND(firstHwnd)));

        Assert.True(host.TryAttach());

        Assert.NotEqual(firstHwnd, host.Hwnd);
        Assert.True(host.IsCompositionReady);
        Assert.True(host.CompositionGeneration > firstGeneration);
    }

    /// <summary>
    /// A DirectComposition RCW minted on the thread that built the tree (this test thread, standing
    /// in for the wallpaper thread) throws <c>E_NOINTERFACE</c> when queried from a different
    /// apartment. <see cref="Win32SceneWallpaperHost.AddCompositionOverlayVisual"/> must succeed when
    /// called from a genuine STA thread instead (the WPF UI thread), never throw across the boundary,
    /// and commit cleanly.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void AddCompositionOverlayVisual_FromAnSTAThread_SucceedsWithoutCrossThreadFailure()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());

        object? overlay = null;
        Exception? threadException = null;

        var staThread = new Thread(() =>
        {
            try
            {
                overlay = host.AddCompositionOverlayVisual();
                host.CommitComposition();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join(TimeSpan.FromSeconds(10));

        Assert.Null(threadException);
        Assert.NotNull(overlay);
    }

    /// <summary>
    /// When a new wallpaper WorkerW is inserted directly after <c>SHELLDLL_DefView</c> (what the
    /// Windows wallpaper slideshow does, directly ABOVE the host), the next <see cref="Win32SceneWallpaperHost.TryAttach"/> puts the host back directly after
    /// DefView. This spawns its OWN window in that position, standing in for Explorer's new layer.
    /// </summary>
    /// <remarks>
    /// Raised-layout only: on the legacy WorkerW layout DefView lives under a different top-level
    /// window than the one the host attaches to, so the assumption does not apply there and
    /// <see cref="RequiresRaisedDesktopLayoutFactAttribute"/> SKIPS the fact.
    /// </remarks>
    [RequiresRaisedDesktopLayoutFact]
    public void TryAttach_WhenAWindowIsInsertedDirectlyAfterDefView_ReRaisesTheHostAboveIt()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());

        HWND hostParent = ResolveExpectedDesktopParent();
        HWND defView = PInvoke.FindWindowEx(hostParent, HWND.Null, "SHELLDLL_DefView", null);
        Assert.NotEqual(HWND.Null, defView);

        HWND hwnd = new(host.Hwnd);

        HWND simulatedSlideshowLayer = HWND.Null;
        try
        {
            simulatedSlideshowLayer = PInvoke.CreateWindowEx(
                0,
                "Static",
                "Simulated slideshow WorkerW",
                WINDOW_STYLE.WS_CHILD,
                0, 0, 1, 1,
                hostParent,
                null,
                PInvoke.GetModuleHandle((string?)null),
                null);
            Assert.NotEqual(HWND.Null, simulatedSlideshowLayer);

            Assert.True(PInvoke.SetWindowPos(
                simulatedSlideshowLayer,
                defView,
                0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                | SET_WINDOW_POS_FLAGS.SWP_NOSIZE));

            // The host is knocked out of place: something else now sits directly after DefView.
            Assert.NotEqual(hwnd, PInvoke.GetWindow(defView, GET_WINDOW_CMD.GW_HWNDNEXT));

            Assert.True(host.TryAttach());

            // Re-raised: the host is directly after DefView again.
            Assert.Equal(hwnd, PInvoke.GetWindow(defView, GET_WINDOW_CMD.GW_HWNDNEXT));
        }
        finally
        {
            if (simulatedSlideshowLayer != HWND.Null)
            {
                PInvoke.DestroyWindow(simulatedSlideshowLayer);
            }
        }
    }

    /// <summary>
    /// The raw DirectComposition pointer fields are touched by the UI and wallpaper threads alike; a
    /// concurrent <c>Marshal.Release</c> could zero a pointer another thread was about to pass to
    /// <c>Marshal.GetUniqueObjectForIUnknown</c> (a native use-after-free, not a catchable
    /// exception). The race is not deterministically reproducible, so this hammers the seam from
    /// another thread while the host window is destroyed/rebuilt. Passing proves the lock holds.
    /// </summary>
    [RequiresDesktopSessionFact]
    public void CompositionSeam_HammeredFromAnotherThreadWhileTheHostWindowIsRebuilt_NeverCrashesOrThrows()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.True(host.TryAttach());

        var stop = new CancellationTokenSource();
        var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        var overlayTask = Task.Run(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    host.AddCompositionOverlayVisual();
                    host.CommitComposition();
                    host.RemoveCompositionOverlayVisual();
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        for (var i = 0; i < 25 && exceptions.IsEmpty; i++)
        {
            HWND current = new(host.Hwnd);
            if (!current.IsNull)
            {
                PInvoke.DestroyWindow(current);
            }

            host.TryAttach();
        }

        stop.Cancel();
        overlayTask.Wait(TimeSpan.FromSeconds(10));

        Assert.Empty(exceptions);
        Assert.True(host.IsCompositionReady);
    }
}
