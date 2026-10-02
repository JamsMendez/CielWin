namespace CielWin.App.Alerts;

/// <summary>
/// The PURE state machine behind the permanently preloaded scene layer -- host identity/backoff tracking, readiness,
/// and the one pending show a caller may register before the page is ready to receive it.
/// </summary>
/// <remarks>
/// One controller is created at startup and kept alive; alerts are shown and hidden inside the page
/// instead of recreating a WebView2 per alert (a fresh environment/controller costs seconds).
/// This class owns every decision that does not need a real WebView2 to make, so it is unit-tested
/// directly -- <see cref="WebViewAlertLayerController"/> is the thin WebView2-specific shell driven
/// by it (real environment/controller/navigation stay a hardware-only concern).
/// <para>
/// A preloaded controller must NOT be torn down when an alert's duration ends -- only <see cref="Failed"/> (creation/process
/// failure) or a host identity change ever calls for that, both handled by the controller itself.
/// </para>
/// </remarks>
public sealed class AlertLayerPreloadState(Func<DateTimeOffset>? clock = null)
{
    /// <summary>At most this many runtime failures are recovered from in one burst; a renderer that keeps dying gives up instead of looping.</summary>
    public const int MaxRecoveries = 2;

    /// <summary>A runtime failure at least this long after the previous one starts a new burst, so a layer that ran stably this long gets its full budget back.</summary>
    public static readonly TimeSpan StableWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A navigation that has neither completed nor failed after this long is hung (a local-folder page
    /// loads in well under a second; this only has to outlast a cold WebView2 on a busy machine).
    /// </summary>
    public static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private int _recoveries;
    private DateTimeOffset? _lastRuntimeFailureAt;
    private DateTimeOffset? _navigationDeadline;
    private bool _hostKnown;
    private nint _hwnd;
    private int _generation;
    private DateTimeOffset _retryAfter;
    private int _failures;
    private (AlertShowRequest Request, DateTimeOffset Deadline)? _pending;
    private (AlertShowRequest Request, DateTimeOffset Deadline)? _shown;

    /// <summary>Environment + controller created, navigation completed, and the page's own "ready" message received.</summary>
    public bool Ready { get; private set; }

    /// <summary>The layer is currently supposed to be shown (<c>IsVisible = true</c> on the real controller).</summary>
    public bool Visible { get; private set; }

    /// <summary>The recovery budget is spent: the layer stays down until the host changes (see <see cref="RuntimeFailed"/>).</summary>
    public bool GaveUp { get; private set; }

    /// <summary>Whether creation/recreation may be attempted right now -- false while backing off after <see cref="Failed"/>, and for good once <see cref="GaveUp"/>.</summary>
    public bool CanCreate => !GaveUp && _clock() >= _retryAfter;

    /// <summary>A navigation was started and neither completed nor failed within <see cref="NavigationTimeout"/>.</summary>
    public bool NavigationTimedOut => _navigationDeadline is { } deadline && _clock() >= deadline;

    /// <summary>
    /// True the FIRST time it is called (nothing was known yet), and again every time the host's
    /// hwnd/generation actually differs from what was last observed -- an Explorer restart, or a
    /// host that was just attached for the first time. False when the identity is unchanged.
    /// </summary>
    public bool HostChanged(nint hwnd, int generation)
    {
        if (_hostKnown && hwnd == _hwnd && generation == _generation) return false;
        _hostKnown = true;
        _hwnd = hwnd;
        _generation = generation;
        // A new host (first attach, Explorer restart) is a fresh surface: failures on the previous
        // one say nothing about it, so a layer that gave up gets another go.
        _recoveries = 0;
        _lastRuntimeFailureAt = null;
        GaveUp = false;
        return true;
    }

    /// <summary>Creation or the live process failed: not ready, not visible, and back off exponentially before the next attempt (500ms, 1000ms, ... capped at 8000ms).</summary>
    public void Failed()
    {
        // Requeue a showing alert BEFORE clearing Visible: ControllerLost keys on Visible, and the
        // controller calls Failed() ahead of its teardown, so clearing it here dropped the alert.
        ControllerLost();
        _retryAfter = _clock().AddMilliseconds(Math.Min(8000, 500 * (1 << Math.Min(_failures++, 4))));
    }

    /// <summary>
    /// The live layer failed AFTER it was created (the WebView2 process died, or a navigation hung or
    /// failed). Backs off like <see cref="Failed"/> and returns true while the bounded budget
    /// (<see cref="MaxRecoveries"/> per <see cref="StableWindow"/>) allows another recreate; once spent,
    /// returns false and <see cref="CanCreate"/> stays false so a persistently broken renderer does not
    /// loop forever. Creation failures keep using <see cref="Failed"/> alone (backoff, no cap).
    /// </summary>
    public bool RuntimeFailed()
    {
        var now = _clock();
        if (_lastRuntimeFailureAt is { } last && now - last >= StableWindow) _recoveries = 0;
        _lastRuntimeFailureAt = now;
        Failed();
        if (_recoveries >= MaxRecoveries)
        {
            GaveUp = true;
            return false;
        }

        _recoveries++;
        return true;
    }

    /// <summary>A host navigation began (creation or scene switch): arms <see cref="NavigationTimedOut"/>, restarting it when one was already armed.</summary>
    public void NavigationStarted() => _navigationDeadline = _clock() + NavigationTimeout;

    /// <summary>The live navigation completed (successfully or not): nothing is hung any more.</summary>
    public void NavigationFinished() => _navigationDeadline = null;

    /// <summary>A creation attempt succeeded well enough to proceed to navigation: resets the backoff so a LATER failure starts counting from the first step again.</summary>
    public void Created() => _failures = 0;

    /// <summary>The page's own "ready" message arrived after a completed navigation.</summary>
    public void MarkReady() => Ready = true;

    /// <summary>
    /// A start was requested. While ready, returns the request to post RIGHT NOW and marks the layer
    /// visible -- even if it was already visible (a Start while showing must still re-show, not be
    /// swallowed as a no-op; the queue, not this class, decides whether that Start was warranted).
    /// While not yet ready, remembers it as a pending show with an ABSOLUTE deadline and returns null;
    /// see <see cref="ApplyPendingShowIfDue"/>.
    /// </summary>
    public AlertShowRequest? RequestShow(AlertShowRequest request)
    {
        if (Ready)
        {
            Visible = true;
            _pending = null;
            _shown = (request, _clock().AddMilliseconds(request.DurationMilliseconds));
            return request;
        }

        _pending = (request, _clock().AddMilliseconds(request.DurationMilliseconds));
        return null;
    }

    /// <summary>
    /// Call once <see cref="MarkReady"/> has just made the page ready: applies a still-pending show
    /// by returning a copy carrying its REMAINING duration (never the original one; every other field
    /// -- tiles, grid, gap -- passes through unchanged), or drops it silently if its deadline already
    /// passed. Returns null when there was nothing to apply.
    /// </summary>
    public AlertShowRequest? ApplyPendingShowIfDue()
    {
        if (_pending is not { } pending) return null;
        _pending = null;
        if (!Ready) return null;
        var remaining = pending.Deadline - _clock();
        if (remaining <= TimeSpan.Zero) return null;
        Visible = true;
        _shown = pending;
        return pending.Request with { DurationMilliseconds = (int)Math.Ceiling(remaining.TotalMilliseconds) };
    }

    /// <summary>
    /// The queue ended the alert: hides the layer and drops any not-yet-applied pending show, WITHOUT
    /// touching <see cref="Ready"/> -- hiding a preloaded layer must never close/tear it down.
    /// </summary>
    public void Hide()
    {
        Visible = false;
        _pending = null;
        _shown = null;
    }

    /// <summary>The page signaled its own "done": the controller reports nothing else back -- the queue owns ending the alert (it calls <see cref="Hide"/> itself once it advances).</summary>
    public void PageDone()
    {
        Visible = false;
        _shown = null;
    }

    /// <summary>
    /// The controller backing this layer was just torn down (host identity change, lost composition
    /// surface, or a creation/process/navigation failure) while its replacement is being created: no
    /// longer ready. If an alert was actually being shown, it is requeued as pending for the
    /// REMAINING time of its ORIGINAL deadline -- so a later <see cref="ApplyPendingShowIfDue"/> once
    /// the new controller is ready re-shows it (or drops it if that deadline already passed), instead
    /// of leaving it stuck "visible" on a controller that no longer exists. Never touches the backoff
    /// tracked by <see cref="Failed"/> -- that is a separate decision the controller makes for itself.
    /// </summary>
    public void ControllerLost()
    {
        Ready = false;
        _navigationDeadline = null;
        if (Visible && _shown is { } shown) _pending = shown;
        Visible = false;
        _shown = null;
    }

    /// <summary>
    /// alert-survives-scene-switch: the host is about to navigate the SAME controller to another page
    /// (a scene switch). The new page has not loaded yet, so the layer is no longer ready, and an alert
    /// being shown is requeued exactly as <see cref="ControllerLost"/> does -- the new page re-shows it
    /// for its remaining time once ready, or drops it if that deadline passes first.
    /// </summary>
    public void PageReloading() => ControllerLost();
}
