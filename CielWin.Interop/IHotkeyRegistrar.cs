namespace CielWin.Interop;

/// <summary>The <c>MOD_*</c> flags <c>RegisterHotKey</c> takes, bit for bit.</summary>
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
    /// <summary>Raised with the registered id when its chord is pressed, on the registering thread.</summary>
    event Action<int>? Pressed;

    /// <summary>
    /// Registers <paramref name="modifiers"/> + <paramref name="virtualKey"/> under
    /// <paramref name="id"/>. Never throws; <see langword="false"/> when the chord is already owned by
    /// another process (or this registrar is disposed).
    /// </summary>
    bool Register(int id, HotkeyModifiers modifiers, uint virtualKey);
}
