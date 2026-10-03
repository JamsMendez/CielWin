using System.Windows.Media;
using CielWin.Interop;

namespace CielWin.App.Wallpaper;

/// <summary>
/// One glide of the mini window from <paramref name="From"/> to <paramref name="To"/> (physical pixels),
/// starting at <paramref name="Start"/> and lasting <paramref name="Duration"/>, eased out (cubic). Pure:
/// the bounds at any moment are a function of the time alone.
/// </summary>
public readonly record struct MiniGlide(Rectangle From, Rectangle To, DateTimeOffset Start, TimeSpan Duration)
{
    /// <summary>Ease-out cubic: fast at first, settling into the target. <paramref name="t"/> in [0, 1].</summary>
    public static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    public bool IsComplete(DateTimeOffset now) => now - Start >= Duration;

    /// <summary>
    /// The bounds at <paramref name="now"/>: position and size interpolated on the eased progress and
    /// rounded to whole pixels; exactly <see cref="To"/> once complete, exactly <see cref="From"/> before the start.
    /// </summary>
    public Rectangle At(DateTimeOffset now)
    {
        if (IsComplete(now)) return To;
        var t = Math.Clamp((now - Start) / Duration, 0, 1);
        var e = Ease(t);
        return Rectangle.FromSize(
            Lerp(From.Left, To.Left, e), Lerp(From.Top, To.Top, e),
            Lerp(From.Width, To.Width, e), Lerp(From.Height, To.Height, e));
    }

    private static int Lerp(int from, int to, double e) =>
        (int)Math.Round(from + (to - from) * e, MidpointRounding.AwayFromZero);
}

/// <summary>A per-frame callback on the UI thread, behind a seam so tests drive frames by hand.</summary>
public interface IFrameSource
{
    /// <summary>Calls <paramref name="onFrame"/> once per frame until <see cref="Stop"/>; replaces any previous callback.</summary>
    void Start(Action onFrame);

    /// <summary>Stops calling back. Idempotent.</summary>
    void Stop();
}

/// <summary>Frames from WPF's <see cref="CompositionTarget.Rendering"/>: vsync-paced, on the UI thread.</summary>
public sealed class CompositionTargetFrameSource : IFrameSource
{
    private Action? _onFrame;

    public void Start(Action onFrame)
    {
        if (_onFrame is null) CompositionTarget.Rendering += OnRendering;
        _onFrame = onFrame;
    }

    public void Stop()
    {
        if (_onFrame is null) return;
        CompositionTarget.Rendering -= OnRendering;
        _onFrame = null;
    }

    private void OnRendering(object? sender, EventArgs e) => _onFrame?.Invoke();
}
