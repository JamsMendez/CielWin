using CielWin.App.Composition;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// <see cref="SingleInstanceGuard"/>: only the first holder of a name runs; a second start gets
/// nothing and must exit; the name frees up once the holder is disposed.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    private readonly string _name = $@"Local\CielWin.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void FirstAcquire_Succeeds()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_name);

        Assert.NotNull(guard);
    }

    [Fact]
    public void SecondAcquire_WhileHeld_ReturnsNull()
    {
        using var first = SingleInstanceGuard.TryAcquire(_name);

        using var second = SingleInstanceGuard.TryAcquire(_name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void SecondAcquire_FromAnotherThread_WhileHeld_ReturnsNull()
    {
        using var first = SingleInstanceGuard.TryAcquire(_name);
        SingleInstanceGuard? second = null;

        var thread = new Thread(() => second = SingleInstanceGuard.TryAcquire(_name));
        thread.Start();
        thread.Join();

        Assert.NotNull(first);
        Assert.Null(second);
        second?.Dispose();
    }

    [Fact]
    public void Acquire_AfterHolderDisposed_Succeeds()
    {
        SingleInstanceGuard.TryAcquire(_name)!.Dispose();

        using var again = SingleInstanceGuard.TryAcquire(_name);

        Assert.NotNull(again);
    }

    [Fact]
    public void Acquire_WhenTheNameBelongsToAnotherKernelObject_ReturnsNull()
    {
        // Same failure class as a copy holding the name with an ACL this process cannot open:
        // the name is taken by something this start cannot own, so it must exit, not crash.
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, _name);

        using var guard = SingleInstanceGuard.TryAcquire(_name);

        Assert.Null(guard);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var guard = SingleInstanceGuard.TryAcquire(_name)!;

        guard.Dispose();
        guard.Dispose();
    }

    [Fact]
    public void ProductionName_IsSessionLocal()
    {
        Assert.StartsWith(@"Local\", SingleInstanceGuard.ProductionName);
    }
}
