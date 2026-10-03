namespace CielWin.Interop.Win32;

/// <summary>What the low-level keyboard hook does with one key event.</summary>
/// <param name="Swallowed">True: the event never reaches the focused app.</param>
/// <param name="FiredId">The chord id to raise as pressed, if this event fires one.</param>
internal readonly record struct KeyDecision(bool Swallowed, int? FiredId)
{
    /// <summary>Hand the event on (<c>CallNextHookEx</c>).</summary>
    public static KeyDecision Pass => default;

    /// <summary>Eat the event: part of a chord that already fired.</summary>
    public static KeyDecision Swallow => new(true, null);

    /// <summary>Raise <paramref name="id"/> and eat the event.</summary>
    public static KeyDecision Fire(int id) => new(true, id);
}

/// <summary>
/// The decision behind <see cref="Win32KeyboardHookRegistrar"/>, pure so it can be proven without a hook.
/// </summary>
/// <remarks>
/// <para>
/// <c>RegisterHotKey</c> only consumes the chord's key DOWN: the key up (and, in some terminals, the
/// auto-repeat traffic) still reaches the focused app, which then types the letter. This matcher owns
/// the whole key stroke instead: a FRESH down of a registered key whose held modifiers match a chord
/// exactly fires it, and from then on every repeat and the matching up are swallowed too.
/// </para>
/// <para>
/// Exact means exact: Ctrl+Alt (AltGr on many layouts) or a held Win key never matches an Alt chord,
/// and a key already held before the modifiers went down is ordinary typing and passes, up included.
/// </para>
/// <para>
/// The matcher learns a key went up only from the up event; one the hook never saw would leave the key
/// "held" forever. Each event therefore carries its time (<c>KBDLLHOOKSTRUCT.time</c>), and a down more
/// than <see cref="StaleThresholdMs"/> after that key's last event starts afresh.
/// Not thread-safe: the owner serializes <see cref="Register"/> and <see cref="OnKey"/>.
/// </para>
/// </remarks>
internal sealed class KeyboardChordMatcher
{
    /// <summary>
    /// The unassigned virtual key (<c>0xE8</c>) the hook injects after firing, so the Alt release that
    /// follows is not a bare Alt tap that would open the focused app's menu.
    /// </summary>
    public const uint MaskKey = 0xE8;

    /// <summary>
    /// How long after the last event of a key believed down another down of it still counts as that
    /// key's auto-repeat, in milliseconds. Windows waits at most 1000 ms (keyboard delay 3) before the
    /// first repeat and repeats at least ~2.5 times a second after that, so a held key never goes this
    /// long without an event. A later down means the key up was missed (secure desktop, lock screen,
    /// Windows dropping the hook): the stale state is forgotten and the down is a fresh one.
    /// </summary>
    public const uint StaleThresholdMs = 1500;

    private const HotkeyModifiers ChordModifiers =
        HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    private readonly List<Chord> _chords = [];
    /// <summary>The registered keys believed down, with the time of each one's last event.</summary>
    private readonly Dictionary<uint, uint> _down = [];
    private readonly Dictionary<uint, Chord> _swallowing = [];

    /// <summary>
    /// Registers <paramref name="modifiers"/> + <paramref name="virtualKey"/> under <paramref name="id"/>.
    /// False, like <c>RegisterHotKey</c>, for an id or a chord already registered; false too for no
    /// modifier at all (that would swallow ordinary typing) or a bit this matcher does not know.
    /// </summary>
    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        var held = modifiers & ChordModifiers;
        if (held == HotkeyModifiers.None || (modifiers & ~(ChordModifiers | HotkeyModifiers.NoRepeat)) != 0)
        {
            return false;
        }

        if (_chords.Exists(chord => chord.Id == id || (chord.Modifiers == held && chord.VirtualKey == virtualKey)))
        {
            return false;
        }

        _chords.Add(new Chord(id, held, virtualKey, (modifiers & HotkeyModifiers.NoRepeat) != 0));
        return true;
    }

    /// <summary>
    /// Decides one key event. <paramref name="held"/> is the modifier state at the event (only the Alt,
    /// Control, Shift and Win bits are read); <paramref name="time"/> is the event's millisecond tick
    /// count, which wraps, so intervals are measured with unsigned subtraction.
    /// </summary>
    public KeyDecision OnKey(uint virtualKey, bool isDown, HotkeyModifiers held, uint time)
    {
        if (!_chords.Exists(chord => chord.VirtualKey == virtualKey))
        {
            return KeyDecision.Pass; // never tracked: the down set only ever holds registered keys
        }

        if (!isDown)
        {
            _down.Remove(virtualKey);
            return _swallowing.Remove(virtualKey) ? KeyDecision.Swallow : KeyDecision.Pass;
        }

        if (_down.TryGetValue(virtualKey, out var last))
        {
            if (unchecked(time - last) <= StaleThresholdMs)
            {
                _down[virtualKey] = time;
                if (_swallowing.TryGetValue(virtualKey, out var active))
                {
                    // An auto-repeat of a chord that already fired.
                    return active.NoRepeat ? KeyDecision.Swallow : KeyDecision.Fire(active.Id);
                }

                return KeyDecision.Pass; // a repeat of a key that was not a chord when it went down
            }

            // Its key up was missed: forget the stale stroke and take this down as a fresh one.
            _swallowing.Remove(virtualKey);
        }

        _down[virtualKey] = time;

        held &= ChordModifiers;
        var match = _chords.Find(chord => chord.VirtualKey == virtualKey && chord.Modifiers == held);
        if (match is null)
        {
            return KeyDecision.Pass;
        }

        _swallowing[virtualKey] = match;
        return KeyDecision.Fire(match.Id);
    }

    private sealed record Chord(int Id, HotkeyModifiers Modifiers, uint VirtualKey, bool NoRepeat);
}
