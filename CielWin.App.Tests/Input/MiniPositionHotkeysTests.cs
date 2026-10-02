using CielWin.App.Input;
using CielWin.Interop;

namespace CielWin.App.Tests.Input;

/// <summary>The two mini-position chords: Alt+M clockwise, Alt+Shift+M counter-clockwise.</summary>
public sealed class MiniPositionHotkeysTests
{
    [Fact]
    public void TheKeyIsM()
    {
        Assert.Equal(0x4Du, MiniPositionHotkeys.VirtualKeyM);
    }

    [Fact]
    public void Clockwise_IsAltM_WithoutAutoRepeat()
    {
        Assert.Equal(HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, MiniPositionHotkeys.ClockwiseModifiers);
    }

    [Fact]
    public void CounterClockwise_IsAltShiftM_WithoutAutoRepeat()
    {
        Assert.Equal(
            HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat,
            MiniPositionHotkeys.CounterClockwiseModifiers);
    }

    [Fact]
    public void TheIdsAreDistinct()
    {
        Assert.NotEqual(MiniPositionHotkeys.ClockwiseId, MiniPositionHotkeys.CounterClockwiseId);
    }

    [Theory]
    [InlineData(MiniPositionHotkeys.ClockwiseId, MiniPosition.TopLeft, MiniPosition.TopCenter)]
    [InlineData(MiniPositionHotkeys.CounterClockwiseId, MiniPosition.TopLeft, MiniPosition.LeftCenter)]
    public void TryStep_MapsEachIdToItsDirection(int id, MiniPosition from, MiniPosition expected)
    {
        Assert.True(MiniPositionHotkeys.TryStep(id, from, out var next));
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryStep_AnUnknownId_IsRefused()
    {
        Assert.False(MiniPositionHotkeys.TryStep(99, MiniPosition.TopLeft, out _));
    }
}
