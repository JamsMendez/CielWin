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

        Assert.True(registrar.HandleKey(M, isDown: true, Alt));
        Assert.True(registrar.HandleKey(M, isDown: true, Alt));
        Assert.True(registrar.HandleKey(M, isDown: false, Alt));

        Assert.Equal([1], pressed);
        Assert.Equal(1, _masks);
    }

    [Fact]
    public void AnythingElse_PassesWithoutRaisingOrInjecting()
    {
        using var registrar = Unhooked();
        var pressed = new List<int>();
        registrar.Pressed += pressed.Add;

        Assert.False(registrar.HandleKey(M, isDown: true, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(M, isDown: false, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(M, isDown: true, Alt | HotkeyModifiers.Control));

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

        var swallowed = registrar.HandleKey(M, isDown: true, Alt);

        Assert.True(swallowed);
        Assert.Equal(["InvalidOperationException"], failures);
        Assert.Equal(1, _masks);
    }

    [Fact]
    public void AFailureCallbackThatThrows_DoesNotEscapeTheHook()
    {
        using var registrar = Unhooked(_ => throw new IOException("trace sink down"));
        registrar.Pressed += _ => throw new InvalidOperationException("boom");

        Assert.True(registrar.HandleKey(M, isDown: true, Alt));
    }

    [Fact]
    public void AfterDispose_RegisterIsRefused_AndKeysPass()
    {
        var registrar = Unhooked();
        registrar.Dispose();
        registrar.Dispose();

        Assert.False(registrar.Register(2, Alt | HotkeyModifiers.Shift, M));
        Assert.False(registrar.HandleKey(M, isDown: true, Alt));
        Assert.Equal(0, _masks);
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
