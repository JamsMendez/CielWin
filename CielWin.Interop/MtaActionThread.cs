using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop;

/// <summary>
/// A dedicated MTA thread that runs posted work in order AND pumps its own Win32 message queue
/// between items. The wallpaper host window is created on this thread, so the thread has to pump:
/// the <c>TaskbarCreated</c> re-attach after an Explorer restart only fires if the host's hidden
/// receiver window is serviced. MTA, because DirectComposition RCWs minted here must not pin an STA.
/// </summary>
public sealed class MtaActionThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;
    private readonly Action<string>? _onWorkFailed;
    private bool _disposed;

    /// <param name="name">Thread name, visible in debuggers and traces.</param>
    /// <param name="onWorkFailed">
    /// Invoked with the exception TYPE name only (never <see cref="Exception.Message"/>, which can hold
    /// an absolute path) when posted work throws. The loop keeps serving later work either way.
    /// </param>
    public MtaActionThread(string name, Action<string>? onWorkFailed = null)
    {
        _onWorkFailed = onWorkFailed;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Queues <paramref name="work"/> and returns immediately. Ignored once disposed.</summary>
    public void Post(Action work) => _ = TryPost(work);

    /// <summary>
    /// Runs <paramref name="work"/> on the thread and waits (default five seconds) for it. A failure
    /// inside the work is rethrown wrapped.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the work ran to completion; <c>false</c> when it was never queued (disposed)
    /// or the wait timed out, in which case it may still run later, so callers must not assume it
    /// either did or did not happen.
    /// </returns>
    public bool Invoke(Action work, TimeSpan? timeout = null)
    {
        if (Thread.CurrentThread == _thread)
        {
            work();
            return true;
        }

        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(() =>
        {
            try
            {
                work();
                completed.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completed.TrySetResult(exception);
            }
        }))
        {
            return false;
        }

        if (!completed.Task.Wait(timeout ?? TimeSpan.FromSeconds(5)))
        {
            return false;
        }

        if (completed.Task.Result is { } failure)
        {
            throw new InvalidOperationException("Scene wallpaper thread work failed.", failure);
        }

        return true;
    }

    private bool TryPost(Action work)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            _queue.Add(work);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(5)))
        {
            _queue.Dispose();
        }
    }

    private void Run()
    {
        while (!_queue.IsCompleted)
        {
            if (_queue.TryTake(out var work, millisecondsTimeout: 16))
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    // Posted wallpaper work must not kill the thread that owns the host HWND.
                    // Synchronous Invoke callers wrap their own failures before they get here.
                    // Only the TYPE reaches the sink: exception.Message can hold an absolute path.
                    // The sink is guarded too, so "keeps serving later work" never depends on it --
                    // an exception escaping here would end this thread and, unhandled on a
                    // background thread, the whole process.
                    try
                    {
                        _onWorkFailed?.Invoke(exception.GetType().Name);
                    }
                    catch
                    {
                    }
                }
            }

            PumpThreadMessages();
        }
    }

    private static void PumpThreadMessages()
    {
        while (PInvoke.PeekMessage(out MSG message, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }
}
