using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop.Win32;

/// <summary>Which modifier keys <c>GetAsyncKeyState</c> reported down when a hook event arrived.</summary>
internal readonly record struct ModifierSnapshot(bool Alt, bool Control, bool Shift, bool LeftWin, bool RightWin)
{
    /// <summary>The chord modifiers this snapshot holds; either Win key is <see cref="HotkeyModifiers.Win"/>.</summary>
    public HotkeyModifiers ToHotkeyModifiers()
    {
        var held = HotkeyModifiers.None;
        if (Alt) held |= HotkeyModifiers.Alt;
        if (Control) held |= HotkeyModifiers.Control;
        if (Shift) held |= HotkeyModifiers.Shift;
        if (LeftWin || RightWin) held |= HotkeyModifiers.Win;
        return held;
    }
}

/// <summary>
/// Global hotkeys through a <c>WH_KEYBOARD_LL</c> hook that owns the WHOLE key stroke of a chord.
/// </summary>
/// <remarks>
/// <para>
/// <c>RegisterHotKey</c> (<see cref="Win32HotkeyRegistrar"/>) only consumes the chord's key down: the
/// key up and auto-repeat traffic still reach the focused app, and a terminal such as Alacritty running
/// WSL types the letter. This hook swallows the fresh down, every repeat and the matching up (see
/// <see cref="KeyboardChordMatcher"/>), and after firing injects the unassigned mask key
/// (<see cref="KeyboardChordMatcher.MaskKey"/>) so the Alt release does not open the app's menu.
/// </para>
/// <para>
/// The hook lives on its own background thread with a <c>GetMessage</c> loop, never on the UI thread:
/// a busy dispatcher must not make Windows drop the hook for missing <c>LowLevelHooksTimeout</c>. The
/// callback only decides and returns, so <see cref="Pressed"/> is raised ON THE HOOK THREAD and a
/// handler must hand its work off (post to the UI thread) instead of doing it there.
/// </para>
/// <para>
/// <see cref="Register"/> and <see cref="Dispose"/> may be called from any thread. No elevation is
/// needed. Unlike <c>RegisterHotKey</c>, a chord another process registered is not refused; the hook
/// sees the keys first.
/// </para>
/// </remarks>
public sealed unsafe class Win32KeyboardHookRegistrar : IHotkeyRegistrar
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;

    /// <summary>How long <see cref="TryStart"/> waits for the hook thread to report the install.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long <see cref="Dispose"/> waits for the hook thread to end.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Action<string>? _onHandlerFailed;
    private readonly Action _injectMask;
    private readonly Lock _gate = new();
    private readonly KeyboardChordMatcher _matcher = new();
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private HOOKPROC? _hookProc;
    private SafeHandle? _module;
    private uint _nativeThreadId;
    private int _installError;
    private bool _installed;
    private bool _disposed;

    /// <summary>A registrar with no hook installed: the tests drive <see cref="HandleKey"/> directly.</summary>
    internal Win32KeyboardHookRegistrar(Action<string>? onHandlerFailed, Action injectMask)
    {
        _onHandlerFailed = onHandlerFailed;
        _injectMask = injectMask;
    }

    public event Action<int>? Pressed;

    /// <summary>The hook thread, while it runs (null for a registrar built without one).</summary>
    internal Thread? HookThread => _thread;

    /// <summary>
    /// Starts the hook thread and waits for the hook to be installed. Null, with the Win32 error in
    /// <paramref name="win32Error"/> (0 when it timed out), when it could not be: the caller falls back
    /// to <see cref="Win32HotkeyRegistrar"/>.
    /// </summary>
    /// <param name="onHandlerFailed">
    /// Invoked with the exception TYPE name only (never <see cref="Exception.Message"/>, which can hold
    /// an absolute path) when a <see cref="Pressed"/> handler throws; the failure is swallowed either way.
    /// </param>
    /// <param name="win32Error">The install failure, when null is returned.</param>
    public static Win32KeyboardHookRegistrar? TryStart(Action<string>? onHandlerFailed, out int win32Error)
    {
        var registrar = new Win32KeyboardHookRegistrar(onHandlerFailed, InjectMaskKey);
        var thread = new Thread(registrar.Run) { IsBackground = true, Name = "CielWinKeyboardHook" };
        registrar._thread = thread;
        thread.Start();
        if (registrar._started.Wait(StartTimeout) && registrar._installed)
        {
            win32Error = 0;
            return registrar;
        }

        win32Error = registrar._installError;
        registrar.Dispose();
        return null;
    }

    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        lock (_gate)
        {
            return !_disposed && _matcher.Register(id, modifiers, virtualKey);
        }
    }

    /// <summary>
    /// The hook callback's decision, testable without a hook: true when the event must be swallowed. A
    /// fired chord raises <see cref="Pressed"/> and injects the mask key. Nothing ever escapes: an
    /// exception must not unwind through a native hook, not even one thrown by the failure callback.
    /// </summary>
    internal bool HandleKey(uint virtualKey, bool isDown, HotkeyModifiers held, uint time)
    {
        KeyDecision decision;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            decision = _matcher.OnKey(virtualKey, isDown, held, time);
        }

        if (decision.FiredId is { } id)
        {
            try
            {
                Pressed?.Invoke(id);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }

            try
            {
                _injectMask();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        return decision.Swallowed;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Pressed = null;
        if (_thread is { } thread)
        {
            // The thread's queue exists before _started is set; a thread that never started its loop
            // has already ended (or ends right after the install failed), so the join returns.
            _started.Wait(StartTimeout);
            if (_nativeThreadId != 0)
            {
                PInvoke.PostThreadMessage(_nativeThreadId, PInvoke.WM_QUIT, default, default);
            }

            if (thread != Thread.CurrentThread)
            {
                thread.Join(StopTimeout);
            }
        }
    }

    private void Run()
    {
        try
        {
            _nativeThreadId = PInvoke.GetCurrentThreadId();
            // Creates this thread's message queue before Dispose can post WM_QUIT to it.
            PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            _hookProc = HookProc;
            // Not disposed: the process's own module handle is not this thread's to release.
            _module = PInvoke.GetModuleHandle((string?)null);
            using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, _module, 0);
            if (hook.IsInvalid)
            {
                _installError = Marshal.GetLastPInvokeError();
                return;
            }

            _installed = true;
            _started.Set();
            // Low-level hook callbacks are delivered while this thread waits in GetMessage.
            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) is { Value: > 0 })
            {
                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            _started.Set();
        }
    }

    /// <summary>
    /// The hook callback's translation from its native inputs, testable without a hook: the window
    /// message (<c>WM_KEYDOWN</c>/<c>WM_SYSKEYDOWN</c> is a down, <c>WM_KEYUP</c>/<c>WM_SYSKEYUP</c> an
    /// up, anything else passes untouched), the event's <c>vkCode</c> and <c>time</c>, and the modifier
    /// snapshot. True when the event must be swallowed.
    /// </summary>
    internal bool HandleHookEvent(uint message, uint virtualKey, uint time, ModifierSnapshot modifiers)
    {
        var isDown = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
        var isUp = message is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP;
        return (isDown || isUp) && HandleKey(virtualKey, isDown, modifiers.ToHotkeyModifiers(), time);
    }

    private LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (code >= 0)
            {
                var input = (KBDLLHOOKSTRUCT*)lParam.Value;
                if (HandleHookEvent((uint)wParam.Value, input->vkCode, input->time, ReadModifiers()))
                {
                    return new LRESULT(1);
                }
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    /// <summary>The modifiers held right now (the hook sees the state before the current key's event).</summary>
    private static ModifierSnapshot ReadModifiers()
    {
        return new ModifierSnapshot(
            Alt: IsDown(VkMenu),
            Control: IsDown(VkControl),
            Shift: IsDown(VkShift),
            LeftWin: IsDown(VkLeftWin),
            RightWin: IsDown(VkRightWin));

        static bool IsDown(int virtualKey) => PInvoke.GetAsyncKeyState(virtualKey) < 0;
    }

    private static void InjectMaskKey()
    {
        Span<INPUT> inputs = stackalloc INPUT[2];
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = (VIRTUAL_KEY)KeyboardChordMatcher.MaskKey;
        inputs[1] = inputs[0];
        inputs[1].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput(inputs, sizeof(INPUT));
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            _onHandlerFailed?.Invoke(exception.GetType().Name);
        }
        catch
        {
        }
    }
}
