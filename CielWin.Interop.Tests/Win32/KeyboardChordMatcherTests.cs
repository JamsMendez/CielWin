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

    /// <summary>A fixed event time: every event of a test sequence lands within the stale threshold.</summary>
    private const uint T0 = 0;

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

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, Alt, T0));
    }

    [Fact]
    public void AHeldChord_FiresOnce_AndSwallowsEveryRepeatAndTheUp()
    {
        var matcher = AltMChords();

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(Swallow, matcher.OnKey(M, isDown: true, Alt, T0));
        }

        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, Alt, T0));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
    }

    [Fact]
    public void ReleasingAltMidHold_KeepsSwallowingUntilTheKeyComesUp()
    {
        var matcher = AltMChords();

        matcher.OnKey(M, isDown: true, Alt, T0);

        Assert.Equal(Swallow, matcher.OnKey(M, isDown: true, HotkeyModifiers.None, T0));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, HotkeyModifiers.None, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None, T0));
    }

    [Fact]
    public void ModifiersMatchExactly_AltShiftFiresOnlyTheCounterClockwiseChord()
    {
        var matcher = AltMChords();

        Assert.Equal(KeyDecision.Fire(Ccw), matcher.OnKey(M, isDown: true, AltShift, T0));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, AltShift, T0));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
    }

    [Fact]
    public void AltShiftWithOnlyTheAltChordRegistered_Passes()
    {
        var matcher = new KeyboardChordMatcher();
        matcher.Register(Cw, Alt, M);

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, AltShift, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, AltShift, T0));
    }

    /// <summary>Ctrl+Alt is AltGr on many layouts: it must reach the app.</summary>
    [Theory]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Control)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Win)]
    public void ExtraModifiers_Pass(HotkeyModifiers held)
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, held, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, held, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, held, T0));
    }

    [Fact]
    public void APlainKey_Passes()
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, HotkeyModifiers.None, T0));
    }

    /// <summary>Only a FRESH down starts a chord: M already held when Alt goes down is typing, not a chord.</summary>
    [Fact]
    public void KeyHeldBeforeAlt_PassesIncludingItsRepeatsAndItsUp()
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, Alt, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, Alt, T0));
        Assert.Equal(Pass, matcher.OnKey(M, isDown: false, Alt, T0));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
    }

    [Fact]
    public void AnUnregisteredKey_Passes()
    {
        var matcher = AltMChords();

        Assert.Equal(Pass, matcher.OnKey(N, isDown: true, Alt, T0));
        Assert.Equal(Pass, matcher.OnKey(N, isDown: false, Alt, T0));
        Assert.Equal(Pass, matcher.OnKey(KeyboardChordMatcher.MaskKey, isDown: true, Alt, T0));
    }

    /// <summary>Without <see cref="HotkeyModifiers.NoRepeat"/> each auto-repeat fires again (still swallowed).</summary>
    [Fact]
    public void AChordWithoutNoRepeat_FiresOnEveryRepeat()
    {
        var matcher = new KeyboardChordMatcher();
        matcher.Register(Cw, Alt, M);

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, T0));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, Alt, T0));
    }

    /// <summary>
    /// A missed key up (secure desktop, lock screen, Windows dropping the hook) must not cost the next
    /// chord: a down long after the last event of a key believed down is a fresh down, not a repeat.
    /// </summary>
    [Fact]
    public void AMissedUpOfAFiredChord_IsForgottenAfterTheStaleThreshold()
    {
        var matcher = AltMChords();
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, 1_000));

        var later = 1_000 + KeyboardChordMatcher.StaleThresholdMs + 1;

        Assert.Equal(KeyDecision.Fire(Ccw), matcher.OnKey(M, isDown: true, AltShift, later));
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: false, AltShift, later + 50));
    }

    [Fact]
    public void AMissedUpOfPlainTyping_DoesNotEatTheNextChord()
    {
        var matcher = AltMChords();
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None, 1_000));

        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, 1_000 + KeyboardChordMatcher.StaleThresholdMs + 1));
    }

    /// <summary>Within the threshold of the key's LAST event it is still a held key's auto-repeat.</summary>
    [Fact]
    public void RepeatsWithinTheThresholdOfTheLastEvent_StayRepeats()
    {
        var matcher = AltMChords();
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, HotkeyModifiers.None, 1_000));

        // Each repeat is within the threshold of the previous one, though far past the first down.
        var time = 1_000u;
        for (var i = 0; i < 5; i++)
        {
            time += KeyboardChordMatcher.StaleThresholdMs;
            Assert.Equal(Pass, matcher.OnKey(M, isDown: true, Alt, time));
        }
    }

    [Fact]
    public void AFiredChordRepeatWithinTheThreshold_IsStillSwallowed()
    {
        var matcher = AltMChords();
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, 1_000));

        Assert.Equal(Swallow, matcher.OnKey(M, isDown: true, Alt, 1_000 + KeyboardChordMatcher.StaleThresholdMs));
    }

    /// <summary>The event time is a 32-bit millisecond tick count that wraps after ~49.7 days.</summary>
    [Fact]
    public void TheTickCountWrapping_IsMeasuredWithUnsignedSubtraction()
    {
        var matcher = AltMChords();
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, uint.MaxValue - 10));

        // 31 ms later, across the wrap: a repeat.
        Assert.Equal(Swallow, matcher.OnKey(M, isDown: true, Alt, 20));
        // Far past the last event, across no wrap: stale, so a fresh chord.
        Assert.Equal(KeyDecision.Fire(Cw), matcher.OnKey(M, isDown: true, Alt, 20 + KeyboardChordMatcher.StaleThresholdMs + 1));
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
        Assert.Equal(Pass, matcher.OnKey(M, isDown: true, modifiers & ~HotkeyModifiers.NoRepeat, T0));
    }
}
