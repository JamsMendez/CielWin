using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// The foreground hook that keeps the mini window on top of the shared topmost band: created with the
/// window, it re-claims the top when another window takes the foreground, and is gone after dispose.
/// The composition setup is replaced by a no-op so no GPU is needed.
/// </summary>
[Collection(RealDesktopCollection.Name)]
[Trait("Category", "RequiresDesktop")]
public sealed class Win32MiniSceneWindowForegroundHookTests
{
    private static readonly Rectangle Bounds = new(0, 0, 64, 64);

    private static Win32MiniSceneWindow CreatePlaced()
    {
        var window = new Win32MiniSceneWindow(_ => { });
        Assert.True(window.TryCreate(Bounds));
        Assert.True(window.Place(Bounds));
        return window;
    }

    /// <summary>Whether <paramref name="upper"/> is above <paramref name="lower"/> in the z-order.</summary>
    private static bool IsAbove(nint upper, nint lower)
    {
        for (var hwnd = PInvoke.GetWindow(new HWND(lower), GET_WINDOW_CMD.GW_HWNDPREV);
             !hwnd.IsNull;
             hwnd = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_HWNDPREV))
        {
            if (hwnd == new HWND(upper)) return true;
        }

        return false;
    }

    [RequiresDesktopSessionFact]
    public void CreatingTheWindow_InstallsTheForegroundHook()
    {
        using var window = CreatePlaced();

        Assert.True(window.ReassertsTopmost);
    }

    [RequiresDesktopSessionFact]
    public void AnotherWindowTakingTheForeground_BringsOursBackAboveIt()
    {
        using var ours = CreatePlaced();
        using var other = CreatePlaced();
        Assert.True(IsAbove(other.Hwnd, ours.Hwnd), "the later topmost window should start above ours");

        ours.OnForegroundChanged(other.Hwnd);

        Assert.True(IsAbove(ours.Hwnd, other.Hwnd), "ours did not re-claim the top of the topmost band");
    }

    [RequiresDesktopSessionFact]
    public void OurOwnWindowTakingTheForeground_ChangesNothing()
    {
        using var ours = CreatePlaced();
        using var other = CreatePlaced();

        ours.OnForegroundChanged(ours.Hwnd);

        Assert.True(IsAbove(other.Hwnd, ours.Hwnd));
    }

    [RequiresDesktopSessionFact]
    public void AfterDispose_TheHookIsGoneAndTheCallbackIsANoOp()
    {
        var window = CreatePlaced();
        using var other = CreatePlaced();

        window.Dispose();

        Assert.False(window.ReassertsTopmost);
        window.OnForegroundChanged(other.Hwnd);
        Assert.Equal(0, window.Hwnd);
    }
}
