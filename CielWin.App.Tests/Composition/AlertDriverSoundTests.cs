using CielWin.App.Alerts;
using CielWin.App.Composition;
using CielWin.Interop;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// <see cref="AlertDriver"/>'s alert sound: exactly one per newly displayed alert (failed wins over
/// warning), never on a re-show of the same alert after a surface change, never for an alert that
/// did not show, and a throwing player never costs the alert itself.
/// </summary>
public sealed class AlertDriverSoundTests
{
    private readonly ManualClock _clock = new();
    private readonly FakeAlertSoundPlayer _sounds = new();
    private readonly List<string> _trace = [];
    private readonly FakeSurface _surface = new();
    private bool _soundsEnabled = true;

    private AlertDriver Create(TimeProvider? clock = null) =>
        new(clock ?? _clock, () => CompositionHarness.DefaultDisplay, _sounds, () => _soundsEnabled, _trace.Add);

    [Fact]
    public void ANewAlert_PlaysItsSoundOnce()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:10");

        driver.Update(_surface, primaryMonitorCovered: false);
        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Single(_surface.Shows);
        Assert.Equal([AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void AFailedOnlyAlert_PlaysTheFailedSound()
    {
        var driver = Create();
        driver.Accept("failed:2");

        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Equal([AlertKind.Failed], _sounds.Played);
    }

    [Fact]
    public void AnAlertWithBothKinds_PlaysOnlyTheFailedSound()
    {
        var driver = Create();
        driver.Accept("warning:3 failed:1");

        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Equal([AlertKind.Failed], _sounds.Played);
    }

    [Fact]
    public void TheSameAlertShownAgainAfterASurfaceChange_DoesNotPlayAgain()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:10");
        driver.Update(_surface, primaryMonitorCovered: false);

        driver.SurfaceReplaced();
        var replacement = new FakeSurface();
        driver.Update(replacement, primaryMonitorCovered: false);

        Assert.Single(replacement.Shows);
        Assert.Equal([AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void ALaterAlert_PlaysAgain()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:1");
        driver.Update(_surface, primaryMonitorCovered: false);
        _clock.Advance(TimeSpan.FromSeconds(1));
        driver.Update(_surface, primaryMonitorCovered: false);

        driver.Accept("failed:1");
        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Equal([AlertKind.Warning, AlertKind.Failed], _sounds.Played);
    }

    [Fact]
    public void AnAlertHeldWhileTheSurfaceCannotShowIt_PlaysNothingUntilItShows()
    {
        var driver = Create();
        _surface.CanShow = false;
        driver.Accept("warning:1");

        driver.Update(_surface, primaryMonitorCovered: false);
        Assert.Empty(_sounds.Played);

        _surface.CanShow = true;
        driver.Update(_surface, primaryMonitorCovered: false);
        Assert.Equal([AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void AFailedShow_PlaysNothing_AndTheRetryThatShowsPlaysOnce()
    {
        var driver = Create();
        _surface.ShowFailuresLeft = 1;
        driver.Accept("warning:1");

        driver.Update(_surface, primaryMonitorCovered: false);
        Assert.Empty(_sounds.Played);

        driver.Update(_surface, primaryMonitorCovered: false);
        Assert.Single(_surface.Shows);
        Assert.Equal([AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void AnAlertWhoseTimeRanOutBeforeItCouldShow_PlaysNothing()
    {
        // Every clock read moves one second: the alert is promoted on one read and has no time left
        // on the next, the remaining-time check the driver makes right before showing.
        var clock = new SteppingClock(TimeSpan.FromSeconds(1));
        var driver = Create(clock);
        driver.Accept("warning:1 duration:1");

        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Empty(_surface.Shows);
        Assert.Empty(_sounds.Played);
    }

    [Fact]
    public void AThrowingPlayer_IsTracedByTypeName_AndTheAlertStillShows()
    {
        var driver = Create();
        _sounds.Throws = true;
        driver.Accept("failed:1");

        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Single(_surface.Shows);
        Assert.Contains("alert sound-failed error=InvalidOperationException", _trace);
        Assert.DoesNotContain(_trace, line => line.Contains("no audio device", StringComparison.Ordinal));
    }

    [Fact]
    public void AThrowingPlayer_IsNotRetried_OnTheNextUpdate()
    {
        var driver = Create();
        _sounds.Throws = true;
        driver.Accept("failed:1 duration:10");
        driver.Update(_surface, primaryMonitorCovered: false);

        _sounds.Throws = false;
        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Empty(_sounds.Played);
        Assert.Single(_surface.Shows);
    }

    [Fact]
    public void Muted_PlaysNothing_AndTheAlertStillShows()
    {
        var driver = Create();
        _soundsEnabled = false;
        driver.Accept("failed:1");

        driver.Update(_surface, primaryMonitorCovered: false);

        Assert.Single(_surface.Shows);
        Assert.Empty(_sounds.Played);
    }

    [Fact]
    public void AnAlertShownWhileMuted_StaysSilent_WhenReshownAfterUnmuting()
    {
        var driver = Create();
        _soundsEnabled = false;
        driver.Accept("warning:1 duration:10");
        driver.Update(_surface, primaryMonitorCovered: false);

        _soundsEnabled = true;
        driver.SurfaceReplaced();
        driver.Update(new FakeSurface(), primaryMonitorCovered: false);

        Assert.Empty(_sounds.Played);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    public void H4_TheHeldWarningRepeat_UsesTheTicksOwnClockReads(int updates, int sounds)
    {
        // Every clock read moves one second (the accept takes one): shown on the second read of the
        // first update, the first repeat is due on the sixth update.
        var driver = Create(new SteppingClock(TimeSpan.FromSeconds(1)));
        driver.Accept("warning:1 duration:0");
        for (var update = 0; update < updates; update++)
        {
            driver.Update(_surface, primaryMonitorCovered: false);
        }

        Assert.Equal([598000], _surface.Shows.Select(show => show.DurationMilliseconds));
        Assert.Equal(sounds, _sounds.Played.Count);
    }

    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            var now = _now;
            _now += step;
            return now;
        }
    }

    private sealed class FakeSurface : ISceneSurface
    {
        public List<AlertShowRequest> Shows { get; } = [];
        public bool CanShow { get; set; } = true;
        public int ShowFailuresLeft { get; set; }

        public void Start() { }
        public bool SwitchScene(WallpaperScene scene) => true;
        public bool CanShowAlerts(bool primaryMonitorCovered) => CanShow;

        public void ShowAlert(AlertShowRequest request)
        {
            if (ShowFailuresLeft > 0)
            {
                ShowFailuresLeft--;
                throw new InvalidOperationException("show failed");
            }

            Shows.Add(request);
        }

        public void HideAlert() { }
        public void Tick(bool primaryMonitorCovered) { }
        public void Dispose() { }
    }
}
