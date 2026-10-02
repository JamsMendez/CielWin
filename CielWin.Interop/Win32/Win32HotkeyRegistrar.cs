using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop.Win32;

/// <summary>
/// Global hotkeys through <c>RegisterHotKey</c>, delivered to a message-only window this class owns.
/// </summary>
/// <remarks>
/// <para>
/// A message-only window (parent <c>HWND_MESSAGE</c>) rather than a thread-queue registration: any
/// message loop on the creating thread (the WPF dispatcher, in production) dispatches WM_HOTKEY to
/// the window procedure, so no WPF type has to cross into this assembly and no filter hook is needed.
/// </para>
/// <para>
/// <c>RegisterHotKey</c> needs no elevation and installs no keyboard hook: Windows itself owns the
/// chord, and a chord another process already owns is simply refused (<see cref="Register"/> returns
/// <see langword="false"/>). The window is created on the first <see cref="Register"/> call, on the
/// calling thread; <see cref="Register"/>, <see cref="Dispose"/> and every <see cref="Pressed"/>
/// raise belong to that thread.
/// </para>
/// </remarks>
public sealed unsafe partial class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    /// <summary><c>WM_HOTKEY</c>.</summary>
    internal const uint WmHotkey = 0x0312;

    /// <summary><c>HWND_MESSAGE</c>: the parent that makes a window message-only.</summary>
    private static readonly HWND MessageOnlyParent = new(-3);

    private readonly string _className = $"CielWinHotkeys-{Guid.NewGuid():N}";
    private readonly List<int> _registeredIds = [];
    private WNDPROC? _wndProc;
    private SafeHandle? _hInstance;
    private bool _classRegistered;
    private HWND _hwnd;
    private bool _disposed;

    public event Action<int>? Pressed;

    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        if (_disposed || !EnsureWindow())
        {
            return false;
        }

        if (!RegisterHotKey(_hwnd, id, (uint)modifiers, virtualKey))
        {
            return false;
        }

        _registeredIds.Add(id);
        return true;
    }

    /// <summary>Creates the message-only window on first use. False when it cannot be created.</summary>
    internal bool EnsureWindow()
    {
        if (_disposed)
        {
            return false;
        }

        if (!_hwnd.IsNull)
        {
            return true;
        }

        _wndProc = (hwnd, msg, wParam, lParam) =>
            HandleMessage(msg, wParam.Value) ? new LRESULT(0) : PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
        _hInstance = PInvoke.GetModuleHandle((string?)null);
        fixed (char* name = _className)
        {
            WNDCLASSEXW wc = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProc,
                hInstance = new HINSTANCE(_hInstance.DangerousGetHandle()),
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(wc) == 0)
            {
                return false;
            }
        }

        _classRegistered = true;
        _hwnd = PInvoke.CreateWindowEx(
            0, _className, "CielWin Hotkeys", 0, 0, 0, 0, 0, MessageOnlyParent, null, _hInstance, null);
        if (_hwnd.IsNull)
        {
            ReleaseWindow();
            return false;
        }

        return true;
    }

    /// <summary>
    /// The window procedure's decision, testable without a window: WM_HOTKEY raises
    /// <see cref="Pressed"/> with the id carried in <paramref name="wParam"/>. A throwing handler is
    /// swallowed -- an exception must never unwind through a native window procedure.
    /// </summary>
    internal bool HandleMessage(uint message, nuint wParam)
    {
        if (message != WmHotkey)
        {
            return false;
        }

        try
        {
            Pressed?.Invoke((int)wParam);
        }
        catch
        {
        }

        return true;
    }

    /// <summary>Synchronously delivers a message to the window (tests prove the routing with it).</summary>
    internal void SendToWindow(uint message, nuint wParam) =>
        PInvoke.SendMessage(_hwnd, message, new WPARAM(wParam), default);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_hwnd, id);
        }

        _registeredIds.Clear();
        ReleaseWindow();
        Pressed = null;
    }

    private void ReleaseWindow()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }

        if (_classRegistered && _hInstance is not null)
        {
            PInvoke.UnregisterClass(_className, _hInstance);
            _classRegistered = false;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
