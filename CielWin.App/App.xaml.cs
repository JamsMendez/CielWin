using System.Windows;

namespace CielWin.App;

/// <summary>
/// Production entry point. Composition is wired in a later task; until then the app exits at once.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Shutdown();
    }
}
