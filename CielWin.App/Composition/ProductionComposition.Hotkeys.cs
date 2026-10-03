using CielWin.Interop;

namespace CielWin.App.Composition;

public static partial class ProductionComposition
{
    /// <summary>
    /// The hotkey registrar: the low-level keyboard hook from <paramref name="tryHook"/>, which swallows
    /// the whole chord (down, repeats, up), or -- when it cannot be installed, or its start throws --
    /// <paramref name="fallback"/> (<c>RegisterHotKey</c>, which lets the key up leak), traced with the
    /// Win32 error or the exception TYPE only.
    /// </summary>
    internal static IHotkeyRegistrar CreateHotkeys(
        Action<string> trace,
        Func<(IHotkeyRegistrar? Hook, int Win32Error)> tryHook,
        Func<IHotkeyRegistrar> fallback)
    {
        string error;
        try
        {
            var (hook, win32Error) = tryHook();
            if (hook is not null)
            {
                return hook;
            }

            error = win32Error.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name;
        }

        trace($"hotkey hook-unavailable error={error} fallback=register-hotkey");
        return fallback();
    }
}
