using CielWin.App.Wallpaper;
using CielWin.Interop;
using CielWin.Interop.Win32;
using DrawingRectangle = System.Drawing.Rectangle;
using InteropRectangle = CielWin.Interop.Rectangle;

namespace CielWin.App.Tests.Wallpaper;

/// <summary>The mini window's cursor dodge as the controller drives it: polling, gliding aside and back, cancels.</summary>
public sealed class MiniSceneWindowDodgeTests
{
    private static readonly InteropRectangle WorkArea = new(0, 0, 2560, 1400);
    private static readonly InteropRectangle Home = InteropRectangle.FromSize(2272, 0, 288, 288);   // top-right
    private static readonly InteropRectangle Aside = InteropRectangle.FromSize(2272, 320, 288, 288); // below it
    private static readonly InteropRectangle OtherHome = InteropRectangle.FromSize(1136, 0, 288, 288); // top-center
    private static readonly ScreenPoint Near = new(2260, 144); // just left of home
    private static readonly ScreenPoint Far = new(100, 1300);

    private sealed class FakeSurface : IMiniSceneSurface
    {
        public List<InteropRectangle> Placed { get; } = [];
        public nint Hwnd => 42;
        public bool IsCompositionReady => true;
        public int CompositionGeneration => 1;
        public bool ReassertsTopmost => true;
        public bool TryCreate(InteropRectangle bounds) => true;

        public bool Place(InteropRectangle bounds)
        {
            Placed.Add(bounds);
            return true;
        }

        public object? AddCompositionOverlayVisual() => new object();
        public void RemoveCompositionOverlayVisual() { }
        public void CommitComposition() { }
        public void Dispose() { }
    }

    private sealed class FakeBrowser : IMiniSceneBrowser
    {
        public event Action? Ready { add { } remove { } }
        public event Action<string>? Failed { add { } remove { } }
        public Task<bool> AttachAsync(ICompositionOverlaySurface surface, DrawingRectangle viewport) => new TaskCompletionSource<bool>().Task;
        public void Navigate(string url) { }
        public void Resize(DrawingRectangle viewport) { }
        public void PostMessage(string json) { }
        public void Dispose() { }
    }

    private sealed class FakeFrames : IFrameSource
    {
        private Action? _onFrame;
        public bool Running => _onFrame is not null;
        public void Start(Action onFrame) => _onFrame = onFrame;
        public void Stop() => _onFrame = null;
        public void Frame() => _onFrame?.Invoke();
    }

    private sealed class FakeDesktop : IMiniDodgeDesktop
    {
        public ScreenPoint? Cursor;
        public InteropRectangle? Work = WorkArea;
        public Exception? Throw;
        public int CursorReads;

        public ScreenPoint? ReadCursor()
        {
            CursorReads++;
            if (Throw is { } error) throw error;
            return Cursor;
        }

        public InteropRectangle? WorkAreaOf(InteropRectangle bounds) => Work;
    }

    /// <summary>The poll timer, ticked by hand.</summary>
    private sealed class FakePoll
    {
        public TimeSpan? Interval;
        public Action? Callback;
        public bool Disposed;

        public IDisposable Schedule(TimeSpan interval, Action callback)
        {
            Interval = interval;
            Callback = callback;
            return new Stopper(this);
        }

        public void Tick() => Callback?.Invoke();

        private sealed class Stopper(FakePoll poll) : IDisposable
        {
            public void Dispose() => poll.Disposed = true;
        }
    }

    private readonly List<string> _trace = [];
    private readonly FakeFrames _frames = new();
    private readonly FakeDesktop _desktop = new();
    private readonly FakePoll _poll = new();
    private readonly FakeSurface _surface = new();
    private DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private MiniSceneWindowController Create(bool dodge = true) =>
        new(() => _surface, new FakeBrowser(), _trace.Add, () => _now, _frames,
            dodgeDesktop: dodge ? _desktop : null, schedulePoll: dodge ? _poll.Schedule : null);

    /// <summary>Runs the glide to its end; returns the bounds the last frame placed.</summary>
    private InteropRectangle Land()
    {
        _now += MiniSceneWindowController.GlideDuration;
        _frames.Frame();
        Assert.False(_frames.Running);
        return _surface.Placed[^1];
    }

    private MiniSceneWindowController ShownAndDodged()
    {
        var controller = Create();
        controller.Show(WallpaperScene.Processing, Home);
        _desktop.Cursor = Near;
        _poll.Tick();
        Assert.Equal(Aside, Land());
        return controller;
    }

    [Fact]
    public void ShowStartsPollingTheCursorEvery100Ms()
    {
        var controller = Create();
        Assert.Null(_poll.Callback);

        controller.Show(WallpaperScene.Processing, Home);

        Assert.Equal(TimeSpan.FromMilliseconds(100), _poll.Interval);
    }

    [Fact]
    public void WithoutADesktopThereIsNoPolling()
    {
        Create(dodge: false).Show(WallpaperScene.Processing, Home);

        Assert.Null(_poll.Callback);
    }

    [Fact]
    public void ANearCursorGlidesTheWindowAsideAndTracesTheDirection()
    {
        var controller = Create();
        controller.Show(WallpaperScene.Processing, Home);
        _desktop.Cursor = Near;

        _poll.Tick();

        Assert.True(_frames.Running);
        _now += TimeSpan.FromMilliseconds(110);
        _frames.Frame();
        var mid = _surface.Placed[^1];
        Assert.True(mid.Top > Home.Top && mid.Top < Aside.Top); // a glide, never a jump
        Assert.Equal(Aside, Land());
        Assert.Contains("mini-window: dodge direction=down", _trace);
    }

    [Fact]
    public void AFarCursorNeverMovesTheWindow()
    {
        var controller = Create();
        controller.Show(WallpaperScene.Processing, Home);
        var placed = _surface.Placed.Count;
        _desktop.Cursor = Far;

        _poll.Tick();

        Assert.False(_frames.Running);
        Assert.Equal(placed, _surface.Placed.Count);
    }

    [Fact]
    public void OnceTheCursorStaysAwayItGlidesBackHome()
    {
        ShownAndDodged();
        _desktop.Cursor = Far;

        _poll.Tick();
        _now += TimeSpan.FromMilliseconds(399);
        _poll.Tick();
        Assert.False(_frames.Running);
        _now += TimeSpan.FromMilliseconds(1);
        _poll.Tick();

        Assert.Equal(Home, Land());
        Assert.Contains("mini-window: dodge return", _trace);
    }

    [Fact]
    public void AGlideToANewPositionCancelsTheDodgeAndStartsFromWhereTheWindowIs()
    {
        var controller = ShownAndDodged();

        controller.GlideTo(OtherHome);
        _now += TimeSpan.FromMilliseconds(110);
        _frames.Frame();
        Assert.True(_surface.Placed[^1].Top > 0); // from the aside spot, not from the old home
        Assert.Equal(OtherHome, Land());

        // The old home is forgotten: a far cursor never sends the window back there.
        _desktop.Cursor = Far;
        _poll.Tick();
        _now += TimeSpan.FromSeconds(1);
        _poll.Tick();
        Assert.False(_frames.Running);
        Assert.Equal(OtherHome, _surface.Placed[^1]);
        Assert.DoesNotContain("mini-window: dodge return", _trace);
    }

    [Fact]
    public void AMoveCancelsTheDodge()
    {
        var controller = ShownAndDodged();

        controller.MoveTo(OtherHome);
        _desktop.Cursor = Far;
        _poll.Tick();
        _now += TimeSpan.FromSeconds(1);
        _poll.Tick();

        Assert.Equal(OtherHome, _surface.Placed[^1]);
        Assert.DoesNotContain("mini-window: dodge return", _trace);
    }

    [Fact]
    public void ADodgeLandingIsNotAMoveSoTheWindowStillReturnsHome()
    {
        ShownAndDodged();
        _desktop.Cursor = Far;

        _poll.Tick();
        _now += MiniDodge.ReturnDelay;
        _poll.Tick();

        Assert.Equal(Home, Land());
    }

    [Fact]
    public void DisposeStopsThePollingAndALateTickDoesNothing()
    {
        var controller = Create();
        controller.Show(WallpaperScene.Processing, Home);
        _desktop.Cursor = Near;

        controller.Dispose();
        _poll.Tick();

        Assert.True(_poll.Disposed);
        Assert.Equal(0, _desktop.CursorReads);
    }

    [Fact]
    public void AFailingTickNeverThrowsIsTracedOnceAndReportsThePlacementLost()
    {
        var controller = Create();
        controller.Show(WallpaperScene.Processing, Home);
        var lost = 0;
        controller.PlacementLost += () => lost++;
        _desktop.Throw = new InvalidOperationException("cursor");

        _poll.Tick();
        _poll.Tick();

        Assert.Single(_trace, line => line == "mini-window: dodge-failed error=InvalidOperationException");
        Assert.Equal(1, lost);

        // Recovered: the next failure is traced again.
        _desktop.Throw = null;
        _desktop.Cursor = Far;
        _poll.Tick();
        _desktop.Throw = new InvalidOperationException("cursor");
        _poll.Tick();
        Assert.Equal(2, _trace.Count(line => line.StartsWith("mini-window: dodge-failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void NoWorkAreaMeansNoDodge()
    {
        var controller = Create();
        controller.Show(WallpaperScene.Processing, Home);
        _desktop.Work = null;
        _desktop.Cursor = Near;

        _poll.Tick();

        Assert.False(_frames.Running);
    }
}
