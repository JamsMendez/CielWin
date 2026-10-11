using CielWin.Interop;
using CielWin.Interop.Win32;

namespace CielWin.App.Wallpaper;

public enum MiniDodgeDirection { Left, Right, Up, Down }

/// <summary>Where a dodge sends the window: the direction and its bounds (physical pixels).</summary>
public readonly record struct MiniDodgeChoice(MiniDodgeDirection Direction, Rectangle Bounds);

/// <summary>
/// The cursor dodge's geometry (CieLinux <c>MiniDodge</c>): the mini window is click-through but still hides
/// what is under it, so it moves aside when the cursor comes near. Pure, physical pixels throughout.
/// </summary>
public static class MiniDodge
{
    /// <summary>Pixels around the window that count as "near".</summary>
    public const int Approach = 24;

    /// <summary>Pixels between the dodged window and the zone it left.</summary>
    public const int Gap = 8;

    /// <summary>The cursor stays away this long before the window returns.</summary>
    public static readonly TimeSpan ReturnDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>How often the cursor is read while the window is shown.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static string Name(MiniDodgeDirection direction) => direction switch
    {
        MiniDodgeDirection.Left => "left",
        MiniDodgeDirection.Right => "right",
        MiniDodgeDirection.Up => "up",
        _ => "down",
    };

    /// <summary><paramref name="bounds"/> grown by <paramref name="margin"/> on every side.</summary>
    public static Rectangle Zone(Rectangle bounds, int margin = Approach) =>
        new(bounds.Left - margin, bounds.Top - margin, bounds.Right + margin, bounds.Bottom + margin);

    /// <summary>Whether <paramref name="point"/> lies in <paramref name="area"/> (right and bottom edges excluded, as in RECT).</summary>
    public static bool Contains(Rectangle area, ScreenPoint point) =>
        area.Left <= point.X && point.X < area.Right && area.Top <= point.Y && point.Y < area.Bottom;

    /// <summary>
    /// Where to dodge from <paramref name="home"/> with the cursor at <paramref name="cursor"/>: away from it
    /// along the dominant axis (relative to home's centre), shifted by side + <see cref="Approach"/> +
    /// <see cref="Gap"/> so it clears home's zone. The bounds must lie inside <paramref name="workArea"/> and
    /// keep the cursor out of their own zone; when the preferred side fails, the two perpendicular sides are
    /// tried, the one farther from the cursor first. Null: nowhere to go, the window stays.
    /// </summary>
    public static MiniDodgeChoice? Choose(Rectangle home, ScreenPoint cursor, Rectangle workArea)
    {
        var dx = cursor.X - (home.Left + home.Right) / 2;
        var dy = cursor.Y - (home.Top + home.Bottom) / 2;
        var horizontal = dx > 0 ? MiniDodgeDirection.Left : MiniDodgeDirection.Right;
        var vertical = dy > 0 ? MiniDodgeDirection.Up : MiniDodgeDirection.Down;
        MiniDodgeDirection[] order = Math.Abs(dx) >= Math.Abs(dy)
            ? [horizontal, vertical, Opposite(vertical)]
            : [dy < 0 ? MiniDodgeDirection.Down : MiniDodgeDirection.Up, horizontal, Opposite(horizontal)];
        var sx = home.Width + Approach + Gap;
        var sy = home.Height + Approach + Gap;
        foreach (var direction in order)
        {
            var (x, y) = direction switch
            {
                MiniDodgeDirection.Left => (-sx, 0),
                MiniDodgeDirection.Right => (sx, 0),
                MiniDodgeDirection.Up => (0, -sy),
                _ => (0, sy),
            };
            var bounds = new Rectangle(home.Left + x, home.Top + y, home.Right + x, home.Bottom + y);
            if (workArea.Contains(bounds) && !Contains(Zone(bounds), cursor)) return new MiniDodgeChoice(direction, bounds);
        }

        return null;
    }

    private static MiniDodgeDirection Opposite(MiniDodgeDirection direction) => direction switch
    {
        MiniDodgeDirection.Left => MiniDodgeDirection.Right,
        MiniDodgeDirection.Right => MiniDodgeDirection.Left,
        MiniDodgeDirection.Up => MiniDodgeDirection.Down,
        _ => MiniDodgeDirection.Up,
    };
}

public enum MiniDodgeAction { None, Dodge, Return }

/// <summary>What one cursor reading asks the window to do; <see cref="Choice"/> is set for a dodge.</summary>
public readonly record struct MiniDodgeStep(MiniDodgeAction Action, MiniDodgeChoice? Choice = null);

/// <summary>
/// The dodge state (CieLinux <c>MiniDodger</c>, without its polling): dodges when the cursor enters home's
/// zone, takes another side when followed to the dodged spot, and returns once the cursor has stayed away
/// from both spots for <see cref="MiniDodge.ReturnDelay"/>. Each step it returns is already committed; the
/// caller only moves the window. Never touches the saved position: home is whatever the caller passes.
/// </summary>
public sealed class MiniDodger
{
    // When the cursor was first seen away from both spots; null while it is near one.
    private DateTimeOffset? _awaySince;

    public bool Dodged => Aside is not null;

    /// <summary>The dodged bounds the window is at, or gliding to; null at home.</summary>
    public Rectangle? Aside { get; private set; }

    /// <summary>Forgets the dodge (a hotkey move, a re-placement, a failure): the caller places the window itself.</summary>
    public void Cancel()
    {
        Aside = null;
        _awaySince = null;
    }

    /// <param name="home">Where the window belongs.</param>
    /// <param name="cursor">The cursor; null when unknown (counts as away).</param>
    /// <param name="workArea">The work area the dodged window must fit in; null: no dodge.</param>
    /// <param name="now">The clock, for the return delay.</param>
    public MiniDodgeStep Update(Rectangle home, ScreenPoint? cursor, Rectangle? workArea, DateTimeOffset now)
    {
        var nearHome = cursor is { } c && MiniDodge.Contains(MiniDodge.Zone(home), c);
        if (Aside is not { } aside)
        {
            return nearHome ? Dodge(home, cursor!.Value, workArea) : default;
        }

        if (cursor is { } followed && MiniDodge.Contains(MiniDodge.Zone(aside), followed))
        {
            // Followed to the new spot: another side, chosen from home again; with none left and home
            // clear of the cursor, home.
            _awaySince = null;
            var step = Dodge(home, followed, workArea);
            return step.Action == MiniDodgeAction.None && !nearHome ? Return() : step;
        }

        if (nearHome)
        {
            // Still looking behind the window.
            _awaySince = null;
            return default;
        }

        _awaySince ??= now;
        return now - _awaySince.Value >= MiniDodge.ReturnDelay ? Return() : default;
    }

    private MiniDodgeStep Dodge(Rectangle home, ScreenPoint cursor, Rectangle? workArea)
    {
        if (workArea is not { } area || MiniDodge.Choose(home, cursor, area) is not { } choice) return default;
        Aside = choice.Bounds;
        _awaySince = null;
        return new MiniDodgeStep(MiniDodgeAction.Dodge, choice);
    }

    private MiniDodgeStep Return()
    {
        Cancel();
        return new MiniDodgeStep(MiniDodgeAction.Return);
    }
}
