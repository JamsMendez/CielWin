using CielWin.App.Alerts;
using CielWin.Interop;

namespace CielWin.App.Composition;

/// <summary>
/// Turns accepted HTTP alert commands into show/hide calls on whichever <see cref="ISceneSurface"/> is
/// active: one <see cref="AlertQueue"/>, advanced on the UI thread, holding an alert while the surface
/// cannot be seen and ending it when its duration runs out. A newly displayed alert plays its sound once.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Accept"/> and <see cref="Clear"/> run on the HTTP server thread; <see cref="Update"/> and
/// <see cref="SurfaceReplaced"/> on the UI thread. The queue is shared between them under one lock.
/// </para>
/// <para>
/// Alerts are told apart by <see cref="ActiveAlert.Id"/>: a held warning resuming after a failed alert
/// comes back with its own id (shown again for the rest of its hold, never sounded twice) and a fresh
/// <see cref="ActiveAlert.StartedAt"/>, so a showing held warning is
/// <c>active.Command.IsHeld</c>, and a show or resume is the tick where the displayed id changes.
/// </para>
/// </remarks>
internal sealed class AlertDriver(
    TimeProvider clock, Func<PrimaryDisplayInfo> readDisplay, IAlertSoundPlayer sounds, Func<bool> soundsEnabled,
    Action<string> trace, TimeSpan? holdMax = null)
{
    private readonly object _gate = new();
    private readonly AlertQueue _queue = new(onDiagnostic: trace, holdMax: holdMax);
    private long? _displayed;

    // The last alert whose sound was played (or attempted), and the last such held warning: a held
    // warning outlives any number of failed alerts preempting it (the queue keeps at most one alive), so
    // its resume must never count as new, however many sounded in between.
    private long? _sounded;
    private long? _soundedHeld;

    /// <summary>
    /// Parses and queues one command. The reply goes back to the HTTP caller: <c>"ok id=&lt;n&gt;"</c>
    /// when it shows or waits, plain <c>"ok"</c> when it is ignored (busy), <c>"error: ..."</c> when it
    /// does not parse.
    /// </summary>
    public string Accept(string text)
    {
        var parsed = AlertCommandParser.Parse(text);
        if (parsed.Command is not { } command)
        {
            var error = parsed.Error ?? "alert command could not be parsed";
            trace($"alert rejected: {error}");
            return AlertReplyProtocol.FormatError(error);
        }

        long id;
        lock (_gate)
        {
            id = _queue.Enqueue(command, clock.GetUtcNow());
        }

        // An ignored request keeps the plain "ok": it has no id to clear.
        return id > 0 ? AlertHttpProtocol.FormatAccepted(id) : AlertReplyProtocol.OkReply;
    }

    /// <summary>
    /// POST /v1/alerts/clear: clears alert <paramref name="id"/>, or with <see langword="null"/> the held
    /// warning. Always <c>"ok"</c>, whether or not anything was cleared; the next <see cref="Update"/>
    /// hides it.
    /// </summary>
    public string Clear(int? id)
    {
        lock (_gate)
        {
            _queue.Clear(id, clock.GetUtcNow());
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

        if (surface is null || active?.Id == _displayed)
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

        // The deadline was set by the queue (promotion, or the request for a held warning), not when a
        // renderer picked it up.
        var remaining = active.EndsAt - clock.GetUtcNow();
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

        _displayed = active.Id;
        PlaySoundOnce(active);
    }

    /// <summary>
    /// Failed wins over warning. Attempted at most once per alert: a throwing player is traced and
    /// not retried, and never takes the alert down with it. An alert shown while muted counts as
    /// sounded, so unmuting never plays it on a later re-show. A held warning resuming after failed
    /// alerts is not played again; one preempted before it ever showed plays when it first shows.
    /// </summary>
    private void PlaySoundOnce(ActiveAlert active)
    {
        if (active.Id == _sounded || active.Id == _soundedHeld)
        {
            return;
        }

        _sounded = active.Id;
        if (active.Command.IsHeld)
        {
            _soundedHeld = active.Id;
        }

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
