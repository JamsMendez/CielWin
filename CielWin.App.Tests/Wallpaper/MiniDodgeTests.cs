using CielWin.App.Wallpaper;
using CielWin.Interop.Win32;
using InteropRectangle = CielWin.Interop.Rectangle;

namespace CielWin.App.Tests.Wallpaper;

public sealed class MiniDodgeTests
{
    private static readonly InteropRectangle WorkArea = new(0, 0, 2560, 1400);

    // Away from every edge, centre (1144, 644); a dodge shifts by 288 + 24 + 8 = 320.
    private static readonly InteropRectangle Middle = InteropRectangle.FromSize(1000, 500, 288, 288);

    private static readonly InteropRectangle TopRight = InteropRectangle.FromSize(2272, 0, 288, 288);

    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1300, 650, MiniDodgeDirection.Left, 680, 500)]  // cursor on the right
    [InlineData(990, 640, MiniDodgeDirection.Right, 1320, 500)] // cursor on the left
    [InlineData(1150, 490, MiniDodgeDirection.Down, 1000, 820)] // cursor above
    [InlineData(1150, 800, MiniDodgeDirection.Up, 1000, 180)]   // cursor below
    public void ChooseMovesAwayFromTheCursorAlongTheDominantAxisFarEnoughToClearTheZone(
        int x, int y, MiniDodgeDirection direction, int left, int top)
    {
        var choice = MiniDodge.Choose(Middle, new ScreenPoint(x, y), WorkArea);

        Assert.Equal(new MiniDodgeChoice(direction, InteropRectangle.FromSize(left, top, 288, 288)), choice);
        Assert.False(MiniDodge.Contains(MiniDodge.Zone(choice!.Value.Bounds), new ScreenPoint(x, y)));
    }

    [Fact]
    public void ChooseTakesAPerpendicularSideWhenThePreferredOneLeavesTheWorkArea()
    {
        // Cursor on the left of top-right: right does not fit; down (farther from the cursor's y) does.
        var choice = MiniDodge.Choose(TopRight, new ScreenPoint(2260, 144), WorkArea);

        Assert.Equal(new MiniDodgeChoice(MiniDodgeDirection.Down, InteropRectangle.FromSize(2272, 320, 288, 288)), choice);
    }

    [Fact]
    public void ChooseTriesThePerpendicularSideFartherFromTheCursorFirst()
    {
        var home = InteropRectangle.FromSize(2272, 500, 288, 288); // right-center, centre y 644
        Assert.Equal(MiniDodgeDirection.Up, MiniDodge.Choose(home, new ScreenPoint(2260, 700), WorkArea)!.Value.Direction);
        Assert.Equal(MiniDodgeDirection.Down, MiniDodge.Choose(home, new ScreenPoint(2260, 600), WorkArea)!.Value.Direction);
    }

    [Fact]
    public void ChooseStaysWhenNoSideFits()
    {
        var home = InteropRectangle.FromSize(0, 0, 288, 288);

        Assert.Null(MiniDodge.Choose(home, new ScreenPoint(300, 100), home));
    }

    [Fact]
    public void TheZoneIsTheWindowGrownByTheApproachMarginAndExcludesItsFarEdges()
    {
        var zone = MiniDodge.Zone(Middle);

        Assert.Equal(new InteropRectangle(976, 476, 1312, 812), zone);
        Assert.True(MiniDodge.Contains(zone, new ScreenPoint(976, 476)));
        Assert.False(MiniDodge.Contains(zone, new ScreenPoint(1312, 600)));
        Assert.False(MiniDodge.Contains(zone, new ScreenPoint(975, 600)));
    }

    [Fact]
    public void AFarCursorLeavesTheWindowHome()
    {
        var dodger = new MiniDodger();

        Assert.Equal(MiniDodgeAction.None, dodger.Update(Middle, new ScreenPoint(10, 10), WorkArea, T0).Action);
        Assert.False(dodger.Dodged);
    }

    [Fact]
    public void ANearCursorDodgesOnce()
    {
        var dodger = new MiniDodger();

        var step = dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0);

        Assert.Equal(MiniDodgeAction.Dodge, step.Action);
        Assert.Equal(MiniDodgeDirection.Left, step.Choice!.Value.Direction);
        Assert.True(dodger.Dodged);
        Assert.Equal(step.Choice.Value.Bounds, dodger.Aside);
        // Lingering over the spot it left keeps it aside, however long.
        Assert.Equal(MiniDodgeAction.None, dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0.AddSeconds(5)).Action);
        Assert.True(dodger.Dodged);
    }

    [Fact]
    public void ItReturnsOnceTheCursorHasStayedAwayForTheReturnDelay()
    {
        var dodger = new MiniDodger();
        dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0);
        var away = new ScreenPoint(2400, 1300);

        Assert.Equal(MiniDodgeAction.None, dodger.Update(Middle, away, WorkArea, T0.AddMilliseconds(100)).Action);
        Assert.Equal(MiniDodgeAction.None, dodger.Update(Middle, away, WorkArea, T0.AddMilliseconds(499)).Action);
        Assert.Equal(MiniDodgeAction.Return, dodger.Update(Middle, away, WorkArea, T0.AddMilliseconds(500)).Action);
        Assert.False(dodger.Dodged);
        Assert.Null(dodger.Aside);
    }

    [Fact]
    public void ComingBackNearRestartsTheReturnDelay()
    {
        var dodger = new MiniDodger();
        dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0);
        var away = new ScreenPoint(2400, 1300);

        dodger.Update(Middle, away, WorkArea, T0.AddMilliseconds(100));
        dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0.AddMilliseconds(300));
        Assert.Equal(MiniDodgeAction.None, dodger.Update(Middle, away, WorkArea, T0.AddMilliseconds(600)).Action);
        Assert.Equal(MiniDodgeAction.Return, dodger.Update(Middle, away, WorkArea, T0.AddMilliseconds(1000)).Action);
    }

    [Fact]
    public void AnUnknownCursorCountsAsAway()
    {
        var dodger = new MiniDodger();
        dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0);

        dodger.Update(Middle, null, WorkArea, T0.AddMilliseconds(100));

        Assert.Equal(MiniDodgeAction.Return, dodger.Update(Middle, null, WorkArea, T0.AddMilliseconds(500)).Action);
    }

    [Fact]
    public void FollowedToTheNewSpotItTakesAnotherSideChosenFromHome()
    {
        var dodger = new MiniDodger();
        dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0); // left, to (680, 500)

        // Into the left spot's zone from below: from home's centre the cursor is mostly left, so right.
        var step = dodger.Update(Middle, new ScreenPoint(800, 700), WorkArea, T0.AddMilliseconds(100));

        Assert.Equal(MiniDodgeAction.Dodge, step.Action);
        Assert.Equal(MiniDodgeDirection.Right, step.Choice!.Value.Direction);
        Assert.Equal(InteropRectangle.FromSize(1320, 500, 288, 288), dodger.Aside);
    }

    [Fact]
    public void FollowedWithNoOtherSideLeftItGoesHomeWhenHomeIsClear()
    {
        // A work area just wide enough for home and one spot to its left.
        var work = new InteropRectangle(680, 500, 1288, 788);
        var dodger = new MiniDodger();
        Assert.Equal(MiniDodgeAction.Dodge, dodger.Update(Middle, new ScreenPoint(1300, 650), work, T0).Action);

        var step = dodger.Update(Middle, new ScreenPoint(800, 650), work, T0.AddMilliseconds(100));

        Assert.Equal(MiniDodgeAction.Return, step.Action);
        Assert.False(dodger.Dodged);
    }

    [Fact]
    public void NoWorkAreaMeansNoDodge()
    {
        var dodger = new MiniDodger();

        Assert.Equal(MiniDodgeAction.None, dodger.Update(Middle, new ScreenPoint(1300, 650), null, T0).Action);
        Assert.False(dodger.Dodged);
    }

    [Fact]
    public void CancelForgetsTheDodgeSoAFarCursorNeverReturnsToTheOldHome()
    {
        var dodger = new MiniDodger();
        dodger.Update(Middle, new ScreenPoint(1300, 650), WorkArea, T0);

        dodger.Cancel();

        Assert.False(dodger.Dodged);
        Assert.Null(dodger.Aside);
        Assert.Equal(MiniDodgeAction.None, dodger.Update(TopRight, new ScreenPoint(10, 10), WorkArea, T0.AddSeconds(1)).Action);
    }

    [Theory]
    [InlineData(MiniDodgeDirection.Left, "left")]
    [InlineData(MiniDodgeDirection.Right, "right")]
    [InlineData(MiniDodgeDirection.Up, "up")]
    [InlineData(MiniDodgeDirection.Down, "down")]
    public void DirectionsHaveTraceNames(MiniDodgeDirection direction, string name) =>
        Assert.Equal(name, MiniDodge.Name(direction));

    [Fact]
    public void TimingsMatchCieLinux()
    {
        Assert.Equal(24, MiniDodge.Approach);
        Assert.Equal(8, MiniDodge.Gap);
        Assert.Equal(TimeSpan.FromMilliseconds(400), MiniDodge.ReturnDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(100), MiniDodge.PollInterval);
    }
}
