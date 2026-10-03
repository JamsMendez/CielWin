using CielWin.App.Wallpaper;
using InteropRectangle = CielWin.Interop.Rectangle;

namespace CielWin.App.Tests.Wallpaper;

public sealed class MiniGlideTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly InteropRectangle From = InteropRectangle.FromSize(0, 0, 288, 288);
    private static readonly InteropRectangle To = InteropRectangle.FromSize(1000, 500, 288, 288);

    private static MiniGlide Glide() => new(From, To, Start, TimeSpan.FromMilliseconds(200));

    [Fact]
    public void AtTheStartTheBoundsAreTheOrigin()
    {
        Assert.Equal(From, Glide().At(Start));
        Assert.False(Glide().IsComplete(Start));
    }

    [Fact]
    public void HalfwayTheBoundsFollowTheEaseOutCubicCurveRoundedToWholePixels()
    {
        // t = 0.5 -> 1 - 0.5^3 = 0.875
        var mid = Glide().At(Start + TimeSpan.FromMilliseconds(100));

        Assert.Equal(InteropRectangle.FromSize(875, 438, 288, 288), mid);
    }

    [Fact]
    public void TheEaseIsAheadOfLinearAndMonotonic()
    {
        Assert.Equal(0, MiniGlide.Ease(0));
        Assert.Equal(1, MiniGlide.Ease(1));
        Assert.True(MiniGlide.Ease(0.25) > 0.25);
        Assert.True(MiniGlide.Ease(0.5) < MiniGlide.Ease(0.75));
    }

    [Fact]
    public void AtAndPastTheEndTheBoundsAreExactlyTheTarget()
    {
        var glide = Glide();

        Assert.True(glide.IsComplete(Start + TimeSpan.FromMilliseconds(200)));
        Assert.Equal(To, glide.At(Start + TimeSpan.FromMilliseconds(200)));
        Assert.Equal(To, glide.At(Start + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ATimeBeforeTheStartClampsToTheOrigin()
    {
        Assert.Equal(From, Glide().At(Start - TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void ASizeChangeIsInterpolatedWithThePosition()
    {
        var glide = new MiniGlide(
            InteropRectangle.FromSize(0, 0, 200, 200), InteropRectangle.FromSize(0, 0, 360, 360), Start,
            TimeSpan.FromMilliseconds(200));

        // 200 + 160 * 0.875 = 340
        Assert.Equal(InteropRectangle.FromSize(0, 0, 340, 340), glide.At(Start + TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void ARetargetedGlideStartsExactlyAtTheCurrentAnimatedBounds()
    {
        var now = Start + TimeSpan.FromMilliseconds(100);
        var current = Glide().At(now);
        var target = InteropRectangle.FromSize(0, 900, 288, 288);

        var retarget = new MiniGlide(current, target, now, TimeSpan.FromMilliseconds(200));

        Assert.Equal(current, retarget.At(now));
        Assert.Equal(target, retarget.At(now + TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void AZeroDurationIsCompleteImmediatelyAtTheTarget()
    {
        var glide = new MiniGlide(From, To, Start, TimeSpan.Zero);

        Assert.True(glide.IsComplete(Start));
        Assert.Equal(To, glide.At(Start));
    }
}
