using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// The pure classification half of <see cref="PrimaryMonitorFullscreenDetector"/>, pinned without a
/// live desktop: no caption, not maximised, covering the monitor to within two pixels per edge.
/// </summary>
public sealed class PrimaryMonitorFullscreenDetectorTests
{
    private const uint StyleCaption = 0x00C00000;
    private const uint StyleMaximized = 0x01000000;
    private const uint StyleThickFrame = 0x00040000;

    private static readonly Rectangle Monitor = new(0, 0, 3440, 1440);

    /// <summary>
    /// A borderless TOPMOST fullscreen form with <c>Bounds</c> set to the exact primary screen
    /// bounds (a hardware probe shape). This must count as covering.
    /// </summary>
    [Fact]
    public void BorderlessWindowExactlyOnTheMonitor_IsFullscreen()
    {
        Assert.True(PrimaryMonitorFullscreenDetector.IsFullscreen(style: 0, Monitor, Monitor));
    }

    /// <summary>Chrome/Brave settle one pixel short on a real monitor -- still fullscreen.</summary>
    [Fact]
    public void BorderlessWindowOnePixelShortOfTheMonitor_IsStillFullscreen()
    {
        var bounds = new Rectangle(0, 0, 3440, 1439);

        Assert.True(PrimaryMonitorFullscreenDetector.IsFullscreen(style: 0, bounds, Monitor));
    }

    [Fact]
    public void BorderlessWindowThreePixelsShortOfTheMonitor_IsNotFullscreen()
    {
        var bounds = new Rectangle(0, 0, 3440, 1437);

        Assert.False(PrimaryMonitorFullscreenDetector.IsFullscreen(style: 0, bounds, Monitor));
    }

    [Fact]
    public void AWindowWithACaption_IsNeverFullscreenEvenIfItCoversTheMonitor()
    {
        Assert.False(PrimaryMonitorFullscreenDetector.IsFullscreen(StyleCaption, Monitor, Monitor));
    }

    /// <summary>
    /// A maximised window covers the monitor too (with an auto-hide taskbar) but keeps its caption
    /// and <c>WS_MAXIMIZE</c> -- must stay on the ordinary, non-covering path.
    /// </summary>
    [Fact]
    public void AMaximizedWindow_IsNotFullscreen()
    {
        var style = StyleCaption | StyleThickFrame | StyleMaximized;

        Assert.False(PrimaryMonitorFullscreenDetector.IsFullscreen(style, Monitor, Monitor));
    }

    [Fact]
    public void ABorderlessWindowSmallerThanTheMonitor_IsNotFullscreen()
    {
        var bounds = new Rectangle(100, 100, 800, 600);

        Assert.False(PrimaryMonitorFullscreenDetector.IsFullscreen(style: 0, bounds, Monitor));
    }

    [Fact]
    public void ABorderlessMaximizedWindowWithNoCaption_IsNotFullscreen()
    {
        // WS_MAXIMIZE alone, no caption -- still excluded: "not maximised" is its own condition,
        // independent of the caption check.
        Assert.False(PrimaryMonitorFullscreenDetector.IsFullscreen(StyleMaximized, Monitor, Monitor));
    }
}

/// <summary>
/// The desktop shell (Progman/WorkerW) and
/// click-through overlays are caption-less, non-maximised, full-monitor popups too --
/// <see cref="PrimaryMonitorFullscreenDetector.IsFullscreen"/> alone cannot tell them apart from an
/// actual fullscreen video or game, so <see cref="PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage"/>
/// must veto them first.
/// </summary>
public sealed class PrimaryMonitorFullscreenDetectorExclusionTests
{
    private const uint NoExStyle = 0;
    private const uint TransparentOnly = 0x00000020; // WS_EX_TRANSPARENT alone: not click-through proof.
    private const uint TransparentAndLayered = TransparentOnly | 0x00080000; // + WS_EX_LAYERED.

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void TheShellsOwnClassNames_AreExcludedFromCoverage(string shellClassName)
    {
        Assert.True(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage(shellClassName, NoExStyle, isShellWindow: false));
    }

    /// <summary>
    /// CielWin's own scene wallpaper host is a caption-less, monitor-sized window that can be
    /// foreground right after start; it IS the wallpaper, so it never covers the desktop. Its class
    /// name carries a per-instance GUID suffix.
    /// </summary>
    [Fact]
    public void TheSceneWallpaperHostClass_IsExcludedFromCoverage()
    {
        Assert.True(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage(
                "CielWinSceneWallpaperHost-0123456789abcdef0123456789abcdef", NoExStyle, isShellWindow: false));
    }

    [Fact]
    public void TheShellWindowFlag_IsExcludedFromCoverageRegardlessOfClassName()
    {
        // GetShellWindow() is the most direct signal Win32 offers for "this is the desktop",
        // ahead of any class-name guess.
        Assert.True(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage("AnythingAtAll", NoExStyle, isShellWindow: true));
    }

    /// <summary>The NVIDIA overlay's "Press Alt+Z" toast: click-through and layered, both bits set.</summary>
    [Fact]
    public void ATransparentLayeredOverlay_IsExcludedFromCoverage()
    {
        Assert.True(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage(
                "CEF-OSC-WIDGET", TransparentAndLayered, isShellWindow: false));
    }

    [Fact]
    public void TransparentWithoutLayered_IsNotExcludedFromCoverage()
    {
        Assert.False(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage("SomeAppClass", TransparentOnly, isShellWindow: false));
    }

    /// <summary>The probe shape must still pass through: ordinary class, no overlay styles, not the shell.</summary>
    [Fact]
    public void AnOrdinaryFullscreenApplicationWindow_IsNotExcludedFromCoverage()
    {
        Assert.False(
            PrimaryMonitorFullscreenDetector.IsExcludedFromCoverage("CielWinProbeForm", NoExStyle, isShellWindow: false));
    }
}
