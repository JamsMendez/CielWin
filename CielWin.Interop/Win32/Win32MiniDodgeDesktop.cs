using System.Runtime.InteropServices;

namespace CielWin.Interop.Win32;

/// <summary>A point in physical virtual-screen pixels (the space <see cref="Rectangle"/> uses).</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// What the mini window's cursor dodge reads from the desktop. The window is click-through, so it gets no
/// pointer events: the cursor is polled instead.
/// </summary>
public interface IMiniDodgeDesktop
{
    /// <summary>The cursor, physical pixels; null when it cannot be read (for example on the secure desktop).</summary>
    ScreenPoint? ReadCursor();

    /// <summary>The work area of the monitor nearest <paramref name="bounds"/>, physical pixels; null when it cannot be read.</summary>
    Rectangle? WorkAreaOf(Rectangle bounds);
}

/// <summary>
/// <c>GetCursorPos</c> and <c>MonitorFromRect</c>/<c>GetMonitorInfo</c> (<c>rcWork</c>). The process is
/// per-monitor (v2) DPI aware, so both answer in the physical pixels the mini window is placed in.
/// Never throws: a failed call is null.
/// </summary>
public sealed partial class Win32MiniDodgeDesktop : IMiniDodgeDesktop
{
    private const uint MonitorDefaultToNearest = 2;

    public ScreenPoint? ReadCursor() => GetCursorPos(out var point) ? new ScreenPoint(point.X, point.Y) : null;

    public Rectangle? WorkAreaOf(Rectangle bounds)
    {
        var rect = new NativeRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return null;
        return new Rectangle(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromRect(ref NativeRect rect, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
