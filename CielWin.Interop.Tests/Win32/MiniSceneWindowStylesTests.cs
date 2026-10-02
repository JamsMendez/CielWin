using CielWin.Interop.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop.Tests.Win32;

public sealed class MiniSceneWindowStylesTests
{
    [Fact]
    public void ExtendedStyleIsTopmostToolNoActivateClickThroughLayeredAndNoRedirectionBitmap()
    {
        var style = MiniSceneWindowStyles.ExtendedStyle;

        Assert.Equal(
            WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
            | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP,
            style);
        Assert.True(style.HasFlag(WINDOW_EX_STYLE.WS_EX_LAYERED)); // click-through needs it, see the style's remarks
        Assert.False(style.HasFlag(WINDOW_EX_STYLE.WS_EX_APPWINDOW));
    }

    [Fact]
    public void PlacementNeverActivatesAndStaysInTheTopmostBand()
    {
        Assert.True(MiniSceneWindowStyles.PlacementFlags.HasFlag(SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE));
        Assert.Equal((nint)(-1), MiniSceneWindowStyles.InsertAfterTopmost);
    }

    [Fact]
    public void ReassertKeepsBoundsAndVisibilityAndNeverActivates()
    {
        Assert.Equal(
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE,
            MiniSceneWindowStyles.ReassertFlags);
    }

    [Theory]
    [InlineData(0x1234, 0x9999, true)]  // another window came to the foreground
    [InlineData(0x9999, 0x9999, false)] // our own window (never expected: it is no-activate)
    [InlineData(0, 0x9999, false)]      // no foreground window
    [InlineData(0x1234, 0, false)]      // our window does not exist (yet / anymore)
    public void ReassertsTopmostOnlyWhenAnotherWindowTakesTheForeground(int foreground, int own, bool expected) =>
        Assert.Equal(expected, MiniSceneWindowStyles.ShouldReassertTopmost(foreground, own));
}
