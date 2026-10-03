using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// <see cref="KeyboardChordMatcher"/>: the low-level keyboard hook's decision, pure. A registered
/// chord fires once on a FRESH key down whose held modifiers match exactly, and from then on every
/// repeat and the matching key up are swallowed, so nothing of the chord reaches the focused app.
/// Everything else passes through untouched.
/// </summary>
public sealed class KeyboardChordMatcherTests
{
    private const uint M = 0x4D;
    private const uint N = 0x4E;
    private const int Cw = 1;
    private const int Ccw = 2;
    private const HotkeyModifiers Alt = HotkeyModifiers.Alt;
    private const HotkeyModifiers AltShift = HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    private static readonly KeyDecision Pass = KeyDecision.Pass;
    private static readonly KeyDecision Swallow = KeyDecision.Swallow;

    private static KeyboardChordMatcher AltMChords()
    {
        var matcher = new KeyboardChordMatcher();
        Assert.True(matcher.Register(Cw, Alt | HotkeyModifiers.NoRepeat, M));
        Assert.True(matcher.Register(Ccw, AltShift | HotkeyModifiers.NoRepeat, M));
        return matcher;
    }

    [Fact]
    public void ATap_FiresOnTheDown_AndSwallowsTheUp()
    {
        var matcher = AltMChords();

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, Alt));
    }

    [Fact]
    public void AHeldChord_FiresOnce_AndSwallowsEveryRepeatAndTheUp()
    {
        var matcher = AltMChords();

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(Swallow, matcher.OnKey(M, isDown: true, Alt));
        }

        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, Alt));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
    }

    [Fact]
    public void ReleasingAltMidHold_KeepsSwallowingUntilTheKeyComesUp()
    {
        var matcher = AltMChords();

        matcher.OnKey(M, isDown: true, Alt);

        Assert.Equal(Swallow, matcher.OnKey(M, isDown: true, HotkeyModifiers.None));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, HotkeyModifiers.None));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None));
    }

    [Fact]
    public void ModifiersMatchExactly_AltShiftFiresOnlyTheCounterClockwiseChord()
    {
        var matcher = AltMChords();

        Assert.Equal(KeyDecision.Fire(Ccw), matcher.OnKey(M, isDown: true, AltShift));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, AltShift));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
    }

    [Fact]
    public void AltShiftWithOnlyTheAltChordRegistered_Passes()
    {
        var matcher = new KeyboardChordMatcher();
        matcher.Register(Cw, Alt, M);

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, AltShift));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, AltShift));
    }

    /// <summary>Ctrl+Alt is AltGr on many layouts: it must reach the app.</summary>
    [Theory]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Control)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Win)]
    public void ExtraModifiers_Pass(HotkeyModifiers held)
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, held));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, held));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, held));
    }

    [Fact]
    public void APlainKey_Passes()
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, HotkeyModifiers.None));
    }

    /// <summary>Only a FRESH down starts a chord: M already held when Alt goes down is typing, not a chord.</summary>
    [Fact]
    public void KeyHeldBeforeAlt_PassesIncludingItsRepeatsAndItsUp()
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, Alt));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, Alt));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, Alt));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
    }

    [Fact]
    public void AnUnregisteredKey_Passes()
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(N, isDown: true, Alt));
        Assert.Equal(Pass, matcher.OnKey(N, isDown: false, Alt));
        Assert.Equal(Pass, matcher.OnKey(KeyboardChordMatcher.MaskKey, isDown: true, Alt));
    }

    /// <summary>Without <see cref="HotkeyModifiers.NoRepeat"/> each auto-repeat fires again (still swallowed).</summary>
    [Fact]
    public void AChordWithoutNoRepeat_FiresOnEveryRepeat()
    {
        var matcher = new KeyboardChordMatcher();
        matcher.Register(Cw, Alt, M);

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, Alt));
    }

    [Fact]
    public void Register_RefusesADuplicateIdOrChord_AsRegisterHotKeyWould()
    {
        var matcher = new KeyboardChordMatcher();
        Assert.True(matcher.Register(Cw, Alt | HotkeyModifiers.NoRepeat, M));

        Assert.False(matcher.Register(Cw, AltShift, M));
        Assert.False(matcher.Register(Ccw, Alt, M));
        Assert.True(matcher.Register(Ccw, Alt, N));
    }

    /// <summary>A bare key would swallow ordinary typing; unknown bits are not a chord this hook can match.</summary>
    [Theory]
    [InlineData(HotkeyModifiers.None)]
    [InlineData(HotkeyModifiers.NoRepeat)]
    [InlineData((HotkeyModifiers)0x0100 | HotkeyModifiers.Alt)]
    public void Register_RefusesUnsupportedModifiers(HotkeyModifiers modifiers)
    {
        var matcher = new KeyboardChordMatcher();

        Assert.False(matcher.Register(Cw, modifiers, M));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, modifiers & ~HotkeyModifiers.NoRepeat));
    }
}
