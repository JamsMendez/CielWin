namespace CielWin.App.Alerts;

/// <summary>
/// One alert on screen: which command it is, and when its display window started, so the overlay
/// can animate from that instant.
/// </summary>
/// <param name="StartedAt">When it was shown: its promotion, or for a held warning, also each resume.</param>
/// <param name="Id">
/// The request's id from <see cref="AlertQueue.Enqueue"/>. A re-show, or a held warning resuming after
/// a failed alert, keeps it, so a caller can tell "the same alert again" from a new one.
/// </param>
/// <param name="EndsAt">When it ends: start + duration, or for a held warning, request + hold max.</param>
public sealed record ActiveAlert(AlertCommand Command, DateTimeOffset StartedAt, long Id, DateTimeOffset EndsAt);

/// <summary>
/// Decides which single parsed alert command -- if any -- is on screen or waiting to be, right now.
/// </summary>
/// <remarks>
/// <para>
/// Pure and time-free by design: every method takes <c>now</c> from the
/// caller rather than reading the clock or owning a timer. <see cref="Advance"/> is
/// meant to be called once per tick, alongside whether the desktop is currently visible, by the
/// overlay driver.
/// </para>
/// <para>
/// Busy-ignore: at most ONE alert exists at a
/// time, showing or waiting -- never more. <see cref="Enqueue"/> judges "in progress" against
/// <paramref name="now"/> at call time, not against state only <see cref="Advance"/> clears: a new
/// request is ignored (still reported as accepted to the caller, per the maintainer's decision that
/// the HTTP reply status never reveals the difference) only while an alert is CURRENTLY showing
/// (<c>now</c> has not yet reached its end) or CURRENTLY waiting and not expired.
/// A request arriving once the showing alert's window has elapsed, or once the waiting one has aged
/// past <see cref="_maxAge"/>, is accepted and queued even though nobody has called
/// <see cref="Advance"/> to notice that yet -- it shows on the very next <see cref="Advance"/> call.
/// Every ignore and every drop is reported through <see cref="_onDiagnostic"/> so the caller can log
/// which happened and why.
/// </para>
/// <para>
/// Queued-while-covered behaviour: an alert that arrives while a fullscreen
/// window covers the desktop is queued rather than shown or dropped outright. It starts once the
/// desktop is visible again, unless it has waited longer than <see cref="_maxAge"/> since it was
/// enqueued, in which case <see cref="Advance"/> drops it -- reporting the drop -- the first time it
/// notices, whether or not the desktop is visible at that moment. Exactly <c>maxAge</c> is still
/// eligible; only strictly past it is dropped. An alert already showing when the desktop becomes
/// covered keeps its own clock running: it simply ends on time, it is never paused or extended, and
/// <see cref="Advance"/> keeps returning it as the active alert regardless of visibility until then --
/// only STARTING a new one requires the desktop to be visible.
/// </para>
/// <para>
/// Held warnings (H1, <see cref="AlertCommand.IsHeld"/>) end at their request time plus
/// <see cref="_holdMax"/>, wherever they are -- showing, waiting or suspended -- so one never outlives
/// a crashed caller; a waiting one is still dropped by the max age too, whichever comes first. A held
/// request while a timed alert shows waits for it. A request with any failed tile while a held
/// warning shows or waits PREEMPTS it: the held warning is suspended and comes back, same id, for the
/// rest of its hold once nothing else shows or waits. So besides the single slot there is at most one
/// more alert, a suspended held one. <see cref="Clear"/> removes an alert by id, or the held one.
/// </para>
/// <para>
/// NOT thread-safe: <see cref="Enqueue"/>, <see cref="Clear"/> and <see cref="Advance"/> all read and
/// mutate the same slots with no locking, by design -- the caller is expected to serialize every call.
/// </para>
/// </remarks>
public sealed class AlertQueue
{
    /// <summary>Default ceiling on how long a queued alert may wait, from enqueue time, before it is dropped unshown.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(5);

    /// <summary>Default ceiling on how long a held warning lasts, from its request, without being cleared.</summary>
    public static readonly TimeSpan DefaultHoldMax = TimeSpan.FromMinutes(10);

    private readonly TimeSpan _maxAge;
    private readonly TimeSpan _holdMax;
    private readonly Action<string> _onDiagnostic;

    // The single slot, not started yet; and a held warning a failed alert took the place of.
    private Request? _pending;
    private Request? _suspended;
    private ActiveAlert? _current;
    private long _nextId = 1;

    /// <param name="maxAge">
    /// How long a queued alert may wait before it is dropped unshown; defaults to <see
    /// cref="DefaultMaxAge"/> when omitted. <see cref="TimeSpan.Zero"/> is allowed and means "must
    /// start on the same tick it was enqueued, or be dropped" -- <see cref="DropExpired"/>'s
    /// strictly-greater-than check still leaves an alert exactly at its max age eligible, so a zero
    /// max age does not make every enqueue pointless.
    /// </param>
    /// <param name="onDiagnostic">
    /// Told about every ignore, drop, suspension and clear, with a short message naming the reason, so
    /// the caller can log it. Never called with anything else, and never expected to throw. Defaults to
    /// a no-op, the same optional-delegate convention the composition root uses.
    /// </param>
    /// <param name="holdMax">
    /// How long a held warning lasts, counted from its request; defaults to <see cref="DefaultHoldMax"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The resolved <paramref name="maxAge"/> or <paramref name="holdMax"/> is negative.
    /// </exception>
    public AlertQueue(TimeSpan? maxAge = null, Action<string>? onDiagnostic = null, TimeSpan? holdMax = null)
    {
        var resolvedMaxAge = maxAge ?? DefaultMaxAge;
        if (resolvedMaxAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), resolvedMaxAge, "Max age must not be negative.");
        }

        var resolvedHoldMax = holdMax ?? DefaultHoldMax;
        if (resolvedHoldMax < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(holdMax), resolvedHoldMax, "Hold max must not be negative.");
        }

        _maxAge = resolvedMaxAge;
        _holdMax = resolvedHoldMax;
        _onDiagnostic = onDiagnostic ?? (_ => { });
    }

    /// <summary>
    /// Queues <paramref name="command"/>, unless an alert is already showing or waiting at
    /// <paramref name="now"/>, in which case the request is IGNORED: reported through
    /// <see cref="_onDiagnostic"/> and dropped. Two exceptions to the busy rule: a held request waits
    /// behind a showing timed alert, and a request with a failed tile preempts a showing or waiting
    /// held warning, suspending it.
    /// </summary>
    /// <returns>The request's id (1, 2, ... per queue) when it was queued; 0 when it was ignored.</returns>
    /// <remarks>
    /// Drops any already-expired waiting alert first (reusing <see cref="DropExpired"/>, so that drop
    /// is still reported) before judging whether the queue is busy -- an alert nobody would ever
    /// start again must not swallow a new request.
    /// </remarks>
    public long Enqueue(AlertCommand command, DateTimeOffset now)
    {
        DropExpired(now);

        if (_current is { } active && now < active.EndsAt)
        {
            if (active.Command.IsHeld && command.HasFailed)
            {
                // A failure is interesting exactly while a question waits: it takes the held warning's
                // place, which resumes for the rest of its hold once the failed alert ends.
                _onDiagnostic($"alert {active.Id} suspended: a failed alert preempts it");
                _suspended = new Request(active.Command, active.Id, active.StartedAt, active.EndsAt);
                _current = null;
            }
            else if (active.Command.IsHeld || !command.IsHeld)
            {
                _onDiagnostic("alert ignored: one is already showing");
                return 0;
            }
        }

        if (_pending is { } waiting)
        {
            // A held warning still waiting to start gives way to a failed request the same way.
            if (!waiting.Command.IsHeld || !command.HasFailed)
            {
                _onDiagnostic("alert ignored: one is already waiting to show");
                return 0;
            }

            _suspended = waiting;
        }

        // A held alert's deadline runs from the request, so it never outlives a crashed caller.
        _pending = new Request(command, _nextId, now, now + _holdMax);
        return _nextId++;
    }

    /// <summary>
    /// Clears alert <paramref name="id"/> (held or timed), or with <see langword="null"/> the held
    /// warning whatever its id (never a timed alert), wherever it is: showing, suspended or waiting.
    /// Anything else -- an unknown or finished id, nothing held -- is a silent no-op.
    /// </summary>
    public void Clear(long? id, DateTimeOffset now)
    {
        DropExpired(now);

        if (_current is { } active && Matches(active.Command, active.Id))
        {
            _onDiagnostic($"alert {active.Id} cleared");
            _current = null;
        }

        if (_suspended is { } suspended && Matches(suspended.Command, suspended.Id))
        {
            _onDiagnostic($"alert {suspended.Id} cleared");
            _suspended = null;
        }

        if (_pending is { } pending && Matches(pending.Command, pending.Id))
        {
            _onDiagnostic($"alert {pending.Id} cleared");
            _pending = null;
        }

        bool Matches(AlertCommand command, long candidate) => id is { } wanted ? candidate == wanted : command.IsHeld;
    }

    /// <summary>
    /// Ends the current alert once its display window has elapsed, drops anything waiting past its max
    /// age or hold max, and -- if nothing is currently showing and the desktop is visible -- starts the
    /// waiting alert, or else resumes a suspended held warning.
    /// </summary>
    /// <returns>The alert that should be on screen right now, or <see langword="null"/> for none.</returns>
    public ActiveAlert? Advance(DateTimeOffset now, bool desktopVisible)
    {
        if (_current is { } active && now >= active.EndsAt)
        {
            _current = null;
        }

        DropExpired(now);

        if (_current is not null || !desktopVisible)
        {
            return _current;
        }

        if (_pending is { } pending)
        {
            _pending = null;
            var endsAt = pending.Command.IsHeld ? pending.HoldEndsAt : now + pending.Command.Duration;
            _current = new ActiveAlert(pending.Command, now, pending.Id, endsAt);
        }
        else if (_suspended is { } suspended)
        {
            // Resumed: same id, same deadline, shown again from now.
            _suspended = null;
            _current = new ActiveAlert(suspended.Command, now, suspended.Id, suspended.HoldEndsAt);
        }

        return _current;
    }

    /// <summary>
    /// Drops the waiting alert once it has waited strictly longer than <see cref="_maxAge"/> since it
    /// was enqueued, and a waiting or suspended held warning once its hold max (counted from its
    /// request) is reached -- whichever comes first.
    /// </summary>
    private void DropExpired(DateTimeOffset now)
    {
        if (_pending is { Command.IsHeld: true } heldPending && now >= heldPending.HoldEndsAt)
        {
            _pending = null;
            _onDiagnostic($"alert dropped: held past the {_holdMax} hold max");
        }

        if (_pending is { } pending && now - pending.RequestedAt > _maxAge)
        {
            _pending = null;
            _onDiagnostic($"alert dropped: waited longer than the {_maxAge} max age without starting");
        }

        if (_suspended is { } suspended && now >= suspended.HoldEndsAt)
        {
            _suspended = null;
            _onDiagnostic($"alert dropped: held past the {_holdMax} hold max");
        }
    }

    /// <param name="HoldEndsAt">Request + hold max; only a held warning reads it.</param>
    private sealed record Request(AlertCommand Command, long Id, DateTimeOffset RequestedAt, DateTimeOffset HoldEndsAt);
}
