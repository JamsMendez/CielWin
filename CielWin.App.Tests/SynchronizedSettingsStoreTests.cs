namespace CielWin.App.Tests;

/// <summary>
/// The lock discipline of <see cref="SynchronizedSettingsStore"/>, proven against the same
/// <see cref="Settings"/> record production uses.
/// </summary>
public sealed class SynchronizedSettingsStoreTests
{
    /// <summary>
    /// Each update sleeps inside its change function: without a lock held for the WHOLE
    /// read-modify-write, both threads would read the same pre-update snapshot and the later write
    /// would clobber the other's field.
    /// </summary>
    [Fact]
    public void Update_TwoConcurrentUpdatesToDifferentFields_BothSurvive()
    {
        var saved = new List<Settings>();
        var store = new SynchronizedSettingsStore(Settings.Default, saved.Add);
        var start = new ManualResetEventSlim(false);

        var modeThread = new Thread(() =>
        {
            start.Wait();
            store.Update(s =>
            {
                Thread.Sleep(50);
                return s with { WallpaperMode = WallpaperMode.SceneMini };
            });
        });
        var sceneThread = new Thread(() =>
        {
            start.Wait();
            store.Update(s =>
            {
                Thread.Sleep(50);
                return s with { WallpaperScene = WallpaperScene.Raphael };
            });
        });

        modeThread.Start();
        sceneThread.Start();
        start.Set();

        Assert.True(modeThread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(sceneThread.Join(TimeSpan.FromSeconds(5)));

        Assert.Equal(WallpaperMode.SceneMini, store.Current.WallpaperMode);
        Assert.Equal(WallpaperScene.Raphael, store.Current.WallpaperScene);

        // What reaches disk is the argument handed to save, so the LAST saved snapshot must carry
        // both fields too.
        Assert.Equal(2, saved.Count);
        Assert.Equal(WallpaperMode.SceneMini, saved[^1].WallpaperMode);
        Assert.Equal(WallpaperScene.Raphael, saved[^1].WallpaperScene);
    }

    /// <summary>
    /// Memory is assigned BEFORE save runs and a throwing save propagates uncaught, so the next
    /// successful update saves both changes: one save behind, never lost.
    /// </summary>
    [Fact]
    public void Update_WhenSaveThrows_MemoryStaysAheadAndTheNextSuccessfulUpdateSavesBothChanges()
    {
        var saved = new List<Settings>();
        var saveCallCount = 0;
        void Save(Settings settings)
        {
            saveCallCount++;
            if (saveCallCount == 1)
            {
                throw new IOException("disk full");
            }

            saved.Add(settings);
        }

        var store = new SynchronizedSettingsStore(Settings.Default, Save);

        var thrown = Assert.Throws<IOException>(() => store.Update(s => s with { HttpServerEnabled = false }));
        Assert.Equal("disk full", thrown.Message);
        Assert.False(store.Current.HttpServerEnabled);

        store.Update(s => s with { MiniPosition = MiniPosition.BottomLeft });

        var onlySavedSnapshot = Assert.Single(saved);
        Assert.False(onlySavedSnapshot.HttpServerEnabled);
        Assert.Equal(MiniPosition.BottomLeft, onlySavedSnapshot.MiniPosition);
    }
}
