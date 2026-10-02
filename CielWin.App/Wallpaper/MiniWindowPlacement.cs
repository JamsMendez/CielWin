using CielWin.Interop;

namespace CielWin.App.Wallpaper;

/// <summary>
/// Pure placement math for the mini scene window (<see cref="WallpaperMode.SceneMini"/>): a square,
/// <c>monitorHeight / 5</c> on a side, flush with one corner or side midpoint of the work area so it
/// stays clear of the taskbar. No Win32 and no UI, so the geometry is testable without a desktop.
/// </summary>
public static class MiniWindowPlacement
{
    /// <summary>The window's side is this fraction of the monitor's height.</summary>
    public const int SideDivisor = 5;

    /// <summary>
    /// The window rectangle for <paramref name="corner"/> of <paramref name="workArea"/>, in physical
    /// pixels. The side never exceeds the work area's shorter dimension and is never negative, so a
    /// degenerate monitor height or a tiny work area yields a smaller (possibly empty) window that still
    /// lies inside the work area.
    /// </summary>
    public static Rectangle Compute(Rectangle workArea, int monitorHeight, MiniPosition corner)
    {
        var side = Math.Max(0, Math.Min(monitorHeight / SideDivisor, Math.Min(workArea.Width, workArea.Height)));
        var x = corner switch
        {
            MiniPosition.TopRight or MiniPosition.RightCenter or MiniPosition.BottomRight => workArea.Right - side,
            MiniPosition.TopCenter or MiniPosition.BottomCenter => workArea.Left + (workArea.Width - side) / 2,
            _ => workArea.Left,
        };
        var y = corner switch
        {
            MiniPosition.BottomLeft or MiniPosition.BottomCenter or MiniPosition.BottomRight => workArea.Bottom - side,
            MiniPosition.LeftCenter or MiniPosition.RightCenter => workArea.Top + (workArea.Height - side) / 2,
            _ => workArea.Top,
        };

        return Rectangle.FromSize(x, y, side, side);
    }

    /// <summary>The next position, clockwise through all eight: corners and side midpoints, and around (Alt+M).</summary>
    public static MiniPosition Next(MiniPosition corner) => corner switch
    {
        MiniPosition.TopLeft => MiniPosition.TopCenter,
        MiniPosition.TopCenter => MiniPosition.TopRight,
        MiniPosition.TopRight => MiniPosition.RightCenter,
        MiniPosition.RightCenter => MiniPosition.BottomRight,
        MiniPosition.BottomRight => MiniPosition.BottomCenter,
        MiniPosition.BottomCenter => MiniPosition.BottomLeft,
        MiniPosition.BottomLeft => MiniPosition.LeftCenter,
        MiniPosition.LeftCenter => MiniPosition.TopLeft,
        // Not a defined position (a cast int): restart the cycle rather than throw.
        _ => MiniPosition.TopLeft,
    };

    /// <summary>
    /// The previous position, counter-clockwise through all eight (Alt+Shift+M): the exact inverse of
    /// <see cref="Next"/>, so <c>Previous(Next(p)) == p</c> for every defined position.
    /// </summary>
    public static MiniPosition Previous(MiniPosition corner) => corner switch
    {
        MiniPosition.TopLeft => MiniPosition.LeftCenter,
        MiniPosition.LeftCenter => MiniPosition.BottomLeft,
        MiniPosition.BottomLeft => MiniPosition.BottomCenter,
        MiniPosition.BottomCenter => MiniPosition.BottomRight,
        MiniPosition.BottomRight => MiniPosition.RightCenter,
        MiniPosition.RightCenter => MiniPosition.TopRight,
        MiniPosition.TopRight => MiniPosition.TopCenter,
        MiniPosition.TopCenter => MiniPosition.TopLeft,
        // Not a defined position (a cast int): restart the cycle rather than throw.
        _ => MiniPosition.TopLeft,
    };
}
