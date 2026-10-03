using CielWin.Interop.Win32;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// <see cref="Win32KeyboardHookRegistrar"/>: the hook callback's decision runs anywhere through
/// <see cref="Win32KeyboardHookRegistrar.HandleKey"/>; installing the real hook needs an interactive
/// session (no input is injected: the facts only prove the install, thread and dispose lifecycle).
/// </summary>
public sealed class Win32KeyboardHookRegistrarTests
{
    private const uint M = 0x4D;
    private const HotkeyModifiers Alt = HotkeyModifiers.Alt;

    private int _masks;

    private Win32KeyboardHookRegistrar Unhooked(Action<string>? onHandlerFailed = null)
    {
        var registrar = new Win32KeyboardHookRegistrar(onHandlerFailed, () => _masks++);
        Assert.True(registrar.Register(1, Alt | HotkeyModifiers.NoRepeat, M));
        return registrar;
    }

    [Fact]
    public void AFiredChord_RaisesPressedOnce_InjectsTheMaskOnce_AndSwallowsTheWholeStroke()
    {
        using var registrar = Unhooked();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        Assert.True(registrar.HandleKey(M, isDown: true, Alt, 0));
        Assert.True(registrar.HandleKey(M, isDown: true, Alt, 0));
        Assert.True(registrar.HandleKey(M, isDown: false, Alt, 0));

        Assert.Equal([1], pressed);
        Assert.Equal(1, _masks);
    }

    [Fact]
    public void AnythingElse_PassesWithoutRaisingOrInjecting()
    {
        using var registrar = Unhooked();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        Assert.False(registrar.HandleKey(M, isDown: true, HotkeyModifiers.None, 0));
        Assert.False(registrar.HandleKey(M, isDown: false, HotkeyModifiers.None, 0));
        Assert.False(registrar.HandleKey(M, isDown: true, Alt | HotkeyModifiers.Control, 0));

        Assert.Empty(pressed);
        Assert.Equal(0, _masks);
    }

    /// <summary>
    /// A throwing handler is reported by exception TYPE name only (a message can hold an absolute path),
    /// never unwinds through the hook, and the stroke is still swallowed and masked.
    /// </summary>
    [Fact]
    public void AHandlerThatThrows_IsReportedByTypeName_AndTheChordIsStillSwallowed()
    {
        var failures = new List<string>();
        using var registrar = Unhooked(failures.Add);
        registrar.Pressed += _ => throw new InvalidOperationException("C:\\secret\\path");

        var swallowed = registrar.HandleKey(M, isDown: true, Alt, 0);

        Assert.True(swallowed);
        Assert.Equal(["InvalidOperationException"], failures);
        Assert.Equal(1, _masks);
    }

    [Fact]
    public void AFailureCallbackThatThrows_DoesNotEscapeTheHook()
    {
        using var registrar = Unhooked(_ => throw new IOException("trace sink down"));
        registrar.Pressed += _ => throw new InvalidOperationException("boom");

        Assert.True(registrar.HandleKey(M, isDown: true, Alt, 0));
    }

    [Fact]
    public void AfterDispose_RegisterIsRefused_AndKeysPass()
    {
        var registrar = Unhooked();
        registrar.Dispose();
        registrar.Dispose();

        Assert.False(registrar.Register(2, Alt | HotkeyModifiers.Shift, M));
        Assert.False(registrar.HandleKey(M, isDown: true, Alt, 0));
        Assert.Equal(0, _masks);
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmChar = 0x0102;

    private static readonly ModifierSnapshot NoModifiers = default;
    private static readonly ModifierSnapshot AltHeld = new(Alt: true, Control: false, Shift: false, LeftWin: false, RightWin: false);

    /// <summary>With Alt held Windows reports the chord's key as WM_SYSKEYDOWN / WM_SYSKEYUP.</summary>
    [Fact]
    public void HookEvent_SysKeyDownWithAlt_FiresAndMasks_AndSysKeyUpIsSwallowed()
    {
        using var registrar = Unhooked();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        Assert.True(registrar.HandleHookEvent(WmSysKeyDown, M, 100, AltHeld));
        Assert.Equal([1], pressed);
        Assert.Equal(1, _masks);

        Assert.True(registrar.HandleHookEvent(WmSysKeyUp, M, 150, AltHeld));
        Assert.Equal([1], pressed);
        Assert.Equal(1, _masks);
    }

    [Fact]
    public void HookEvent_KeyDownAndKeyUp_AreADownAndAnUp()
    {
        using var registrar = Unhooked();

        Assert.True(registrar.HandleHookEvent(WmKeyDown, M, 100, AltHeld));
        Assert.True(registrar.HandleHookEvent(WmKeyUp, M, 150, AltHeld));
        // The up ended the stroke: the next down is fresh and fires again.
        Assert.True(registrar.HandleHookEvent(WmKeyDown, M, 200, AltHeld));
        Assert.Equal(2, _masks);
    }

    /// <summary>Any other message passes and touches no state: it is neither a down nor an up.</summary>
    [Fact]
    public void HookEvent_AnUnknownMessage_PassesAndChangesNothing()
    {
        using var registrar = Unhooked();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        Assert.False(registrar.HandleHookEvent(WmChar, M, 100, AltHeld));
        Assert.Empty(pressed);
        Assert.Equal(0, _masks);

        Assert.True(registrar.HandleHookEvent(WmSysKeyDown, M, 110, AltHeld));
        Assert.False(registrar.HandleHookEvent(WmChar, M, 120, AltHeld));
        // The unknown message was not taken for the up: the stroke is still swallowed.
        Assert.True(registrar.HandleHookEvent(WmSysKeyUp, M, 130, AltHeld));
    }

    [Fact]
    public void HookEvent_PlainTyping_PassesWithoutMasking()
    {
        using var registrar = Unhooked();

        Assert.False(registrar.HandleHookEvent(WmKeyDown, M, 100, NoModifiers));
        Assert.False(registrar.HandleHookEvent(WmKeyUp, M, 150, NoModifiers));
        Assert.Equal(0, _masks);
    }

    /// <summary>Each snapshot maps to exactly one chord's modifiers; either Win key is Win.</summary>
    [Theory]
    [InlineData(false, false, false, false, false, HotkeyModifiers.None)]
    [InlineData(true, false, false, false, false, HotkeyModifiers.Alt)]
    [InlineData(false, true, false, false, false, HotkeyModifiers.Control)]
    [InlineData(false, false, true, false, false, HotkeyModifiers.Shift)]
    [InlineData(false, false, false, true, false, HotkeyModifiers.Win)]
    [InlineData(false, false, false, false, true, HotkeyModifiers.Win)]
    [InlineData(false, false, false, true, true, HotkeyModifiers.Win)]
    [InlineData(true, true, true, true, false,
        HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)]
    public void ModifierSnapshot_MapsEachKeyToItsModifier(
        bool alt, bool control, bool shift, bool leftWin, bool rightWin, HotkeyModifiers expected)
    {
        var snapshot = new ModifierSnapshot(alt, control, shift, leftWin, rightWin);

        Assert.Equal(expected, snapshot.ToHotkeyModifiers());
    }

    /// <summary>The snapshot decides the chord exactly: Alt+Shift fires the Alt+Shift chord, an extra key passes.</summary>
    [Theory]
    [InlineData(true, false, false, false, false, 1)]
    [InlineData(true, false, true, false, false, 2)]
    [InlineData(true, true, false, false, false, 0)]
    [InlineData(true, false, false, true, false, 0)]
    [InlineData(true, false, false, false, true, 0)]
    [InlineData(false, false, true, false, false, 0)]
    public void HookEvent_TheSnapshotSelectsTheExactChord(
        bool alt, bool control, bool shift, bool leftWin, bool rightWin, int expectedId)
    {
        using var registrar = Unhooked();
        Assert.True(registrar.Register(2, Alt | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat, M));
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        var swallowed = registrar.HandleHookEvent(WmSysKeyDown, M, 100, new ModifierSnapshot(alt, control, shift, leftWin, rightWin));

        Assert.Equal(expectedId != 0, swallowed);
        Assert.Equal(expectedId != 0 ? [expectedId] : [], pressed);
        Assert.Equal(expectedId != 0 ? 1 : 0, _masks);
    }

    /// <summary>
    /// The real hook installs on its own background thread (never the caller's), and dispose unhooks,
    /// ends that thread and joins it.
    /// </summary>
    [RequiresDesktopOptInFact]
    public void TheRealHook_RunsOnItsOwnThread_AndDisposeEndsIt()
    {
        var registrar = Win32KeyboardHookRegistrar.TryStart(null, out var error);

        Assert.NotNull(registrar);
        Assert.Equal(0, error);
        var thread = registrar.HookThread;
        Assert.NotNull(thread);
        Assert.True(thread.IsAlive);
        Assert.True(thread.IsBackground);
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread.ManagedThreadId);
        Assert.True(registrar.Register(1, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x87));

        registrar.Dispose();

        Assert.False(thread.IsAlive);
        Assert.False(registrar.Register(2, HotkeyModifiers.Alt, 0x87));
    }

    [RequiresDesktopOptInFact]
    public void TheRealHook_CanBeStartedAgainAfterDispose()
    {
        Win32KeyboardHookRegistrar.TryStart(null, out _)!.Dispose();

        using var again = Win32KeyboardHookRegistrar.TryStart(null, out var error);

        Assert.NotNull(again);
        Assert.Equal(0, error);
    }
}
