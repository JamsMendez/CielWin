using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop.Win32;

/// <summary>
/// Attach-only wallpaper host: a window parented behind the desktop icons (Progman/WorkerW) that
/// shows one DirectComposition overlay visual -- the WebView2 composition layer's render target -- and
/// nothing else. It has no D3D device and no swapchain of its own; the composition tree is just a
/// root visual bound to the window.
/// </summary>
/// <remarks>
/// <para>
/// <c>TryAttach</c> is idempotent by design: the first successful call creates the host window, the
/// hidden top-level <c>TaskbarCreated</c> receiver and the composition tree; every later call re-runs
/// the Progman/WorkerW/DefView discovery and parent/z-order half against the window that already
/// exists. The composition device is kept for the life of this object once created.
/// </para>
/// <para>
/// Explorer restart: the host window is a <c>WS_CHILD</c> of Progman and is destroyed together with
/// it, leaving <see cref="_hwnd"/> a dead handle. <c>TryAttach</c> detects that with
/// <c>PInvoke.IsWindow</c>, drops the now-dead window-bound composition tree, and creates and attaches
/// a brand-new window with a brand-new tree on the SAME composition device. The registered
/// <c>TaskbarCreated</c> message, received by the hidden receiver, triggers exactly that re-attach,
/// which is why the owning thread must pump messages (see <see cref="MtaActionThread"/>).
/// </para>
/// <para>
/// The window is created <c>WS_POPUP</c>, not <c>WS_CHILD</c>: <c>CreateWindowEx</c> refuses
/// <c>WS_CHILD</c> with no parent handle yet (error 1406, <c>ERROR_TLW_WITH_WSCHILD</c>). It only
/// becomes <c>WS_CHILD</c> once <c>SetParent</c> has given it a real parent.
/// </para>
/// <para>
/// <b>Composition threading</b>: DirectComposition objects are documented free-threaded
/// (<c>IAgileObject</c>), but their RCWs are apartment-bound -- an RCW minted on this class's own
/// thread throws <c>E_NOINTERFACE</c> when queried from the WPF UI (STA) thread that calls
/// <see cref="AddCompositionOverlayVisual"/>/<see cref="RemoveCompositionOverlayVisual"/>/
/// <see cref="CommitComposition"/>. So every composition object that might be touched from a
/// different thread than the one that created it (device, target, root visual, overlay visual) is
/// reduced to a raw <c>IUnknown</c> pointer the moment it is created, never cached as an RCW;
/// <see cref="CallerContextDComp{T}"/> mints a fresh RCW over that pointer for whichever thread is
/// calling right now, used for exactly one call and disposed before returning.
/// </para>
/// <para>
/// Those raw pointers are also touched by those same threads with no synchronization of their own, so
/// <see cref="_compositionLock"/> guards every read-then-use and write/release of them, held for the
/// whole operation (the short DComp calls made while holding it are fine -- free-threaded, fast).
/// Always the innermost lock: never held while calling back into a caller or another lock.
/// </para>
/// </remarks>
public sealed unsafe class Win32SceneWallpaperHost : ISceneWallpaperHost
{
    /// <summary>Class-name prefix; each instance appends <c>-{guid}</c>. Internal so
    /// <see cref="PrimaryMonitorFullscreenDetector"/> can recognise the host as the wallpaper itself.</summary>
    internal const string ClassName = "CielWinSceneWallpaperHost";

    private readonly string _className = $"{ClassName}-{Guid.NewGuid():N}";

    private WNDPROC? _wndProcDelegate;
    private uint _taskbarCreatedMessage;
    private bool _classRegistered;

    private HWND _hwnd;
    private HWND _taskbarMessageHwnd;
    private HINSTANCE _hInstance;
    private SafeHandle? _hInstanceSafe;

    // Raw IUnknown pointers, not cached RCWs -- see the class remarks "Composition threading".
    // _dcompDevicePtr is built once (EnsureCompositionDevice) and kept for the life of this object.
    // _dcompTargetPtr/_dcompRootVisualPtr are window-bound: dropped and rebuilt together in
    // RebuildCompositionTarget. _dcompOverlayVisualPtr is the ONE overlay visual a caller has added
    // via AddCompositionOverlayVisual, if any -- it belongs to the window-bound tree above it and is
    // dropped (never carried over) on a rebuild.
    private nint _dcompDevicePtr;
    private nint _dcompTargetPtr;
    private nint _dcompRootVisualPtr;
    private nint _dcompOverlayVisualPtr;
    private readonly object _compositionLock = new(); // guards the four pointers above.

    private bool _disposed;

    /// <summary>
    /// The real native handle, once <see cref="TryAttach"/> has created the window. Public (and a
    /// <c>nint</c>, never a CsWin32 type) because the WebView2 composition layer needs it to create a
    /// <c>CoreWebView2CompositionController</c> from <c>CielWin.App</c>, an assembly this one does not
    /// grant <c>InternalsVisibleTo</c>; CsWin32's generated Win32 types are internal to this assembly,
    /// so a public member returning one would not compile.
    /// </summary>
    public nint Hwnd => (nint)_hwnd.Value;

    /// <summary>Hidden top-level message receiver that survives after the visible host becomes a child window.</summary>
    internal nint TaskbarMessageHwnd => (nint)_taskbarMessageHwnd.Value;

    public bool TryAttach()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            if (_taskbarCreatedMessage == 0)
            {
                _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
                if (_taskbarCreatedMessage == 0)
                {
                    return false;
                }
            }

            if (_hwnd.IsNull)
            {
                return CreateAndAttach() && EnsureTaskbarMessageWindow();
            }

            // A non-null _hwnd that is no longer a real window means Explorer destroyed it (it was
            // WS_CHILD of Progman and died with it) -- the stale handle would otherwise make every
            // AttachToDesktop call below fail forever. Checked before EnsureTaskbarMessageWindow/
            // AttachToDesktop, which both assume _hwnd is at least a live window.
            if (!PInvoke.IsWindow(_hwnd))
            {
                return RecreateDestroyedHostWindow() && EnsureTaskbarMessageWindow();
            }

            if (!EnsureTaskbarMessageWindow())
            {
                return false;
            }

            if (!AttachToDesktop(_hwnd))
            {
                return false;
            }

            return EnsureCompositionTree(_hwnd);
        }
        catch
        {
            // Interface contract: TryAttach never throws, it reports failure. A partially attached
            // window from a failed first attempt is left in place rather than torn down -- Dispose()
            // is the only place that tears down, so a caller that retries TryAttach later does not
            // lose whatever succeeded so far.
            return false;
        }
    }

    /// <summary>
    /// True once the composition device AND a window-bound target/root visual tree both exist for the
    /// CURRENT host window -- the earliest point at which <see cref="AddCompositionOverlayVisual"/>
    /// can succeed.
    /// </summary>
    public bool IsCompositionReady
    {
        get
        {
            lock (_compositionLock)
            {
                return _dcompDevicePtr != 0 && _dcompTargetPtr != 0 && _dcompRootVisualPtr != 0;
            }
        }
    }

    /// <summary>
    /// Bumped every time the window-bound composition target/visual tree is (re)built by
    /// <see cref="RebuildCompositionTarget"/> -- once after the first successful attach, and again
    /// whenever the host window is recreated (Explorer restart). A caller holding an overlay visual
    /// from <see cref="AddCompositionOverlayVisual"/> must treat a changed generation as "that
    /// visual's tree is gone" (<see cref="RebuildCompositionTarget"/> already dropped it) and add a
    /// fresh one.
    /// </summary>
    public int CompositionGeneration { get; private set; }

    /// <summary>
    /// Adds ONE DirectComposition visual as a child of the root visual, commits, and returns it typed
    /// as <see cref="object"/> so a caller in another assembly (the WebView2 composition layer) can
    /// assign it straight to <c>CoreWebView2CompositionController.RootVisualTarget</c> -- itself typed
    /// <see cref="object"/> for exactly this reason -- without this assembly ever exposing a
    /// DirectComposition type across the boundary.
    /// </summary>
    /// <remarks>
    /// At most one overlay visual exists at a time: a second call replaces the first, exactly like
    /// this method's own <c>Remove</c> counterpart. Returns <see langword="null"/> when
    /// <see cref="IsCompositionReady"/> is false or on any DirectComposition failure -- a caller must
    /// treat a null return as "not ready yet, try again", never as an exception to catch. Safe to call
    /// from a thread other than the one that built the composition tree -- see the class remarks
    /// "Composition threading" and <see cref="CallerContextDComp{T}"/>.
    /// </remarks>
    public object? AddCompositionOverlayVisual()
    {
        lock (_compositionLock)
        {
            return AddCompositionOverlayVisualUnlocked();
        }
    }

    /// <summary>Always called while holding <see cref="_compositionLock"/>.</summary>
    private object? AddCompositionOverlayVisualUnlocked()
    {
        if (!IsCompositionReady)
        {
            return null;
        }

        try
        {
            RemoveOverlayVisualCore();

            using var device = new CallerContextDComp<IDCompositionDevice>(_dcompDevicePtr);
            using var root = new CallerContextDComp<IDCompositionVisual>(_dcompRootVisualPtr);

            device.Value.CreateVisual(out IDCompositionVisual overlay);
            root.Value.AddVisual(overlay, true, null);
            device.Value.Commit();

            // Tracked as a raw pointer (see the class remarks) so RemoveCompositionOverlayVisual and
            // a later rebuild can find and release it, even from a different thread than this call.
            // The AddRef inside GetIUnknownForObject is what keeps the object alive once the RCW
            // returned here (which the caller owns from this point on) is eventually released.
            _dcompOverlayVisualPtr = Marshal.GetIUnknownForObject(overlay);
            return overlay;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the overlay visual added by <see cref="AddCompositionOverlayVisual"/>, if any, and
    /// commits. Idempotent and never throws -- safe to call when nothing was ever added, when the
    /// composition tree is gone (a rebuild already dropped it), or from a different thread than the
    /// one that added it.
    /// </summary>
    public void RemoveCompositionOverlayVisual()
    {
        lock (_compositionLock)
        {
            RemoveCompositionOverlayVisualUnlocked();
        }
    }

    /// <summary>Always called while holding <see cref="_compositionLock"/>.</summary>
    private void RemoveCompositionOverlayVisualUnlocked()
    {
        try
        {
            RemoveOverlayVisualCore();
        }
        catch
        {
            // A DComp failure must never propagate into caller code -- see the class remarks.
        }
    }

    /// <summary>
    /// Commits pending DirectComposition changes -- a caller that sets
    /// <c>CoreWebView2CompositionController.RootVisualTarget</c> to the visual returned by
    /// <see cref="AddCompositionOverlayVisual"/> must call this afterwards or the change never
    /// reaches the screen. A no-op, never throwing, when no composition device exists yet.
    /// </summary>
    public void CommitComposition()
    {
        lock (_compositionLock)
        {
            CommitCompositionUnlocked();
        }
    }

    /// <summary>Always called while holding <see cref="_compositionLock"/>.</summary>
    private void CommitCompositionUnlocked()
    {
        if (_dcompDevicePtr == 0)
        {
            return;
        }

        try
        {
            using var device = new CallerContextDComp<IDCompositionDevice>(_dcompDevicePtr);
            device.Value.Commit();
        }
        catch
        {
            // A DComp failure must never propagate into caller code -- see the class remarks.
        }
    }

    private void RemoveOverlayVisualCore()
    {
        if (_dcompOverlayVisualPtr == 0)
        {
            return;
        }

        if (_dcompDevicePtr != 0 && _dcompRootVisualPtr != 0)
        {
            using var device = new CallerContextDComp<IDCompositionDevice>(_dcompDevicePtr);
            using var root = new CallerContextDComp<IDCompositionVisual>(_dcompRootVisualPtr);
            using var overlay = new CallerContextDComp<IDCompositionVisual>(_dcompOverlayVisualPtr);
            root.Value.RemoveVisual(overlay.Value);
            device.Value.Commit();
        }

        ReleaseRawPointer(ref _dcompOverlayVisualPtr);
    }

    /// <summary>
    /// Mints a COM RCW for <paramref name="ptr"/> in the CALLING thread's context, and releases it
    /// (via <see cref="ReleaseComObject"/>) when this wrapper is disposed -- never cached, never
    /// reused across a call. See the class remarks "Composition threading" for why: an RCW minted on
    /// one thread cannot be queried from another for one of these interfaces (<c>E_NOINTERFACE</c>),
    /// even though the underlying object is free-threaded.
    /// </summary>
    private readonly struct CallerContextDComp<T> : IDisposable
        where T : class
    {
        public T Value { get; }

        public CallerContextDComp(nint ptr)
        {
            Value = (T)Marshal.GetUniqueObjectForIUnknown(ptr);
        }

        public void Dispose() => ReleaseComObject(Value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_compositionLock)
        {
            ReleaseWindowBoundCompositionPointers();
            ReleaseRawPointer(ref _dcompDevicePtr);
        }

        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }

        if (!_taskbarMessageHwnd.IsNull)
        {
            PInvoke.DestroyWindow(_taskbarMessageHwnd);
            _taskbarMessageHwnd = default;
        }

        if (_classRegistered)
        {
            PInvoke.UnregisterClass(_className, _hInstanceSafe!);
            _classRegistered = false;
            _hInstanceSafe = null;
            _hInstance = default;
        }
    }

    /// <summary>First-ever attach: creates the window, attaches it, then builds the composition tree.</summary>
    private bool CreateAndAttach()
    {
        var childHwnd = CreateHostWindow();
        if (childHwnd.IsNull)
        {
            return false;
        }

        _hwnd = childHwnd;

        if (!AttachToDesktop(childHwnd))
        {
            PInvoke.DestroyWindow(childHwnd);
            _hwnd = default;
            return false;
        }

        return EnsureCompositionTree(childHwnd);
    }

    /// <summary>
    /// Recovers from the host window being destroyed out from under this process -- Explorer
    /// restarting tears down its Progman parent, and the host is a <c>WS_CHILD</c> of it, so it dies
    /// too. The window-bound composition tree built for it is dead along with it, but the composition
    /// device is not, so only the tree is dropped; a fresh window is created, attached, and given a
    /// fresh tree on the SAME device.
    /// </summary>
    private bool RecreateDestroyedHostWindow()
    {
        lock (_compositionLock)
        {
            ReleaseWindowBoundCompositionPointers();
            ReleaseRawPointer(ref _dcompOverlayVisualPtr);
        }

        HWND newHwnd = CreateHostWindow();
        if (newHwnd.IsNull)
        {
            // _hwnd is left as the dead handle: the next TryAttach still sees !IsWindow(_hwnd) and
            // retries window creation from here again.
            return false;
        }

        _hwnd = newHwnd;

        if (!AttachToDesktop(newHwnd))
        {
            // The new window is alive even though attaching it failed -- left in place (not
            // destroyed) so the NEXT TryAttach takes the ordinary "hwnd already alive" path and
            // retries AttachToDesktop against it.
            return false;
        }

        return EnsureCompositionTree(newHwnd);
    }

    /// <summary>
    /// The Progman/WorkerW/DefView discovery, parent and z-order dance, shared between the first
    /// attach and every <c>TaskbarCreated</c> re-attach.
    /// </summary>
    private bool AttachToDesktop(HWND childHwnd)
    {
        HWND progman = PInvoke.FindWindow(null, "Program Manager");
        if (progman.IsNull)
        {
            return false;
        }

        // Plain (non-Ptr) GetWindowLong, not GetWindowLongPtr -- the Ptr variant is not generatable
        // for an AnyCPU target (PInvoke005), and GWL_EXSTYLE/GWL_STYLE are always 32-bit values
        // regardless of pointer width.
        var exStyle = unchecked((uint)PInvoke.GetWindowLong(progman, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE));
        var raisedDesktop = DesktopLayoutDetector.IsRaisedDesktop(exStyle);

        // Resolved WITHOUT sending the spawn message below -- on the raised-desktop layout host is
        // Progman itself, which always exists, and on the legacy layout a WorkerW created by an
        // earlier attach is still there to be found. Only a genuinely first-ever attach (or one after
        // Explorer destroyed the legacy WorkerW) needs the message, so trying this first is what lets
        // the fast path below skip it entirely in steady state.
        HWND host = ResolveHost(progman, raisedDesktop);

        // The fast path. `!host.IsNull` guards the one edge case that resolving BEFORE the spawn
        // message opens up -- on a legacy-layout machine that has never attached, ResolveHost returns
        // HWND.Null because the WorkerW does not exist yet, and GetParent(childHwnd) is also
        // HWND.Null on a freshly created window; without this guard the two nulls would compare equal
        // and this would report success having attached nothing.
        //
        // Checked, and returned from, BEFORE the 0x052C message farther down: that message is only
        // ever needed to make Explorer (re)create a worker window, and in the already-attached steady
        // state nothing needs creating. Sending it on every keep-alive tick would be an unresearched
        // action against Explorer for a path that never needs one.
        if (!host.IsNull && PInvoke.GetParent(childHwnd) == host)
        {
            return EnsureDirectlyAfterDefView(childHwnd, host);
        }

        // Single message, exactly once. The old two-message form (0xD,1) then (0xD,0) deletes the
        // new WorkerW instead of creating a durable one.
        nuint sendResult;
        PInvoke.SendMessageTimeout(
            progman,
            0x052C,
            0xD,
            0x1,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL,
            1000,
            &sendResult);

        // Re-resolved: on the legacy layout the message above may have just made Explorer create the
        // WorkerW that a first attach needs, which the pre-message resolve necessarily missed. The
        // raised-desktop layout's host is Progman itself and never depended on this message; the
        // re-resolve there just repeats a cheap, side-effect-free lookup.
        host = ResolveHost(progman, raisedDesktop);
        if (host.IsNull)
        {
            return false;
        }

        HWND existingParent = PInvoke.GetParent(childHwnd);
        if (existingParent == host)
        {
            return EnsureDirectlyAfterDefView(childHwnd, host);
        }

        PInvoke.SetParent(childHwnd, host);

        var newStyle = unchecked((int)((uint)WINDOW_STYLE.WS_CHILD | (uint)WINDOW_STYLE.WS_VISIBLE));
        PInvoke.SetWindowLong(childHwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, newStyle);
        if (!PInvoke.SetWindowPos(
            childHwnd, HWND.Null, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED))
        {
            return false;
        }

        var appliedStyle = unchecked((uint)PInvoke.GetWindowLong(childHwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE));
        if ((appliedStyle & (uint)WINDOW_STYLE.WS_CHILD) == 0)
        {
            return false;
        }

        if (PInvoke.GetParent(childHwnd) != host)
        {
            return false;
        }

        // HWND_BOTTOM (1): a pseudo-handle CsWin32 does not project as a named constant, used as the
        // raw literal.
        HWND hwndBottom = new(new IntPtr(1));
        HWND defView = PInvoke.FindWindowEx(host, HWND.Null, "SHELLDLL_DefView", null);
        if (!PInvoke.SetWindowPos(
            childHwnd,
            defView.IsNull ? hwndBottom : defView,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW))
        {
            return false;
        }

        PInvoke.ShowWindow(childHwnd, SHOW_WINDOW_CMD.SW_SHOWNA);
        return true;
    }

    /// <summary>
    /// Finds the same host <see cref="AttachToDesktop"/> has always targeted, WITHOUT the
    /// <c>0x052C</c> spawn message: Progman itself on the raised-desktop layout (always exists), or
    /// the legacy layout's WorkerW sibling of DefView's owner (exists once something has attached
    /// before). Returns <see cref="HWND.Null"/> when that WorkerW does not exist yet -- a caller that
    /// needs one created still has to send the message and resolve again.
    /// </summary>
    private static HWND ResolveHost(HWND progman, bool raisedDesktop)
    {
        if (raisedDesktop)
        {
            // WorkerW is located for parity with the measured sequence but Progman is the actual
            // parent on this layout.
            _ = PInvoke.FindWindowEx(progman, HWND.Null, "WorkerW", null);
            return progman;
        }

        HWND ownerOfDefView = FindTopLevelOwningDefView();
        if (ownerOfDefView.IsNull)
        {
            return HWND.Null;
        }

        return PInvoke.FindWindowEx(HWND.Null, ownerOfDefView, "WorkerW", null);
    }

    /// <summary>
    /// The parent matching <paramref name="host"/> alone is necessary but not sufficient for
    /// <see cref="AttachToDesktop"/> to report success: the Windows wallpaper slideshow creates a NEW
    /// wallpaper WorkerW, inserts it directly after <c>SHELLDLL_DefView</c> (i.e. directly ABOVE the
    /// host), then destroys the old one, and an early return on the parent match alone would keep even
    /// a re-attach from ever noticing. The host must also still sit directly after DefView, and if it
    /// does not, only the z-order is re-applied here -- no re-parent, no style change, since both of
    /// those already hold.
    /// </summary>
    /// <remarks>
    /// When there is no DefView under <paramref name="host"/> at all -- the legacy WorkerW layout,
    /// where DefView lives under a different top-level owner entirely (see
    /// <see cref="FindTopLevelOwningDefView"/>), never under the WorkerW host itself -- there is
    /// nothing here to compare the order against, so the "parent already matches" signal is trusted.
    /// </remarks>
    private static bool EnsureDirectlyAfterDefView(HWND childHwnd, HWND host)
    {
        HWND defView = PInvoke.FindWindowEx(host, HWND.Null, "SHELLDLL_DefView", null);
        if (defView.IsNull)
        {
            return true;
        }

        if (PInvoke.GetWindow(defView, GET_WINDOW_CMD.GW_HWNDNEXT) == childHwnd)
        {
            // Already exactly where the keep-alive tick wants us: a true no-op steady state, so no
            // SetWindowPos call happens here -- the whole reason this fast path exists is for that
            // steady state to cost nothing.
            return true;
        }

        // Explorer's slideshow replaced the wallpaper WorkerW/DefView sibling with a fresh one
        // inserted directly after DefView, i.e. above us. The parent is still right, so only the
        // z-order needs to be re-applied.
        return PInvoke.SetWindowPos(
            childHwnd,
            defView,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    /// <summary>
    /// Ensures the composition device exists and a window-bound tree is built for
    /// <paramref name="hwnd"/>; a no-op returning <see langword="true"/> once both are already ready.
    /// </summary>
    private bool EnsureCompositionTree(HWND hwnd) =>
        IsCompositionReady || (EnsureCompositionDevice() && RebuildCompositionTarget(hwnd));

    /// <summary>
    /// Creates <see cref="_dcompDevicePtr"/> once -- a no-op returning <see langword="true"/> when it
    /// already exists. Reduced to a raw pointer immediately (see the class remarks "Composition
    /// threading") rather than kept as a field.
    /// </summary>
    /// <remarks>
    /// A null DXGI device is the documented way to get a composition device that only composes visuals
    /// (no surfaces), which is all a WebView2 visual target needs.
    /// </remarks>
    private bool EnsureCompositionDevice()
    {
        lock (_compositionLock)
        {
            return EnsureCompositionDeviceUnlocked();
        }
    }

    /// <summary>Always called while holding <see cref="_compositionLock"/>.</summary>
    private bool EnsureCompositionDeviceUnlocked()
    {
        if (_dcompDevicePtr != 0)
        {
            return true;
        }

        IDCompositionDevice? dcompDevice = null;
        try
        {
            PInvoke.DCompositionCreateDevice(null!, out dcompDevice);
            if (dcompDevice is null)
            {
                return false;
            }

            _dcompDevicePtr = Marshal.GetIUnknownForObject(dcompDevice);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(dcompDevice);
        }
    }

    /// <summary>
    /// Builds (or rebuilds, after the host window was recreated) the DirectComposition target and
    /// visual tree for <paramref name="hwnd"/> -- an empty root visual bound to the window via
    /// <see cref="IDCompositionTarget"/>, committed. A caller adds its own overlay visual to it
    /// afterwards, via <see cref="AddCompositionOverlayVisual"/>. Window-bound -- rebuilt fresh every
    /// time the window itself is fresh; <see cref="_dcompDevicePtr"/> is not. Bumps
    /// <see cref="CompositionGeneration"/> and drops any tracked overlay visual, since it belonged to
    /// the tree this just replaced.
    /// </summary>
    private bool RebuildCompositionTarget(HWND hwnd)
    {
        lock (_compositionLock)
        {
            return RebuildCompositionTargetUnlocked(hwnd);
        }
    }

    /// <summary>Always called while holding <see cref="_compositionLock"/>.</summary>
    private bool RebuildCompositionTargetUnlocked(HWND hwnd)
    {
        if (_dcompDevicePtr == 0)
        {
            return false;
        }

        IDCompositionDevice? device = null;
        IDCompositionTarget? target = null;
        IDCompositionVisual? rootVisual = null;

        try
        {
            device = (IDCompositionDevice)Marshal.GetUniqueObjectForIUnknown(_dcompDevicePtr);
            device.CreateTargetForHwnd(hwnd, true, out target);
            device.CreateVisual(out rootVisual);
            target!.SetRoot(rootVisual);
            device.Commit();

            ReleaseWindowBoundCompositionPointers();
            ReleaseRawPointer(ref _dcompOverlayVisualPtr);

            _dcompTargetPtr = Marshal.GetIUnknownForObject(target);
            _dcompRootVisualPtr = Marshal.GetIUnknownForObject(rootVisual);
            CompositionGeneration++;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(rootVisual);
            ReleaseComObject(target);
            ReleaseComObject(device);
        }
    }

    private bool EnsureTaskbarMessageWindow()
    {
        if (!_taskbarMessageHwnd.IsNull)
        {
            return true;
        }

        if (!EnsureWindowClassRegistered())
        {
            return false;
        }

        _taskbarMessageHwnd = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
            _className,
            "CielWin TaskbarCreated Receiver",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            1,
            1,
            HWND.Null,
            null,
            _hInstanceSafe!,
            null);

        return !_taskbarMessageHwnd.IsNull;
    }

    private HWND CreateHostWindow()
    {
        if (!EnsureWindowClassRegistered())
        {
            return HWND.Null;
        }

        // The whole MONITOR, not the work area: sizing to rcWork would leave the scene short of the
        // monitor edge wherever the taskbar is docked. The composition target is bound to this
        // window, so it is sized by it.
        RECT monitorRect = GetPrimaryMonitorRect();
        var width = monitorRect.right - monitorRect.left;
        var height = monitorRect.bottom - monitorRect.top;

        // Hidden and WS_EX_NOACTIVATE: a visible, activatable, monitor-sized popup would take the
        // foreground right after start and stay foreground after SetParent, so the covered-desktop
        // check would hold every alert. AttachToDesktop shows it, without activation, only once it is
        // a WS_CHILD behind the icons.
        return PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
            _className,
            "CielWin Scene Wallpaper",
            WINDOW_STYLE.WS_POPUP,
            monitorRect.left,
            monitorRect.top,
            width,
            height,
            HWND.Null,
            null,
            _hInstanceSafe!,
            null);
    }

    private bool EnsureWindowClassRegistered()
    {
        if (_classRegistered)
        {
            return true;
        }

        _wndProcDelegate ??= WndProc;

        // One handle, reused for both RegisterClassEx and CreateWindowEx: CreateWindowEx fails with
        // 1407 (ERROR_CANNOT_FIND_WND_CLASS) if its hInstance does not exactly match the one the
        // class was registered under, even though the class registered fine.
        _hInstanceSafe = PInvoke.GetModuleHandle((string?)null);
        _hInstance = new HINSTANCE(_hInstanceSafe.DangerousGetHandle());

        fixed (char* classNamePtr = _className)
        {
            WNDCLASSEXW wc = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProcDelegate,
                hInstance = _hInstance,
                lpszClassName = classNamePtr,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
            };

            if (PInvoke.RegisterClassEx(wc) == 0)
            {
                return false;
            }
        }

        _classRegistered = true;
        return true;
    }

    /// <summary><c>rcMonitor</c>, the WHOLE primary monitor (not <c>rcWork</c>).</summary>
    private static RECT GetPrimaryMonitorRect()
    {
        HMONITOR primary = PInvoke.MonitorFromWindow(HWND.Null, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO mi = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (PInvoke.GetMonitorInfo(primary, ref mi))
        {
            return mi.rcMonitor;
        }

        return new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
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
    /// Handles the registered <c>TaskbarCreated</c> message (Explorer restarted) on the hidden
    /// top-level receiver by re-running the attach dance against this same host; everything else falls
    /// through to <c>DefWindowProc</c>. Deliberately never handles <c>WM_PAINT</c>: the host draws
    /// nothing itself, so <c>DefWindowProc</c>'s default (no drawing) is the intended behaviour.
    /// </summary>
    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            if (!_disposed)
            {
                TryAttach();
            }

            return new LRESULT(0);
        }

        return msg switch
        {
            PInvoke.WM_DESTROY => new LRESULT(0),
            _ => PInvoke.DefWindowProc(hwnd, msg, wParam, lParam),
        };
    }

    private void ReleaseWindowBoundCompositionPointers()
    {
        ReleaseRawPointer(ref _dcompTargetPtr);
        ReleaseRawPointer(ref _dcompRootVisualPtr);
    }

    /// <summary>
    /// Releases a raw <c>IUnknown</c> pointer captured by <see cref="Marshal.GetIUnknownForObject"/>
    /// directly, via <see cref="Marshal.Release"/> -- safe from ANY thread for a free-threaded
    /// (<c>IAgileObject</c>) COM object like every DirectComposition interface here, unlike calling a
    /// real method on it (see the class remarks "Composition threading"): <c>Release</c> is always
    /// vtable slot 2 on every COM interface, so no QueryInterface -- the actual failure point -- is
    /// ever needed just to drop a reference.
    /// </summary>
    private static void ReleaseRawPointer(ref nint ptr)
    {
        if (ptr != 0)
        {
            Marshal.Release(ptr);
            ptr = 0;
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
