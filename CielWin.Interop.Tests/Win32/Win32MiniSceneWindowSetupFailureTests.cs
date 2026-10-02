using Windows.Win32;
using Windows.Win32.Foundation;
using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// A failed DirectComposition setup must not leave the created HWND (or the registered window class)
/// behind: the mini recovery retries <c>TryCreate</c> on the same instance.
/// </summary>
[Collection(RealDesktopCollection.Name)]
public sealed class Win32MiniSceneWindowSetupFailureTests
{
    private static readonly Rectangle Bounds = new(0, 0, 64, 64);

    [RequiresDesktopSessionFact]
    public void TryCreate_WhenCompositionSetupFails_DestroysTheWindowAndAllowsACleanRetry()
    {
        HWND created = default;
        var attempts = 0;
        using var window = new Win32MiniSceneWindow(hwnd =>
        {
            created = hwnd;
            if (++attempts == 1) throw new InvalidOperationException("dcomp unavailable");
        });

        Assert.False(window.TryCreate(Bounds));

        Assert.False(created.IsNull);
        Assert.False(PInvoke.IsWindow(created), "the failed attempt left its HWND alive");
        Assert.Equal(0, window.Hwnd);
        Assert.Equal(0, window.CompositionGeneration);

        Assert.True(window.TryCreate(Bounds), "the retry after a failed setup must start clean");
        Assert.NotEqual(0, window.Hwnd);
        Assert.Equal(1, window.CompositionGeneration);
    }
}
