namespace CielWin.Interop;

/// <summary>
/// A command server the composition root starts once and disposes on shutdown. Lets the root and its
/// tests hold the server without depending on the concrete HTTP listener.
/// </summary>
public interface IHttpCommandServer : IDisposable
{
    /// <summary>
    /// Starts listening on a background thread. Idempotent: a second call is a no-op. Never throws; a
    /// failure to start is reported through the constructor's diagnostic callback instead.
    /// </summary>
    void Start();
}
