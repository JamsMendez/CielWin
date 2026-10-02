using System.Diagnostics;
using CielWin.Interop.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// Decides whether a fact that touches the real desktop may run, as a pure function of its inputs.
/// </summary>
/// <remarks>
/// <para>
/// Three gates that compose from ONE opt-in check rather than repeating it.
/// <see cref="OptInSkipReason(string?)"/> is the floor: an interactive session the maintainer asked
/// for. <see cref="SessionSkipReason(string?, Func{bool})"/> adds "CielWin itself is not running", for
/// facts that attach a wallpaper host or create windows the running app would fight over.
/// <see cref="RaisedLayoutSkipReason(string?, Func{bool}, Func{bool})"/> adds the raised-desktop
/// (24H2+) layout.
/// </para>
/// <para>
/// The expensive probes arrive as delegates so the opt-in can settle the question before they run,
/// and the whole decision takes its inputs as arguments rather than reading the environment so it can
/// be pinned by headless facts.
/// </para>
/// </remarks>
public static class DesktopGate
{
    /// <summary>The opt-in. Absent, every desktop fact skips -- the default has to be OFF.</summary>
    public const string RunFlagVariable = "CIELWIN_RUN_DESKTOP_TESTS";

    /// <summary>Exact match, not truthiness: "0", "true" and " 1" are all somebody NOT opting in.</summary>
    private const string OptIn = "1";

    /// <summary>Reads the live opt-in so an attribute does not have to.</summary>
    public static string? OptInSkipReason() =>
        OptInSkipReason(Environment.GetEnvironmentVariable(RunFlagVariable));

    /// <inheritdoc cref="OptInSkipReason()"/>
    public static string? SessionSkipReason() =>
        SessionSkipReason(Environment.GetEnvironmentVariable(RunFlagVariable), AppRunning);

    /// <inheritdoc cref="OptInSkipReason()"/>
    public static string? RaisedLayoutSkipReason() =>
        RaisedLayoutSkipReason(Environment.GetEnvironmentVariable(RunFlagVariable), AppRunning, IsRaisedDesktopLayout);

    /// <summary>
    /// <see langword="null"/> to run, otherwise the reason the fact is skipped. Every other gate
    /// starts here, so the opt-in message is the one a reader sees first on a machine that never
    /// asked for any of this.
    /// </summary>
    public static string? OptInSkipReason(string? runFlag) =>
        runFlag == OptIn ? null : $"Set {RunFlagVariable}=1 in an interactive desktop session.";

    /// <summary>The opt-in, plus no running CielWin that owns the wallpaper layer these facts attach to.</summary>
    public static string? SessionSkipReason(string? runFlag, Func<bool> appRunning)
    {
        if (OptInSkipReason(runFlag) is { } optIn)
        {
            return optIn;
        }

        return appRunning()
            ? "CielWin.App is running and owns the wallpaper layer these facts attach to. " +
              "Exit it from the tray first."
            : null;
    }

    /// <summary>
    /// The session gate plus the raised-desktop (24H2+) layout <see cref="DesktopLayoutDetector"/>
    /// recognises. A fact asserting that <c>SHELLDLL_DefView</c> sits directly under the host's
    /// resolved parent holds only on this layout; on the legacy one DefView lives under a DIFFERENT
    /// top-level window than the wallpaper WorkerW the host attaches to, so the fact must SKIP rather
    /// than report a failure that is not a defect there.
    /// </summary>
    public static string? RaisedLayoutSkipReason(
        string? runFlag, Func<bool> appRunning, Func<bool> isRaisedLayout)
    {
        if (SessionSkipReason(runFlag, appRunning) is { } beneath)
        {
            return beneath;
        }

        return isRaisedLayout()
            ? null
            : "This machine uses the legacy WorkerW desktop layout, where SHELLDLL_DefView does not " +
              "sit under the scene wallpaper host's resolved parent -- not applicable here; this fact " +
              "only holds on the raised-desktop (24H2+) layout.";
    }

    private static bool AppRunning() => Process.GetProcessesByName("CielWin.App").Length > 0;

    /// <summary>
    /// The real probe behind <see cref="RaisedLayoutSkipReason()"/>: Progman's own
    /// <c>GWL_EXSTYLE</c>. A missing Progman reads as "not raised" rather than throwing -- a gate
    /// deciding whether to SKIP must never crash test discovery.
    /// </summary>
    private static bool IsRaisedDesktopLayout()
    {
        HWND progman = PInvoke.FindWindow(null, "Program Manager");
        if (progman == HWND.Null)
        {
            return false;
        }

        var exStyle = unchecked((uint)PInvoke.GetWindowLong(progman, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE));
        return DesktopLayoutDetector.IsRaisedDesktop(exStyle);
    }
}
