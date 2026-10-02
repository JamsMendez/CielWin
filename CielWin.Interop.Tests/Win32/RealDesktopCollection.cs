namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// Serialises every fact that touches the one real desktop this machine has. xunit runs test
/// CLASSES in parallel by default, and a desktop fact is not isolated from its neighbours: attaching
/// a wallpaper host or creating a topmost window changes state another class may be asserting on.
/// Every <c>RequiresDesktop</c> class joins this collection so they run one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealDesktopCollection
{
    public const string Name = "RealDesktop";
}
