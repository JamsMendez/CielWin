using CielWin.App.Alerts;
using CielWin.App.Composition;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// H1 through <see cref="AlertDriver"/>: replies carry the request id, a held warning shows for the
/// rest of its hold and ends on clear or the hold max, and a failed alert preempts it, after which it
/// comes back for the rest of its hold without replaying its sound. H4: while it shows, its warning
/// sound repeats every 5 s, restarting from each show or resume. Mirrors CieLinux's H1 and H4 driver
/// contract tests (commit 2f0e3e6).
/// </summary>
public sealed class AlertDriverHeldWarningTests
{
    private readonly ManualClock _clock = new();
    private readonly FakeAlertSoundPlayer _sounds = new();
    private readonly List<string> _trace = [];
    private readonly RecordingSurface _surface = new();
    private readonly DateTimeOffset _start;
    private bool _soundsEnabled = true;

    public AlertDriverHeldWarningTests() => _start = _clock.GetUtcNow();

    private AlertDriver Create(TimeSpan? holdMax = null) =>
        new(_clock, () => CompositionHarness.DefaultDisplay, _sounds, () => _soundsEnabled, _trace.Add, holdMax);

    private void UpdateAt(AlertDriver driver, double atSeconds)
    {
        // Seconds since the test's start, so a script reads like CieLinux's clock lines.
        _clock.Advance(_start + TimeSpan.FromSeconds(atSeconds) - _clock.GetUtcNow());
        Update(driver);
    }

    private void Update(AlertDriver driver) => driver.Update(_surface, primaryMonitorCovered: false);

    [Fact]
    public void Accept_RepliesWithTheId_AndAnIgnoredRequestRepliesPlainOk()
    {
        var driver = Create();

        Assert.Equal("ok id=1", driver.Accept("warning:1 duration:1"));
        Update(driver);
        Assert.Equal("ok", driver.Accept("failed:1"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Update(driver);
        Assert.Equal("ok id=2", driver.Accept("failed:1"));
    }

    [Fact]
    public void ADurationZeroFailedCommand_IsRejectedWithTheParserError()
    {
        var driver = Create();

        Assert.Equal("error: 'duration:0' requires warning only", driver.Accept("failed:1 duration:0"));
        Assert.Contains("alert rejected: 'duration:0' requires warning only", _trace);
    }

    [Fact]
    public void AHeldWarning_ShowsForTheRemainingHold_IsAnnouncedOnce_AndEndsOnClear()
    {
        var driver = Create();
        Assert.Equal("ok id=1", driver.Accept("warning:1 duration:0"));
        Update(driver);
        _clock.Advance(TimeSpan.FromSeconds(4));
        Update(driver);

        Assert.Equal("ok", driver.Clear(null));
        Update(driver);
        Assert.Equal("ok", driver.Clear(null));

        Assert.Equal([600000], _surface.Shows.Select(show => show.DurationMilliseconds));
        Assert.Equal(1, _surface.Hides);
        Assert.Equal([AlertKind.Warning], _sounds.Played);
        Assert.Single(_trace, line => line == "alert 1 cleared");
    }

    [Fact]
    public void AHeldWarning_EndsAtTheHoldMax()
    {
        var driver = Create(TimeSpan.FromSeconds(60));
        driver.Accept("warning:1 duration:0");
        Update(driver);
        _clock.Advance(TimeSpan.FromMilliseconds(59999));
        Update(driver);
        Assert.Equal(0, _surface.Hides);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Update(driver);

        Assert.Equal([60000], _surface.Shows.Select(show => show.DurationMilliseconds));
        Assert.Equal(1, _surface.Hides);
    }

    [Fact]
    public void AFailedAlert_PreemptsAHeldWarning_WhichResumesWithoutReplayingItsSound()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("ok id=2", driver.Accept("failed:1 duration:2"));
        Update(driver);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Update(driver);
        Update(driver);

        Assert.Equal(
            ["warning 600000", "failed 2000", "warning 597000"],
            _surface.Shows.Select(show => $"{string.Join(',', show.Tiles)} {show.DurationMilliseconds}"));
        Assert.Equal(2, _surface.Hides);
        Assert.Equal([AlertKind.Warning, AlertKind.Failed], _sounds.Played);
        Assert.Contains("alert 1 suspended: a failed alert preempts it", _trace);
    }

    [Fact]
    public void AHeldWarningPreemptedTwice_NeverReplaysItsSoundOnEitherResume()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);

        for (var preemption = 0; preemption < 2; preemption++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            driver.Accept("failed:1 duration:1");
            Update(driver);
            _clock.Advance(TimeSpan.FromSeconds(1));
            Update(driver);
        }

        Assert.Equal(
            ["warning 600000", "failed 1000", "warning 598000", "failed 1000", "warning 596000"],
            _surface.Shows.Select(show => $"{string.Join(',', show.Tiles)} {show.DurationMilliseconds}"));
        Assert.Equal([AlertKind.Warning, AlertKind.Failed, AlertKind.Failed], _sounds.Played);
    }

    [Fact]
    public void AHeldWarningNeverShownBeforeAFailedAlertTookItsPlace_IsAnnouncedWhenItFinallyShows()
    {
        var driver = Create();
        _surface.CanShow = false;
        driver.Accept("warning:1 duration:0");
        Update(driver);
        driver.Accept("failed:1");
        _surface.CanShow = true;
        Update(driver);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Update(driver);

        Assert.Equal([5000, 595000], _surface.Shows.Select(show => show.DurationMilliseconds));
        Assert.Equal([AlertKind.Failed, AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_AShowingHeldWarning_RepeatsItsSoundEvery5Seconds_UntilCleared()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);
        UpdateAt(driver, 4.999);
        Assert.Equal([AlertKind.Warning], _sounds.Played);

        UpdateAt(driver, 5);
        UpdateAt(driver, 9);
        UpdateAt(driver, 10);
        driver.Clear(null);
        Update(driver);
        UpdateAt(driver, 20);

        Assert.Equal([AlertKind.Warning, AlertKind.Warning, AlertKind.Warning], _sounds.Played);
        Assert.Single(_surface.Shows);
        Assert.Equal(1, _surface.Hides);
    }

    [Fact]
    public void H4_TheHoldMax_EndsTheRepeats()
    {
        var driver = Create(TimeSpan.FromSeconds(60));
        driver.Accept("warning:1 duration:0");
        Update(driver);
        UpdateAt(driver, 58);
        UpdateAt(driver, 60);
        UpdateAt(driver, 70);

        Assert.Equal([AlertKind.Warning, AlertKind.Warning], _sounds.Played);
        Assert.Equal(1, _surface.Hides);
    }

    [Fact]
    public void H4_ATimedAlert_PlaysItsSoundOnceOnly()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:20");
        Update(driver);
        UpdateAt(driver, 5);
        UpdateAt(driver, 10);

        Assert.Equal([AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_NoRepeatWhileSuspended_AndTheCadenceRestartsFromTheResume()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);
        _clock.Advance(TimeSpan.FromSeconds(1));
        driver.Accept("failed:1 duration:10");
        Update(driver);
        UpdateAt(driver, 5);
        UpdateAt(driver, 11);
        UpdateAt(driver, 15.999);
        Assert.Equal([AlertKind.Warning, AlertKind.Failed], _sounds.Played);

        UpdateAt(driver, 16);

        Assert.Equal(
            ["warning 600000", "failed 10000", "warning 589000"],
            _surface.Shows.Select(show => $"{string.Join(',', show.Tiles)} {show.DurationMilliseconds}"));
        Assert.Equal([AlertKind.Warning, AlertKind.Failed, AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_NoRepeatWhileCovered_AndTheCadenceRestartsFromTheUncover()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);
        _surface.CanShow = false;
        UpdateAt(driver, 5);
        UpdateAt(driver, 10);
        _surface.CanShow = true;
        UpdateAt(driver, 12);
        UpdateAt(driver, 16.999);
        Assert.Equal([AlertKind.Warning], _sounds.Played);

        UpdateAt(driver, 17);

        Assert.Single(_surface.Shows);
        Assert.Equal(0, _surface.Hides);
        Assert.Equal([AlertKind.Warning, AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_AHeldWarningWaitingWhileCovered_FirstRepeats5SecondsAfterItShows()
    {
        var driver = Create();
        _surface.CanShow = false;
        driver.Accept("warning:1 duration:0");
        Update(driver);
        UpdateAt(driver, 10);
        _surface.CanShow = true;
        Update(driver);
        UpdateAt(driver, 14.999);
        Assert.Equal([AlertKind.Warning], _sounds.Played);

        UpdateAt(driver, 15);

        Assert.Equal([590000], _surface.Shows.Select(show => show.DurationMilliseconds));
        Assert.Equal([AlertKind.Warning, AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_AReShowAfterASurfaceChange_RestartsTheCadenceWithoutPlaying()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);
        _clock.Advance(TimeSpan.FromSeconds(3));
        driver.SurfaceReplaced();
        Update(driver);
        UpdateAt(driver, 7.999);
        Assert.Equal([AlertKind.Warning], _sounds.Played);

        UpdateAt(driver, 8);

        Assert.Equal(2, _surface.Shows.Count);
        Assert.Equal([AlertKind.Warning, AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_ARepeatPlaysOnlyWhenSoundsAreOnAtRepeatTime()
    {
        var driver = Create();
        _soundsEnabled = false;
        driver.Accept("warning:1 duration:0");
        Update(driver);
        UpdateAt(driver, 5);
        Assert.Empty(_sounds.Played);

        _soundsEnabled = true;
        UpdateAt(driver, 10);

        Assert.Equal([AlertKind.Warning], _sounds.Played);
    }

    [Fact]
    public void H4_AThrowingPlayerOnARepeat_IsTraced_AndTheCadenceGoesOn()
    {
        var driver = Create();
        driver.Accept("warning:1 duration:0");
        Update(driver);
        _sounds.Throws = true;
        UpdateAt(driver, 5);
        _sounds.Throws = false;
        UpdateAt(driver, 9.999);
        UpdateAt(driver, 10);

        Assert.Contains("alert sound-failed error=InvalidOperationException", _trace);
        Assert.Equal([AlertKind.Warning, AlertKind.Warning], _sounds.Played);
        Assert.Equal(0, _surface.Hides);
    }

    private sealed class RecordingSurface : ISceneSurface
    {
        public List<AlertShowRequest> Shows { get; } = [];
        public int Hides { get; private set; }
        public bool CanShow { get; set; } = true;

        public void Start() { }
        public bool SwitchScene(WallpaperScene scene) => true;
        public bool CanShowAlerts(bool primaryMonitorCovered) => CanShow;
        public void ShowAlert(AlertShowRequest request) => Shows.Add(request);
        public void HideAlert() => Hides++;
        public void Tick(bool primaryMonitorCovered) { }
        public void Dispose() { }
    }
}
