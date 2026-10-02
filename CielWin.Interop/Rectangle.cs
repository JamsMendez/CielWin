namespace CielWin.Interop;

/// <summary>
/// A rectangle in real (unscaled by DPI) screen-space pixel coordinates.
/// </summary>
/// <remarks>
/// Mirrors the Win32 <c>RECT</c> shape (Left/Top/Right/Bottom, not X/Y/Width/Height) rather than
/// <see cref="System.Drawing.Rectangle"/>, since storing edges rather than extents avoids the "did I
/// add or overwrite width" class of bug when repeatedly repositioning a window.
/// </remarks>
public readonly record struct Rectangle(int Left, int Top, int Right, int Bottom)
{
    public static Rectangle Empty => new(0, 0, 0, 0);

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public static Rectangle FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    public bool Contains(Rectangle other) =>
        Left <= other.Left && other.Right <= Right &&
        Top <= other.Top && other.Bottom <= Bottom;
}
