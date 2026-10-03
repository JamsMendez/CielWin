using CielWin.App.Composition;
using CielWin.Interop;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// The production hotkey registrar: the low-level keyboard hook (it swallows the whole chord), falling
/// back to <c>RegisterHotKey</c> (traced) when the hook cannot be installed.
/// </summary>
public sealed class HotkeyRegistrarChoiceTests
{
    private readonly List<string> _trace = [];

    [Fact]
    public void TheHook_IsUsedWhenItInstalls_AndTheFallbackIsNeverBuilt()
    {
        var hook = new FakeHotkeys();
        var fallbacks = 0;

        var chosen = ProductionComposition.CreateHotkeys(
            _trace.Add, () => (hook, 0), () => { fallbacks++; return new FakeHotkeys(); });

        Assert.Same(hook, chosen);
        Assert.Equal(0, fallbacks);
        Assert.Empty(_trace);
    }

    [Fact]
    public void AHookThatCannotInstall_FallsBackToRegisterHotKey_AndIsTracedWithItsError()
    {
        var fallback = new FakeHotkeys();

        var chosen = ProductionComposition.CreateHotkeys(_trace.Add, () => (null, 5), () => fallback);

        Assert.Same(fallback, chosen);
        Assert.Equal(["hotkey hook-unavailable error=5 fallback=register-hotkey"], _trace);
    }

    [Fact]
    public void AHookStartThatThrows_FallsBackAndTracesTheTypeOnly()
    {
        var fallback = new FakeHotkeys();

        var chosen = ProductionComposition.CreateHotkeys(
            _trace.Add, () => throw new InvalidOperationException(@"C:\secret"), () => fallback);

        Assert.Same(fallback, chosen);
        Assert.Equal(["hotkey hook-unavailable error=InvalidOperationException fallback=register-hotkey"], _trace);
    }
}
