namespace CielWin.App;

/// <summary>
/// The ONE place persist closures read and rewrite the captured settings snapshot, serialized, so
/// updates from different threads (the UI thread's tray and hotkey handlers, the HTTP server's scene
/// switch) can never interleave their read-modify-write of the SAME snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Without it, each closure would do <c>save(stored = stored with { Field = value })</c> directly:
/// two concurrent closures would read the same pre-update snapshot and whichever assigns last would
/// silently discard the other's field. Holding one lock across the whole read-modify-write-and-save
/// makes concurrent updates run entirely before or after each other.
/// </para>
/// <para>
/// Deliberately minimal: no queueing, no async, no batching. A throwing <c>save</c> is NOT caught:
/// <see cref="Update"/> assigns the new snapshot BEFORE calling <c>save</c>, so memory always
/// reflects the change and the failure propagates to the caller. The next successful update saves
/// both changes: one save behind, never lost. In production <c>save</c> is <see cref="SettingsFile.Save(string, Settings, Action{string}?)"/>,
/// which swallows IO failures itself.
/// </para>
/// </remarks>
public sealed class SynchronizedSettingsStore(Settings initial, Action<Settings> save)
{
    private readonly object _gate = new();
    private Settings _current = initial;

    /// <summary>The current in-memory snapshot, read under the same lock <see cref="Update"/> writes under.</summary>
    public Settings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the CURRENT snapshot and saves the result, all under one
    /// lock. <paramref name="change"/> always receives the most recently applied update, never a
    /// value read before this call was made.
    /// </summary>
    public void Update(Func<Settings, Settings> change)
    {
        lock (_gate)
        {
            _current = change(_current);
            save(_current);
        }
    }
}
