using CielWin.App.Alerts;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// The bounded self-recovery decisions of the preloaded scene layer, tested on the pure
/// <see cref="AlertLayerPreloadState"/> so no real WebView2 is needed: how many runtime failures are
/// recovered from, when the budget refills, and when a navigation counts as hung.
/// </summary>
public sealed class AlertLayerRecoveryTests
{
    private static AlertShowRequest Show(int durationMilliseconds) => new(["warning"], 1, 1, 8, durationMilliseconds);

    [Fact]
    public void RuntimeFailures_AreRecoveredFromTwiceThenTheLayerStaysDown()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);

        Assert.True(state.RuntimeFailed());
        now = now.AddSeconds(30);
        Assert.True(state.RuntimeFailed());
        now = now.AddSeconds(30);
        Assert.False(state.RuntimeFailed());

        Assert.True(state.GaveUp);
        now = now.AddMinutes(5);
        Assert.False(state.CanCreate, "a layer that gave up must not be recreated by the poll");
    }

    [Fact]
    public void RuntimeFailed_WhenRecoverable_BacksOffLikeAnyOtherFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);

        state.RuntimeFailed();

        Assert.False(state.CanCreate);
        now = now.AddMilliseconds(500);
        Assert.True(state.CanCreate);
    }

    [Fact]
    public void AFailureAfterAStableWindow_StartsANewBurstWithTheFullBudget()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);
        state.RuntimeFailed();
        now = now.AddSeconds(30);
        state.RuntimeFailed();

        now = now.Add(AlertLayerPreloadState.StableWindow);

        Assert.True(state.RuntimeFailed());
        Assert.False(state.GaveUp);
    }

    [Fact]
    public void AHostChange_RefillsTheBudgetAndLiftsGiveUp()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);
        state.HostChanged(1, 1);
        state.RuntimeFailed();
        state.RuntimeFailed();
        Assert.False(state.RuntimeFailed());

        Assert.True(state.HostChanged(1, 2));
        now = now.AddSeconds(10);

        Assert.False(state.GaveUp);
        Assert.True(state.CanCreate);
        Assert.True(state.RuntimeFailed());
    }

    [Fact]
    public void GivingUp_StillRequeuesTheAlertThatWasShowing_ForTheNextHost()
    {
        var state = new AlertLayerPreloadState();
        state.MarkReady();
        state.RequestShow(Show(60_000));
        state.RuntimeFailed();
        state.RuntimeFailed();
        state.RuntimeFailed();

        Assert.False(state.Ready);
        state.MarkReady();
        Assert.NotNull(state.ApplyPendingShowIfDue());
    }

    [Fact]
    public void Failed_WhileAnAlertIsShowing_KeepsItForTheReplacementController()
    {
        // The controller calls Failed() and THEN tears down; Failed() used to clear Visible first, so
        // the teardown's requeue (which keys on Visible) silently dropped the alert on screen.
        var state = new AlertLayerPreloadState();
        state.MarkReady();
        state.RequestShow(Show(60_000));

        state.Failed();
        state.ControllerLost();
        state.MarkReady();

        var reshown = state.ApplyPendingShowIfDue();
        Assert.NotNull(reshown);
        Assert.True(state.Visible);
    }

    [Fact]
    public void ANavigationThatNeverCompletes_TimesOutAfterTheBound()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);
        Assert.False(state.NavigationTimedOut, "nothing is navigating yet");

        state.NavigationStarted();
        now = now.Add(AlertLayerPreloadState.NavigationTimeout - TimeSpan.FromMilliseconds(1));
        Assert.False(state.NavigationTimedOut);

        now = now.AddMilliseconds(1);
        Assert.True(state.NavigationTimedOut);
    }

    [Fact]
    public void ACompletedNavigation_NeverTimesOut()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);
        state.NavigationStarted();

        state.NavigationFinished();
        now = now.AddHours(1);

        Assert.False(state.NavigationTimedOut);
    }

    [Fact]
    public void ALostController_CancelsTheNavigationDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);
        state.NavigationStarted();

        state.ControllerLost();
        now = now.AddHours(1);

        Assert.False(state.NavigationTimedOut);
    }

    [Fact]
    public void ARestartedNavigation_RestartsTheDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AlertLayerPreloadState(() => now);
        state.NavigationStarted();
        now = now.Add(AlertLayerPreloadState.NavigationTimeout).AddSeconds(-1);

        state.NavigationStarted(); // scene switch navigates the same controller again
        now = now.AddSeconds(2);

        Assert.False(state.NavigationTimedOut);
    }
}
