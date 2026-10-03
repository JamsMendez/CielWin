using CielWin.App.Composition;
using CielWin.App.Input;
using CielWin.App.Tests.Composition;
using CielWin.App.Wallpaper;
using CielWin.Interop;

namespace CielWin.App.Tests.Wallpaper;

/// <summary>
/// <c>scene-mini</c> mode: the mini window at the persisted <c>mini-position</c> of the primary work
/// area, scene switches and alerts routed to it, Alt+M / Alt+Shift+M walking it around the eight
/// positions and persisting each step.
/// </summary>
public sealed class MiniModeWiringTests
{
    private static Settings Mini(MiniPosition position = MiniPosition.TopRight, WallpaperScene scene = WallpaperScene.Processing) =>
        new(WallpaperMode: WallpaperMode.SceneMini, MiniPosition: position, WallpaperScene: scene);

    private static Rectangle PlacementFor(MiniPosition position, PrimaryDisplayInfo display) =>
        MiniWindowPlacement.Compute(display.WorkArea, display.Bounds.Height, position);

    [Theory]
    [InlineData(MiniPosition.TopLeft)]
    [InlineData(MiniPosition.BottomCenter)]
    [InlineData(MiniPosition.LeftCenter)]
    public void Startup_ShowsTheWindowAtThePersistedPositionOfThePrimaryWorkArea(MiniPosition position)
    {
        var harness = new CompositionHarness();

        using var composition = harness.Wire(Mini(position));

        var show = Assert.Single(harness.Mini.Shows);
        Assert.Equal(PlacementFor(position, harness.Display), show.Bounds);
    }

    [Fact]
    public void Startup_ShowsOnTheUiThread_NotInlineDuringWiring()
    {
        var harness = new CompositionHarness { DeferUi = true };

        using var composition = harness.Wire(Mini());
        Assert.Empty(harness.Minis);

        harness.RunUi();

        Assert.Single(harness.Mini.Shows);
    }

    [Fact]
    public void Startup_WhenShowFails_TracesAndKeepsRunningWithoutRetrying()
    {
        var harness = new CompositionHarness { MiniShowResult = false };

        using var composition = harness.Wire(Mini());
        harness.Tick();

        Assert.Single(harness.Mini.Shows);
        Assert.Contains(harness.Trace, line => line.Contains("mini-window shown=False", StringComparison.Ordinal));
    }

    [Fact]
    public void HttpSceneSwitch_SwitchesTheMiniWindowAndPersists()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini());

        Assert.True(harness.Server.Options.HandleSceneSwitch("raphael"));

        Assert.Equal([WallpaperScene.Raphael], harness.Mini.Switches);
        Assert.Equal(WallpaperScene.Raphael, harness.Saves[^1].WallpaperScene);
    }

    [Fact]
    public void HttpSceneSwitch_WhenTheWindowRefuses_DoesNotPersist()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini());
        harness.Mini.SwitchResult = false;

        harness.Server.Options.HandleSceneSwitch("idle");

        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void HttpSceneSwitch_WhenTheWindowFailedToShow_NeitherSwitchesNorPersists()
    {
        var harness = new CompositionHarness { MiniShowResult = false };
        using var composition = harness.Wire(Mini());

        harness.Server.Options.HandleSceneSwitch("idle");

        Assert.Empty(harness.Mini.Switches);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void Alerts_AreRoutedToTheMiniWindow_AndHiddenWhenTheyExpire()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini());

        harness.Server.Options.HandleAlert("failed:1 duration:1");
        Assert.Single(harness.Mini.Alerts);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Tick();

        Assert.Equal(1, harness.Mini.Hides);
    }

    [Fact]
    public void Alerts_WhileTheBrowserIsNotReady_AreHeldUntilItIs()
    {
        var harness = new CompositionHarness { MiniReady = false };
        using var composition = harness.Wire(Mini());

        harness.Server.Options.HandleAlert("warning:1");
        Assert.Empty(harness.Mini.Alerts);

        harness.MiniReady = true;
        harness.Tick();

        Assert.Single(harness.Mini.Alerts);
    }

    [Fact]
    public void Alerts_ACoveredPrimaryMonitorDoesNotHoldThem_TheMiniWindowIsTopmost()
    {
        var harness = new CompositionHarness { Covered = true };
        using var composition = harness.Wire(Mini());

        harness.Server.Options.HandleAlert("warning:1");

        Assert.Single(harness.Mini.Alerts);
    }

    [Fact]
    public void AltM_CyclesAllEightPositionsClockwise_AndPersistsEachStep()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));

        var expected = new List<MiniPosition>();
        var position = MiniPosition.TopLeft;
        for (var i = 0; i < 8; i++)
        {
            position = MiniWindowPlacement.Next(position);
            expected.Add(position);
            harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        }

        Assert.Equal(expected.Select(p => PlacementFor(p, harness.Display)), harness.Mini.Moves);
        Assert.Equal(expected, harness.Saves.Select(s => s.MiniPosition));
        Assert.Equal(MiniPosition.TopLeft, harness.Saves[^1].MiniPosition);
    }

    [Fact]
    public void AltShiftM_CyclesCounterClockwise_AndPersists()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));

        harness.Hotkeys.Press(MiniPositionHotkeys.CounterClockwiseId);
        harness.Hotkeys.Press(MiniPositionHotkeys.CounterClockwiseId);

        Assert.Equal(
            [PlacementFor(MiniPosition.LeftCenter, harness.Display), PlacementFor(MiniPosition.BottomLeft, harness.Display)],
            harness.Mini.Moves);
        Assert.Equal([MiniPosition.LeftCenter, MiniPosition.BottomLeft], harness.Saves.Select(s => s.MiniPosition));
    }

    [Fact]
    public void AltM_ThenAltShiftM_ReturnsToTheStartingPosition()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.RightCenter));

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        harness.Hotkeys.Press(MiniPositionHotkeys.CounterClockwiseId);

        Assert.Equal(MiniPosition.RightCenter, harness.Saves[^1].MiniPosition);
    }

    [Fact]
    public void AltM_PlacesOnTheWorkAreaThatIsTrueNow_NotTheOneSeenAtStartup()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));
        var moved = new PrimaryDisplayInfo(new Rectangle(0, 0, 2560, 1440), new Rectangle(0, 0, 2560, 1392));
        harness.Display = moved;

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Equal(PlacementFor(MiniPosition.TopCenter, moved), harness.Mini.Moves[^1]);
    }

    [Fact]
    public void AltM_RunsTheMoveOnTheUiThread_NotInsideTheRawMessage()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));
        harness.DeferUi = true;

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        Assert.Empty(harness.Mini.Moves);

        harness.RunUi();

        Assert.Single(harness.Mini.Moves);
    }

    [Fact]
    public void AltM_InSceneMode_IsANoOpWithATrace()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(new Settings(WallpaperMode: WallpaperMode.Scene));

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Empty(harness.Saves);
        Assert.Contains(harness.Trace, line => line.Contains("mini-position ignored reason=not-mini-mode", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AltM_WhenTheWindowFailedToShowOrIsNotReady_NeitherMovesNorPersists(bool shown, bool ready)
    {
        var harness = new CompositionHarness { MiniShowResult = shown, MiniReady = ready };
        using var composition = harness.Wire(Mini());

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Empty(harness.Mini.Moves);
        Assert.Empty(harness.Saves);
    }

    /// <summary>
    /// The move runs as a posted dispatcher operation: a throw there would be an unhandled dispatcher
    /// exception and take the whole app down, so it is traced and the position is not persisted.
    /// </summary>
    [Fact]
    public void AltM_WhenTheDisplayReadThrows_TracesAndNeitherMovesNorPersists()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));
        harness.DisplayOverride = () => throw new InvalidOperationException("no monitor");

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Empty(harness.Mini.Moves);
        Assert.Empty(harness.Saves);
        Assert.Contains(harness.Trace, line => line.Contains("mini-position move-failed error=InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public void AltM_WhenTheWindowMoveThrows_TracesDoesNotPersist_AndTheNextPressStillWorks()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));
        harness.Mini.ThrowOnMove = true;

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Empty(harness.Saves);
        Assert.Contains(harness.Trace, line => line.Contains("mini-position move-failed error=InvalidOperationException", StringComparison.Ordinal));

        harness.Mini.ThrowOnMove = false;
        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Equal(MiniPosition.TopCenter, harness.Saves[^1].MiniPosition);
    }

    /// <summary>
    /// A move that throws after the native window already moved leaves the real window away from the
    /// confirmed position. Nothing is persisted, and the next tick puts the window back where the
    /// confirmed (persisted) position says it is, instead of trusting the pre-move placement.
    /// </summary>
    [Fact]
    public void AltM_WhenTheWindowMoveThrowsAfterMoving_DoesNotPersist_AndTheNextTickRestoresTheConfirmedPlacement()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));
        var confirmed = harness.Mini.Shows[0].Bounds;
        harness.Mini.ThrowAfterMove = true;

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);

        Assert.Empty(harness.Saves);
        Assert.Contains(harness.Trace, line => line.Contains("mini-position move-failed error=InvalidOperationException", StringComparison.Ordinal));
        Assert.NotEqual(confirmed, harness.Mini.Moves[^1]);

        harness.Mini.ThrowAfterMove = false;
        harness.Tick();

        Assert.Equal(confirmed, harness.Mini.Moves[^1]);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void AnUnknownHotkeyId_IsIgnored()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini());

        harness.Hotkeys.Press(99);

        Assert.Empty(harness.Mini.Moves);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void AWorkAreaChange_ReplacesTheWindowOnTheNextTick_WithoutPersisting()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.BottomRight));
        harness.Tick();
        Assert.Empty(harness.Mini.Moves);

        var moved = new PrimaryDisplayInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1000));
        harness.Display = moved;
        harness.Tick();
        harness.Tick();

        Assert.Equal([PlacementFor(MiniPosition.BottomRight, moved)], harness.Mini.Moves);
        Assert.Empty(harness.Mini.Glides);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public void AltM_GlidesTheWindowToTheNextPosition()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        harness.Hotkeys.Press(MiniPositionHotkeys.CounterClockwiseId);

        Assert.Equal(
            [PlacementFor(MiniPosition.TopCenter, harness.Display), PlacementFor(MiniPosition.TopLeft, harness.Display)],
            harness.Mini.Glides);
    }

    /// <summary>
    /// The surface records the glide TARGET as placed: a tick mid-glide sees nothing to re-place and lets
    /// the glide run, instead of snapping the window back.
    /// </summary>
    [Fact]
    public void ATickDuringAGlideDoesNotInterruptIt()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));

        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        harness.Tick();

        Assert.Single(harness.Mini.Moves);
    }

    /// <summary>
    /// A glide that fails to land leaves the window somewhere unknown: the window reports its placement
    /// lost, and the next tick re-places it at the confirmed position (persisted as before, not again).
    /// </summary>
    [Fact]
    public void AGlideThatFailsToLand_IsRePlacedAtTheConfirmedPositionOnTheNextTick()
    {
        var harness = new CompositionHarness();
        using var composition = harness.Wire(Mini(MiniPosition.TopLeft));
        harness.Hotkeys.Press(MiniPositionHotkeys.ClockwiseId);
        var saves = harness.Saves.Count;

        harness.Mini.RaisePlacementLost();
        harness.Tick();

        Assert.Equal(2, harness.Mini.Moves.Count);
        Assert.Equal(PlacementFor(MiniPosition.TopCenter, harness.Display), harness.Mini.Moves[^1]);
        Assert.Single(harness.Mini.Glides);
        Assert.Equal(saves, harness.Saves.Count);
    }
}
