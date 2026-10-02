namespace CielWin.Interop.Tests;

/// <summary>
/// <see cref="MtaActionThread"/>'s posted-work loop must report the exception TYPE of a failed work
/// item -- never <see cref="Exception.Message"/>, which can hold an absolute path -- through an
/// injected sink, and keep serving later work either way.
/// </summary>
public sealed class MtaActionThreadTests
{
    [Fact]
    public void Run_WhenPostedWorkThrows_ReportsOnlyTheExceptionTypeAndKeepsServingLaterWork()
    {
        var reported = new List<string>();
        using var thread = new MtaActionThread("MtaActionThreadTests", onWorkFailed: message => reported.Add(message));
        var secondItemRan = new ManualResetEventSlim(initialState: false);

        thread.Post(() => throw new IOException(@"C:\Users\x\secret.html"));
        thread.Post(() => secondItemRan.Set());

        Assert.True(secondItemRan.Wait(TimeSpan.FromSeconds(5)), "the second work item never ran");
        AssertEventually(() => Assert.Single(reported));
        Assert.Contains(nameof(IOException), reported[0]);
        Assert.DoesNotContain(@"C:\Users\x\secret.html", reported[0]);
    }

    /// <summary>
    /// The "keeps serving" guarantee must not depend on the sink: a sink that throws must not let the
    /// exception escape the loop, which would end the thread (and, unhandled on a background thread,
    /// the process).
    /// </summary>
    [Fact]
    public void Run_WhenTheFailureSinkItselfThrows_KeepsServingLaterWork()
    {
        using var thread = new MtaActionThread(
            "MtaActionThreadTests.ThrowingSink",
            onWorkFailed: _ => throw new InvalidOperationException("sink failed"));
        var secondItemRan = new ManualResetEventSlim(initialState: false);

        thread.Post(() => throw new IOException("work failed"));
        thread.Post(() => secondItemRan.Set());

        Assert.True(secondItemRan.Wait(TimeSpan.FromSeconds(5)), "the second work item never ran");
    }

    [Fact]
    public void Invoke_RunsTheWorkOnTheDedicatedMtaThreadAndWaitsForIt()
    {
        using var thread = new MtaActionThread("MtaActionThreadTests.Invoke");
        ApartmentState? apartment = null;
        var callerThreadId = Environment.CurrentManagedThreadId;
        var workThreadId = 0;

        thread.Invoke(() =>
        {
            apartment = Thread.CurrentThread.GetApartmentState();
            workThreadId = Environment.CurrentManagedThreadId;
        });

        Assert.Equal(ApartmentState.MTA, apartment);
        Assert.NotEqual(callerThreadId, workThreadId);
    }

    [Fact]
    public void Invoke_WhenTheWorkThrows_SurfacesItToTheCaller()
    {
        using var thread = new MtaActionThread("MtaActionThreadTests.InvokeThrows");

        var failure = Assert.Throws<InvalidOperationException>(() => thread.Invoke(() => throw new IOException("boom")));

        Assert.IsType<IOException>(failure.InnerException);
    }

    [Fact]
    public void Invoke_WhenTheWorkCompletes_ReturnsTrue()
    {
        using var thread = new MtaActionThread("MtaActionThreadTests.InvokeTrue");

        Assert.True(thread.Invoke(() => { }));
    }

    /// <summary>
    /// A wait timeout leaves the work queued, so the caller must be able to tell it did not finish
    /// (a bare return looked exactly like success).
    /// </summary>
    [Fact]
    public void Invoke_WhenTheThreadIsBusyPastTheTimeout_ReturnsFalse()
    {
        using var thread = new MtaActionThread("MtaActionThreadTests.InvokeTimeout");
        var release = new ManualResetEventSlim(initialState: false);
        thread.Post(() => release.Wait(TimeSpan.FromSeconds(10)));

        var completed = thread.Invoke(() => { }, timeout: TimeSpan.FromMilliseconds(100));
        release.Set();

        Assert.False(completed);
    }

    [Fact]
    public void Invoke_AfterDispose_ReturnsFalse()
    {
        var thread = new MtaActionThread("MtaActionThreadTests.InvokeAfterDispose");
        thread.Dispose();

        Assert.False(thread.Invoke(() => { }));
    }

    [Fact]
    public void Post_AfterDispose_IsIgnored()
    {
        var thread = new MtaActionThread("MtaActionThreadTests.PostAfterDispose");
        thread.Dispose();
        var ran = false;

        thread.Post(() => ran = true);
        Thread.Sleep(50);

        Assert.False(ran);
    }

    /// <summary>Waits briefly for an async assertion to stop failing, instead of racing the worker thread.</summary>
    private static void AssertEventually(Action assertion)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            try
            {
                assertion();
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
        }
    }
}
