namespace CielWin.Interop;

/// <summary>
/// The wallpaper window behind the desktop icons, as the composition root drives it: attach it (and
/// re-attach it whenever the desktop shell may have replaced its layer), then let the WebView2 layer
/// render into its <see cref="ICompositionOverlaySurface"/>.
/// </summary>
/// <remarks>
/// <see cref="TryAttach"/> and <see cref="IDisposable.Dispose"/> must run on the thread that created
/// the window (see <see cref="MtaActionThread"/>); the composition members are safe from any thread.
/// </remarks>
public interface ISceneWallpaperHost : ICompositionOverlaySurface, IDisposable
{
    /// <summary>
    /// Creates the host window on first call and (re-)attaches it behind the desktop icons. Never
    /// throws; returns <see langword="false"/> when Explorer's desktop windows are not ready yet, so
    /// a caller can simply retry.
    /// </summary>
    bool TryAttach();
}
