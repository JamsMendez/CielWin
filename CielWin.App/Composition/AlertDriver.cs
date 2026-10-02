using CielWin.App.Alerts;
using CielWin.Interop;

namespace CielWin.App.Composition;

/// <summary>
/// Turns accepted HTTP alert commands into show/hide calls on whichever <see cref="ISceneSurface"/> is
/// active: one <see cref="AlertQueue"/>, advanced on the UI thread, holding an alert while the surface
/// cannot be seen and ending it when its duration runs out. A newly displayed alert plays its sound once.
/// </summary>
/// <remarks>
/// <see cref="Accept"/> runs on the HTTP server thread; <see cref="Update"/> and
/// <see cref="SurfaceReplaced"/> on the UI thread. The queue is shared between them under one lock.
/// </remarks>
internal sealed class AlertDriver(
    TimeProvider clock, Func<PrimaryDisplayInfo> readDisplay, IAlertSoundPlayer sounds, Func<bool> soundsEnabled,
    Action<string> trace)
{
    private readonly object _gate = new();
    private readonly AlertQueue _queue = new(onDiagnostic: trace);
    private ActiveAlert? _displayed;

    // The last alert whose sound was played (or attempted). The queue builds a new ActiveAlert per
    // promoted command, so the same instance coming back means a re-show after a surface change.
    private ActiveAlert? _sounded;

    /// <summary>Parses and queues one command; the reply goes back to the HTTP caller.</summary>
    public string Accept(string text)
    {
        var parsed = AlertCommandParser.Parse(text);
        if (parsed.Command is not { } command)
        {
            var error = parsed.Error ?? "alert command could not be parsed";
            trace($"alert rejected: {error}");
            return AlertReplyProtocol.FormatError(error);
        }

        lock (_gate)
        {
            _queue.Enqueue(command, clock.GetUtcNow());
        }

        return AlertReplyProtocol.OkReply;
    }

    /// <summary>
    /// The surface changed (mode switch): nothing is on the new one yet, so an alert still inside its
    /// duration is shown again there, for its remaining time, by the next <see cref="Update"/>.
    /// </summary>
    public void SurfaceReplaced() => _displayed = null;

    public void Update(ISceneSurface? surface, bool primaryMonitorCovered)
    {
        var visible = surface?.CanShowAlerts(primaryMonitorCovered) ?? false;
        ActiveAlert? active;
        lock (_gate)
        {
            active = _queue.Advance(clock.GetUtcNow(), visible);
        }

        if (surface is null || ReferenceEquals(active, _displayed))
        {
            return;
        }

        if (_displayed is not null)
        {
            // Nothing stays marked displayed until a start below succeeds, so a failed start is
            // retried next tick and a failed hide is never repeated.
            _displayed = null;
            try
            {
                surface.HideAlert();
            }
            catch (Exception error)
            {
                trace($"alert hide-failed error={error.GetType().Name}");
            }
        }

        if (active is null)
        {
            return;
        }

        // The deadline started when the queue promoted the command, not when a renderer picked it up.
        var remaining = active.Command.Duration - (clock.GetUtcNow() - active.StartedAt);
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            surface.ShowAlert(BuildRequest(active.Command, remaining));
        }
        catch (Exception error)
        {
            trace($"alert start-failed error={error.GetType().Name}");
            return;
        }

        _displayed = active;
        PlaySoundOnce(active);
    }

    /// <summary>
    /// Failed wins over warning. Attempted at most once per alert: a throwing player is traced and
    /// not retried, and never takes the alert down with it. An alert shown while muted counts as
    /// sounded, so unmuting never plays it on a later re-show.
    /// </summary>
    private void PlaySoundOnce(ActiveAlert active)
    {
        if (ReferenceEquals(active, _sounded))
        {
            return;
        }

        _sounded = active;
        if (!soundsEnabled())
        {
            return;
        }

        var kind = active.Command.Groups.Any(group => group.Kind == AlertKind.Failed) ? AlertKind.Failed : AlertKind.Warning;
        try
        {
            sounds.Play(kind);
        }
        catch (Exception error)
        {
            trace($"alert sound-failed error={error.GetType().Name}");
        }
    }

    private AlertShowRequest BuildRequest(AlertCommand command, TimeSpan remaining)
    {
        var layout = AlertTileLayout.From(command);
        var tiles = layout.Tiles.Select(kind => kind == AlertKind.Failed ? "failed" : "warning").ToArray();

        // Read at show time: the taskbar can move between startup and an alert. A failed read lays
        // the mosaic out on the whole canvas rather than losing the alert.
        AlertLayerWorkArea workArea;
        try
        {
            var display = readDisplay();
            workArea = AlertLayerWorkArea.Resolve(display.Bounds, display.WorkArea);
        }
        catch (Exception error)
        {
            trace($"alert workarea-failed error={error.GetType().Name}");
            workArea = AlertLayerWorkArea.Unavailable;
        }

        return new AlertShowRequest(
            tiles, layout.Columns, layout.Rows, AlertTileLayout.GapPixels,
            Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds)),
            workArea.Left, workArea.Top, workArea.Width, workArea.Height);
    }
}
