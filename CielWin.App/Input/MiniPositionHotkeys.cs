using CielWin.App.Wallpaper;
using CielWin.Interop;

namespace CielWin.App.Input;

/// <summary>
/// The two global chords that walk the mini window around the work area: Alt+M clockwise
/// (<see cref="MiniWindowPlacement.Next"/>), Alt+Shift+M counter-clockwise
/// (<see cref="MiniWindowPlacement.Previous"/>). Registered through <c>RegisterHotKey</c>, so no
/// keyboard hook and no elevation; auto-repeat is off so a held chord moves once.
/// </summary>
public static class MiniPositionHotkeys
{
    public const int ClockwiseId = 1;
    public const int CounterClockwiseId = 2;

    /// <summary><c>VK_M</c>.</summary>
    public const uint VirtualKeyM = 0x4D;

    public const HotkeyModifiers ClockwiseModifiers = HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat;

    public const HotkeyModifiers CounterClockwiseModifiers =
        HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat;

    /// <summary>The position one step from <paramref name="from"/> for hotkey <paramref name="id"/>; false for an unknown id.</summary>
    public static bool TryStep(int id, MiniPosition from, out MiniPosition next)
    {
        switch (id)
        {
            case ClockwiseId:
                next = MiniWindowPlacement.Next(from);
                return true;
            case CounterClockwiseId:
                next = MiniWindowPlacement.Previous(from);
                return true;
            default:
                next = from;
                return false;
        }
    }
}
