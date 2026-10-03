namespace CielWin.Interop;

/// <summary>The <c>MOD_*</c> flags <c>RegisterHotKey</c> takes, bit for bit (the keyboard hook reads the same flags).</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,

    /// <summary>Holding the chord down does not repeat it.</summary>
    NoRepeat = 0x4000,
}

/// <summary>
/// System-wide hotkeys. Lets the composition root and its tests hold the registrar without depending
/// on the Win32 implementation.
/// </summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>
    /// Raised with the registered id when its chord is pressed. The raising thread is the
    /// implementation's (the registering thread for <c>RegisterHotKey</c>, the hook's own thread for the
    /// low-level keyboard hook): a handler must not assume it runs on the UI thread, and should only
    /// hand the work off (post it) rather than do it inline.
    /// </summary>
    event Action<int>? Pressed;

    /// <summary>
    /// Registers <paramref name="modifiers"/> + <paramref name="virtualKey"/> under
    /// <paramref name="id"/>. Never throws; <see langword="false"/> when the id or chord is already registered here, the
    /// chord is already owned by another process (<c>RegisterHotKey</c> only), the modifiers are not
    /// supported, or this registrar is disposed.
    /// </summary>
    bool Register(int id, HotkeyModifiers modifiers, uint virtualKey);
}
