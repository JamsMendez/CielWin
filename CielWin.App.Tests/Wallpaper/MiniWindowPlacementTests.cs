using CielWin.App.Wallpaper;
using CielWin.Interop;

namespace CielWin.App.Tests.Wallpaper;

public sealed class MiniWindowPlacementTests
{
    // A work area with a non-zero origin (second-monitor style) and a 48px taskbar taken off the bottom.
    private static readonly Rectangle WorkArea = Rectangle.FromSize(100, 50, 2560, 1392);

    [Theory]
    [InlineData(1440, 288)]
    [InlineData(1080, 216)]
    [InlineData(1083, 216)]
    [InlineData(1079, 215)]
    public void TheSideIsAFifthOfTheMonitorHeight_RoundedDown(int monitorHeight, int side)
    {
        var rect = MiniWindowPlacement.Compute(WorkArea, monitorHeight, MiniPosition.BottomRight);

        Assert.Equal(side, rect.Width);
        Assert.Equal(side, rect.Height);
    }

    [Theory]
    [InlineData(MiniPosition.TopLeft, 100, 50)]
    [InlineData(MiniPosition.TopRight, 100 + 2560 - 288, 50)]
    [InlineData(MiniPosition.BottomLeft, 100, 50 + 1392 - 288)]
    [InlineData(MiniPosition.BottomRight, 100 + 2560 - 288, 50 + 1392 - 288)]
    public void EachCornerIsFlushWithTheWorkAreaEdges(MiniPosition corner, int x, int y)
    {
        var rect = MiniWindowPlacement.Compute(WorkArea, 1440, corner);

        Assert.Equal(Rectangle.FromSize(x, y, 288, 288), rect);
    }

    [Fact]
    public void ABottomCornerStopsAtTheTaskbar_NotAtTheMonitorEdge()
    {
        var rect = MiniWindowPlacement.Compute(WorkArea, 1440, MiniPosition.BottomLeft);

        Assert.Equal(WorkArea.Bottom, rect.Bottom);
        Assert.True(rect.Bottom < 1440 + WorkArea.Top);
    }

    [Fact]
    public void ATopCornerStaysBelowATaskbarDockedAtTheTop()
    {
        // 2560x1440 monitor with a 48px taskbar docked at the top edge.
        var topTaskbarWorkArea = Rectangle.FromSize(0, 48, 2560, 1392);

        var rect = MiniWindowPlacement.Compute(topTaskbarWorkArea, 1440, MiniPosition.TopRight);

        Assert.Equal(Rectangle.FromSize(2560 - 288, 48, 288, 288), rect);
    }

    [Fact]
    public void ARightCornerStaysLeftOfATaskbarDockedAtTheRight()
    {
        // 2560x1440 monitor with a 64px taskbar docked at the right edge.
        var rightTaskbarWorkArea = Rectangle.FromSize(0, 0, 2560 - 64, 1440);

        var rect = MiniWindowPlacement.Compute(rightTaskbarWorkArea, 1440, MiniPosition.TopRight);

        Assert.Equal(Rectangle.FromSize(2560 - 64 - 288, 0, 288, 288), rect);
    }

    [Theory]
    [InlineData(MiniPosition.TopLeft, MiniPosition.TopCenter)]
    [InlineData(MiniPosition.TopCenter, MiniPosition.TopRight)]
    [InlineData(MiniPosition.TopRight, MiniPosition.RightCenter)]
    [InlineData(MiniPosition.RightCenter, MiniPosition.BottomRight)]
    [InlineData(MiniPosition.BottomRight, MiniPosition.BottomCenter)]
    [InlineData(MiniPosition.BottomCenter, MiniPosition.BottomLeft)]
    [InlineData(MiniPosition.BottomLeft, MiniPosition.LeftCenter)]
    [InlineData(MiniPosition.LeftCenter, MiniPosition.TopLeft)]
    public void NextCyclesClockwise(MiniPosition from, MiniPosition expected)
    {
        Assert.Equal(expected, MiniWindowPlacement.Next(from));
    }

    [Theory]
    [InlineData(MiniPosition.TopLeft, MiniPosition.LeftCenter)]
    [InlineData(MiniPosition.LeftCenter, MiniPosition.BottomLeft)]
    [InlineData(MiniPosition.BottomLeft, MiniPosition.BottomCenter)]
    [InlineData(MiniPosition.BottomCenter, MiniPosition.BottomRight)]
    [InlineData(MiniPosition.BottomRight, MiniPosition.RightCenter)]
    [InlineData(MiniPosition.RightCenter, MiniPosition.TopRight)]
    [InlineData(MiniPosition.TopRight, MiniPosition.TopCenter)]
    [InlineData(MiniPosition.TopCenter, MiniPosition.TopLeft)]
    public void PreviousCyclesCounterClockwise(MiniPosition from, MiniPosition expected)
    {
        Assert.Equal(expected, MiniWindowPlacement.Previous(from));
    }

    [Fact]
    public void PreviousUndoesNextAndNextUndoesPreviousForEveryPosition()
    {
        foreach (var position in Enum.GetValues<MiniPosition>())
        {
            Assert.Equal(position, MiniWindowPlacement.Previous(MiniWindowPlacement.Next(position)));
            Assert.Equal(position, MiniWindowPlacement.Next(MiniWindowPlacement.Previous(position)));
        }
    }

    [Fact]
    public void TheFullCycleVisitsAllEightPositionsOnceAndReturnsToTheStart()
    {
        var seen = new List<MiniPosition>();
        var current = MiniPosition.TopLeft;
        for (var i = 0; i < 8; i++)
        {
            seen.Add(current);
            current = MiniWindowPlacement.Next(current);
        }

        Assert.Equal(MiniPosition.TopLeft, current);
        Assert.Equal(8, seen.Distinct().Count());
    }

    [Fact]
    public void TheFullCounterClockwiseCycleVisitsAllEightPositionsOnceAndReturnsToTheStart()
    {
        var seen = new List<MiniPosition>();
        var current = MiniPosition.TopLeft;
        for (var i = 0; i < 8; i++)
        {
            seen.Add(current);
            current = MiniWindowPlacement.Previous(current);
        }

        Assert.Equal(MiniPosition.TopLeft, current);
        Assert.Equal(8, seen.Distinct().Count());
    }

    [Fact]
    public void AnUndefinedPositionRestartsEitherCycleAtTopLeft()
    {
        var undefined = (MiniPosition)99;

        Assert.Equal(MiniPosition.TopLeft, MiniWindowPlacement.Next(undefined));
        Assert.Equal(MiniPosition.TopLeft, MiniWindowPlacement.Previous(undefined));
    }

    [Theory]
    [InlineData(MiniPosition.TopCenter, 100 + (2560 - 288) / 2, 50)]
    [InlineData(MiniPosition.RightCenter, 100 + 2560 - 288, 50 + (1392 - 288) / 2)]
    [InlineData(MiniPosition.BottomCenter, 100 + (2560 - 288) / 2, 50 + 1392 - 288)]
    [InlineData(MiniPosition.LeftCenter, 100, 50 + (1392 - 288) / 2)]
    public void EachMidpointIsCenteredOnItsSideAndFlushWithThatEdge(MiniPosition corner, int x, int y)
    {
        var rect = MiniWindowPlacement.Compute(WorkArea, 1440, corner);

        Assert.Equal(Rectangle.FromSize(x, y, 288, 288), rect);
    }

    [Fact]
    public void ARightCenterStaysLeftOfATaskbarDockedAtTheRight()
    {
        var rightTaskbarWorkArea = Rectangle.FromSize(0, 0, 2560 - 64, 1440);

        var rect = MiniWindowPlacement.Compute(rightTaskbarWorkArea, 1440, MiniPosition.RightCenter);

        Assert.Equal(Rectangle.FromSize(2560 - 64 - 288, (1440 - 288) / 2, 288, 288), rect);
    }

    [Fact]
    public void ABottomCenterStopsAtABottomTaskbar()
    {
        var bottomTaskbarWorkArea = Rectangle.FromSize(0, 0, 2560, 1440 - 48);

        var rect = MiniWindowPlacement.Compute(bottomTaskbarWorkArea, 1440, MiniPosition.BottomCenter);

        Assert.Equal(Rectangle.FromSize((2560 - 288) / 2, 1440 - 48 - 288, 288, 288), rect);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ANonPositiveMonitorHeightYieldsAnEmptyWindowAtTheCorner_NeverANegativeSide(int monitorHeight)
    {
        var rect = MiniWindowPlacement.Compute(WorkArea, monitorHeight, MiniPosition.BottomRight);

        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
        Assert.Equal(WorkArea.Right, rect.Left);
        Assert.Equal(WorkArea.Bottom, rect.Top);
    }

    [Fact]
    public void AWorkAreaSmallerThanTheSideClampsTheSideToItsShorterDimension_StayingInside()
    {
        var tiny = Rectangle.FromSize(500, 300, 200, 150);

        var rect = MiniWindowPlacement.Compute(tiny, 1440, MiniPosition.BottomRight);

        Assert.Equal(Rectangle.FromSize(500 + 200 - 150, 300, 150, 150), rect);
    }

    [Fact]
    public void AnEmptyWorkAreaYieldsAnEmptyWindowAtItsOrigin()
    {
        var rect = MiniWindowPlacement.Compute(Rectangle.FromSize(10, 20, 0, 0), 1440, MiniPosition.TopRight);

        Assert.Equal(Rectangle.FromSize(10, 20, 0, 0), rect);
    }
}
