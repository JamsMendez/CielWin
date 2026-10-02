namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// Marks a fact that needs an interactive desktop session the maintainer opted into, and no running
/// CielWin to own the wallpaper layer the fact attaches to.
/// </summary>
/// <remarks>
/// The <c>RequiresDesktop</c> trait is NOT this gate. The trait is what CI filters on; it does
/// nothing on a developer's machine, where a plain <c>dotnet test</c> would still run the fact
/// against whatever that desktop happens to have open. The decision itself lives in
/// <see cref="DesktopGate"/>, where it can be proven.
/// </remarks>
internal sealed class RequiresDesktopSessionFactAttribute : FactAttribute
{
    public RequiresDesktopSessionFactAttribute()
    {
        if (DesktopGate.SessionSkipReason() is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// The session gate PLUS the raised-desktop (24H2+) layout (see
/// <see cref="DesktopGate.RaisedLayoutSkipReason()"/>).
/// </summary>
/// <remarks>
/// A fact whose assertions assume <c>SHELLDLL_DefView</c> sits directly under the wallpaper host's
/// resolved parent must SKIP on the legacy WorkerW layout instead of reporting a failure that is not
/// a defect there. xunit 2 cannot skip from inside a running test body, so the runtime-observed
/// machine characteristic is decided here, in the attribute constructor.
/// </remarks>
internal sealed class RequiresRaisedDesktopLayoutFactAttribute : FactAttribute
{
    public RequiresRaisedDesktopLayoutFactAttribute()
    {
        if (DesktopGate.RaisedLayoutSkipReason() is { } reason)
        {
            Skip = reason;
        }
    }
}
