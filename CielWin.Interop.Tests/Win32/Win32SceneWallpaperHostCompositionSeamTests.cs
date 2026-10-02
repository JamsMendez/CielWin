using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// The composition seam <see cref="Win32SceneWallpaperHost"/> exposes for the WebView2 layer to add
/// one overlay visual, remove it, commit, and detect a host-window recreate -- a public surface
/// rather than an <c>InternalsVisibleTo</c> hack, since the layer lives in <c>CielWin.App</c>.
/// </summary>
/// <remarks>
/// Pure state checks only, before any real attach -- no desktop session needed. The desktop-gated
/// exercise of the actual DirectComposition tree lives in <see cref="Win32SceneWallpaperHostRealAttachTests"/>.
/// </remarks>
public sealed class Win32SceneWallpaperHostCompositionSeamTests
{
    [Fact]
    public void ImplementsTheOverlaySurfaceTheWebViewLayerConsumes()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.IsAssignableFrom<ICompositionOverlaySurface>(host);
        Assert.IsAssignableFrom<ISceneWallpaperHost>(host);
    }

    [Fact]
    public void IsCompositionReady_IsFalseBeforeAttach()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.False(host.IsCompositionReady);
    }

    [Fact]
    public void CompositionGeneration_StartsAtZeroBeforeAttach()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.Equal(0, host.CompositionGeneration);
    }

    [Fact]
    public void Hwnd_IsZeroBeforeAttach()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.Equal(0, host.Hwnd);
    }

    [Fact]
    public void AddCompositionOverlayVisual_ReturnsNullBeforeTheCompositionTreeExists()
    {
        using var host = new Win32SceneWallpaperHost();
        Assert.Null(host.AddCompositionOverlayVisual());
    }

    /// <summary>Never throws: a DComp failure (or nothing to remove) must never reach the caller.</summary>
    [Fact]
    public void RemoveCompositionOverlayVisual_IsANoOpWhenNothingWasEverAdded()
    {
        using var host = new Win32SceneWallpaperHost();
        var exception = Record.Exception(host.RemoveCompositionOverlayVisual);
        Assert.Null(exception);
    }

    /// <summary>Never throws: the whole point of this seam is that DComp failures stay contained.</summary>
    [Fact]
    public void CommitComposition_IsANoOpBeforeTheDeviceExists()
    {
        using var host = new Win32SceneWallpaperHost();
        var exception = Record.Exception(host.CommitComposition);
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_IsIdempotentAndTryAttachAfterDisposeThrows()
    {
        var host = new Win32SceneWallpaperHost();
        host.Dispose();

        Assert.Null(Record.Exception(host.Dispose));
        Assert.Throws<ObjectDisposedException>(() => host.TryAttach());
    }
}
