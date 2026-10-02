using System.Windows;
using CielWin.App.Composition;

namespace CielWin.App;

/// <summary>
/// Production entry point. Exits at once when another copy already runs in this session
/// (<see cref="SingleInstanceGuard"/>); otherwise delegates everything to
/// <see cref="ProductionComposition.Wire"/> and disposes the result on exit. The wiring itself is
/// tested through <see cref="AppComposition"/>.
/// </summary>
public partial class App : Application
{
    private SingleInstanceGuard? _instance;
    private AppComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = SingleInstanceGuard.TryAcquire(SingleInstanceGuard.ProductionName);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        _composition = ProductionComposition.Wire(Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _composition?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
