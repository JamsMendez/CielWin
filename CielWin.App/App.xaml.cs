using System.Windows;
using CielWin.App.Composition;

namespace CielWin.App;

/// <summary>
/// Production entry point. Delegates everything to <see cref="ProductionComposition.Wire"/> and
/// disposes the result on exit; the wiring itself is tested through <see cref="AppComposition"/>.
/// </summary>
public partial class App : Application
{
    private AppComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _composition = ProductionComposition.Wire(Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _composition?.Dispose();
        base.OnExit(e);
    }
}
