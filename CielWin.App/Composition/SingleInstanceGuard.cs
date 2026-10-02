namespace CielWin.App.Composition;

/// <summary>
/// Keeps CielWin to one running copy per user session. The first start creates a named mutex and
/// holds it for its lifetime; a later start finds the name taken and must exit before it wires
/// anything (a second copy would lose the HTTP port and the hotkeys but could still attach a
/// second wallpaper).
/// </summary>
/// <remarks>
/// Decided by mutex creation, not by waiting, so the check never blocks. If the holder crashes,
/// the kernel closes its handle, the mutex object goes away and the next start creates it again.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>Session-local (<c>Local\</c>), so another signed-in user can run their own copy.</summary>
    public const string ProductionName = @"Local\CielWin.SingleInstance";

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the guard when this is the only holder of <paramref name="name"/>; otherwise null.</summary>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew) return new SingleInstanceGuard(mutex);

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null) return;

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Disposed from a thread that does not own it: closing the handle below still frees the name.
        }

        mutex.Dispose();
    }
}
