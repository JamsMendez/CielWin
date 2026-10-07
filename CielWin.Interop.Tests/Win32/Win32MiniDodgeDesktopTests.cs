using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

[Trait("Category", "RequiresDesktop")]
public sealed class Win32MiniDodgeDesktopTests
{
    [Fact]
    public void ReadsTheCursor() => Assert.NotNull(new Win32MiniDodgeDesktop().ReadCursor());

    [Fact]
    public void WorkAreaOfAnyRectIsANonEmptyMonitorWorkArea()
    {
        // Far off every monitor: the nearest one still answers.
        var work = new Win32MiniDodgeDesktop().WorkAreaOf(Rectangle.FromSize(-100000, -100000, 10, 10));

        Assert.NotNull(work);
        Assert.True(work.Value.Width > 0 && work.Value.Height > 0);
    }
}
