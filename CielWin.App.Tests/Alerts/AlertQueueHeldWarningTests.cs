using CielWin.App.Alerts;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// H1 held warnings in <see cref="AlertQueue"/>: request ids, <c>duration:0</c> held until cleared or
/// the hold max (counted from the request), a failed request preempting and resuming a held warning,
/// and <see cref="AlertQueue.Clear"/>. Mirrors CieLinux's H1 queue contract tests (commit 2f0e3e6).
/// </summary>
public sealed class AlertQueueHeldWarningTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    private readonly List<string> _diagnostics = [];

    private static DateTimeOffset At(long ms) => Epoch.AddMilliseconds(ms);

    private static AlertCommand Parse(string text) => AlertCommandParser.Parse(text).Command!;

    private AlertQueue Create(TimeSpan? maxAge = null, TimeSpan? holdMax = null) =>
        new(maxAge, _diagnostics.Add, holdMax);

    private static void AssertActive(ActiveAlert? active, long id, long startedMs, long endsMs)
    {
        Assert.NotNull(active);
        Assert.Equal(id, active!.Id);
        Assert.Equal(At(startedMs), active.StartedAt);
        Assert.Equal(At(endsMs), active.EndsAt);
    }

    [Fact]
    public void Ids_GrowPerAcceptedRequest_AndAnIgnoredRequestGetsZeroAndUsesNoId()
    {
        var queue = Create();

        Assert.Equal(1, queue.Enqueue(Parse("warning:1 duration:1"), At(0)));
        Assert.Equal(0, queue.Enqueue(Parse("failed:1"), At(0)));
        AssertActive(queue.Advance(At(0), desktopVisible: true), 1, 0, 1000);
        Assert.Equal(0, queue.Enqueue(Parse("failed:1"), At(500)));
        Assert.Equal(2, queue.Enqueue(Parse("failed:2"), At(1000)));
        AssertActive(queue.Advance(At(1000), desktopVisible: true), 2, 1000, 6000);

        Assert.Equal(
            ["alert ignored: one is already waiting to show", "alert ignored: one is already showing"],
            _diagnostics);
    }

    [Fact]
    public void AHeldWarning_LastsUntilTheHoldMax_CountedFromTheRequest()
    {
        var queue = Create(holdMax: TimeSpan.FromSeconds(60));
        Assert.Equal(1, queue.Enqueue(Parse("warning:1 duration:0"), At(0)));

        var active = queue.Advance(At(0), desktopVisible: true);
        AssertActive(active, 1, 0, 60000);
        Assert.True(active!.Command.IsHeld);
        AssertActive(queue.Advance(At(59999), desktopVisible: true), 1, 0, 60000);
        Assert.Null(queue.Advance(At(60000), desktopVisible: true));
    }

    [Fact]
    public void AHeldWarningHeldBackWhileCovered_StillEndsAtTheHoldMaxFromItsRequest()
    {
        var queue = Create(holdMax: TimeSpan.FromSeconds(60));
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));

        Assert.Null(queue.Advance(At(0), desktopVisible: false));
        AssertActive(queue.Advance(At(20000), desktopVisible: true), 1, 20000, 60000);
        Assert.Null(queue.Advance(At(60000), desktopVisible: true));
    }

    [Fact]
    public void AHeldWarningWaitingPastItsHoldMax_IsDroppedThen_BeforeTheStartLimit()
    {
        var queue = Create(holdMax: TimeSpan.FromSeconds(60));
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));

        Assert.Null(queue.Advance(At(59999), desktopVisible: false));
        Assert.Null(queue.Advance(At(60000), desktopVisible: false));
        Assert.Null(queue.Advance(At(60001), desktopVisible: true));

        Assert.Equal(["alert dropped: held past the 00:01:00 hold max"], _diagnostics);
    }

    [Fact]
    public void WithTheDefaultHoldMax_TheFiveMinuteStartLimitDropsAWaitingHeldWarningFirst()
    {
        var dropped = Create();
        dropped.Enqueue(Parse("warning:1 duration:0"), At(0));
        Assert.Null(dropped.Advance(At(300001), desktopVisible: true));
        Assert.Equal(["alert dropped: waited longer than the 00:05:00 max age without starting"], _diagnostics);

        var started = Create();
        started.Enqueue(Parse("warning:1 duration:0"), At(0));
        AssertActive(started.Advance(At(300000), desktopVisible: true), 1, 300000, 600000);
    }

    [Fact]
    public void ANegativeHoldMax_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlertQueue(TimeSpan.Zero, null, TimeSpan.FromMilliseconds(-1)));

    [Fact]
    public void TheDefaultHoldMax_IsTenMinutes() => Assert.Equal(TimeSpan.FromMinutes(10), AlertQueue.DefaultHoldMax);

    [Fact]
    public void AFailedRequest_PreemptsAHeldWarning_WhichResumesForTheRestOfItsHold()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        AssertActive(queue.Advance(At(0), desktopVisible: true), 1, 0, 600000);

        Assert.Equal(2, queue.Enqueue(Parse("failed:1 duration:2"), At(1000)));
        AssertActive(queue.Advance(At(1000), desktopVisible: true), 2, 1000, 3000);
        AssertActive(queue.Advance(At(2999), desktopVisible: true), 2, 1000, 3000);
        AssertActive(queue.Advance(At(3000), desktopVisible: true), 1, 3000, 600000);
        Assert.Null(queue.Advance(At(600000), desktopVisible: true));

        Assert.Equal(["alert 1 suspended: a failed alert preempts it"], _diagnostics);
    }

    [Fact]
    public void AMixedRequestPreemptsToo_AWarningOnlyOneIsIgnored()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);

        Assert.Equal(0, queue.Enqueue(Parse("warning:2"), At(10)));
        Assert.Equal(0, queue.Enqueue(Parse("warning:1 duration:0"), At(20)));
        Assert.Equal(2, queue.Enqueue(Parse("warning:1 failed:1"), At(30)));
        AssertActive(queue.Advance(At(30), desktopVisible: true), 2, 30, 5030);

        Assert.Equal(
            [
                "alert ignored: one is already showing",
                "alert ignored: one is already showing",
                "alert 1 suspended: a failed alert preempts it",
            ],
            _diagnostics);
    }

    [Fact]
    public void ASuspendedHeldWarning_ExpiresOnItsOwnDeadline_WhileTheFailedAlertShows()
    {
        var queue = Create(maxAge: TimeSpan.FromMinutes(5), holdMax: TimeSpan.FromSeconds(4));
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);
        queue.Enqueue(Parse("failed:1"), At(1000));
        queue.Advance(At(1000), desktopVisible: true);

        AssertActive(queue.Advance(At(4000), desktopVisible: true), 2, 1000, 6000);
        Assert.Null(queue.Advance(At(6000), desktopVisible: true));

        Assert.Equal(
            ["alert 1 suspended: a failed alert preempts it", "alert dropped: held past the 00:00:04 hold max"],
            _diagnostics);
    }

    [Fact]
    public void AHeldWarningStillWaiting_GivesWayToAFailedRequest_ThenFollowsIt()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        Assert.Null(queue.Advance(At(0), desktopVisible: false));

        Assert.Equal(2, queue.Enqueue(Parse("failed:1"), At(1000)));
        AssertActive(queue.Advance(At(2000), desktopVisible: true), 2, 2000, 7000);
        AssertActive(queue.Advance(At(7000), desktopVisible: true), 1, 7000, 600000);
    }

    [Fact]
    public void AHeldRequestWhileAnotherHeldWarningIsSuspended_IsIgnored_SoOnlyOneIsEverAlive()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);
        queue.Enqueue(Parse("failed:1 duration:2"), At(1000));
        AssertActive(queue.Advance(At(1000), desktopVisible: true), 2, 1000, 3000);

        Assert.Equal(0, queue.Enqueue(Parse("warning:1 duration:0"), At(1500)));
        AssertActive(queue.Advance(At(3000), desktopVisible: true), 1, 3000, 600000);
        Assert.Null(queue.Advance(At(600000), desktopVisible: true));

        Assert.Equal(
            [
                "alert 1 suspended: a failed alert preempts it",
                "alert ignored: a held warning is already suspended",
            ],
            _diagnostics);
    }

    [Fact]
    public void AFailedRequestAfterAnIgnoredHeldOne_PreemptsTheSameHeldWarningAgain_WhichStillResumes()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);
        queue.Enqueue(Parse("failed:1 duration:2"), At(1000));
        queue.Advance(At(1000), desktopVisible: true);
        queue.Enqueue(Parse("warning:1 duration:0"), At(1500));
        AssertActive(queue.Advance(At(3000), desktopVisible: true), 1, 3000, 600000);

        Assert.Equal(3, queue.Enqueue(Parse("failed:1 duration:2"), At(4000)));
        AssertActive(queue.Advance(At(4000), desktopVisible: true), 3, 4000, 6000);
        AssertActive(queue.Advance(At(6000), desktopVisible: true), 1, 6000, 600000);

        Assert.Equal(
            [
                "alert 1 suspended: a failed alert preempts it",
                "alert ignored: a held warning is already suspended",
                "alert 1 suspended: a failed alert preempts it",
            ],
            _diagnostics);
    }

    [Fact]
    public void AHeldRequestDuringATimedAlert_WaitsForIt_AndASecondOneIsIgnored()
    {
        var queue = Create();
        queue.Enqueue(Parse("failed:1 duration:2"), At(0));
        queue.Advance(At(0), desktopVisible: true);

        Assert.Equal(2, queue.Enqueue(Parse("warning:1 duration:0"), At(1000)));
        Assert.Equal(0, queue.Enqueue(Parse("warning:1 duration:0"), At(1500)));
        AssertActive(queue.Advance(At(1000), desktopVisible: true), 1, 0, 2000);
        AssertActive(queue.Advance(At(2000), desktopVisible: true), 2, 2000, 601000);

        Assert.Equal(["alert ignored: one is already waiting to show"], _diagnostics);
    }

    [Fact]
    public void ClearWithoutAnId_ClearsTheShowingHeldWarning()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);

        queue.Clear(null, At(10));

        Assert.Null(queue.Advance(At(10), desktopVisible: true));
        Assert.Equal(["alert 1 cleared"], _diagnostics);
    }

    [Fact]
    public void ClearWithAnId_ClearsOnlyThatAlert()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);

        queue.Clear(2, At(10));
        Assert.Empty(_diagnostics);
        queue.Clear(1, At(10));

        Assert.Null(queue.Advance(At(10), desktopVisible: true));
        Assert.Equal(["alert 1 cleared"], _diagnostics);
    }

    [Fact]
    public void ClearWithoutAnId_NeverClearsATimedAlert_ItsIdDoes()
    {
        var queue = Create();
        queue.Enqueue(Parse("failed:1"), At(0));
        queue.Advance(At(0), desktopVisible: true);

        queue.Clear(null, At(10));
        AssertActive(queue.Advance(At(10), desktopVisible: true), 1, 0, 5000);
        queue.Clear(1, At(20));

        Assert.Null(queue.Advance(At(20), desktopVisible: true));
        Assert.Equal(["alert 1 cleared"], _diagnostics);
    }

    [Fact]
    public void ASuspendedHeldWarningClearedMeanwhile_DoesNotComeBack()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);
        queue.Enqueue(Parse("failed:1"), At(1000));
        queue.Advance(At(1000), desktopVisible: true);

        queue.Clear(null, At(2000));

        AssertActive(queue.Advance(At(2000), desktopVisible: true), 2, 1000, 6000);
        Assert.Null(queue.Advance(At(6000), desktopVisible: true));
        Assert.Equal(["alert 1 suspended: a failed alert preempts it", "alert 1 cleared"], _diagnostics);
    }

    [Fact]
    public void ClearingThePreemptingFailedAlertById_ResumesTheHeldWarningAtOnce()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        queue.Advance(At(0), desktopVisible: true);
        queue.Enqueue(Parse("failed:1"), At(1000));
        queue.Advance(At(1000), desktopVisible: true);

        queue.Clear(2, At(2000));

        AssertActive(queue.Advance(At(2000), desktopVisible: true), 1, 2000, 600000);
        Assert.Equal(["alert 1 suspended: a failed alert preempts it", "alert 2 cleared"], _diagnostics);
    }

    [Fact]
    public void AWaitingHeldWarningIsClearedToo_AndAnUnknownIdOrNothingHeldIsASilentNoOp()
    {
        var queue = Create();
        queue.Enqueue(Parse("warning:1 duration:0"), At(0));
        Assert.Null(queue.Advance(At(0), desktopVisible: false));

        queue.Clear(null, At(10));
        Assert.Null(queue.Advance(At(20), desktopVisible: true));
        queue.Clear(null, At(30));
        queue.Clear(9, At(30));

        Assert.Equal(["alert 1 cleared"], _diagnostics);
    }
}
