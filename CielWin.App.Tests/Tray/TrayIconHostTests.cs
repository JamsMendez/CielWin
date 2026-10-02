using CielWin.App.Tray;

namespace CielWin.App.Tests.Tray;

/// <summary>
/// The pure slivers of <see cref="TrayIconHost"/>: the menu order and the labels. Everything else
/// needs a live notification area and is verified by hand.
/// </summary>
public sealed class TrayIconHostTests
{
    /// <summary>
    /// The REAL order: the constructor adds its items by walking this list, so a menu built in a
    /// different order cannot pass.
    /// </summary>
    [Fact]
    public void TheMenuIsOrdered_ModeThenSceneThenExit()
    {
        Assert.Equal([TrayMenuEntry.Mode, TrayMenuEntry.Scene, TrayMenuEntry.Exit], TrayIconHost.MenuOrder);
    }

    [Fact]
    public void EveryEntryAppearsExactlyOnce()
    {
        Assert.Equal(Enum.GetValues<TrayMenuEntry>().Order(), TrayIconHost.MenuOrder.Order());
    }

    [Theory]
    [InlineData(WallpaperMode.Scene, "Scene wallpaper")]
    [InlineData(WallpaperMode.SceneMini, "Mini window")]
    public void ModeLabel_NamesEachMode(WallpaperMode mode, string expected)
    {
        Assert.Equal(expected, TrayIconHost.ModeLabel(mode));
    }

    [Theory]
    [InlineData(WallpaperScene.Processing, "Processing")]
    [InlineData(WallpaperScene.Explorer, "Explorer")]
    [InlineData(WallpaperScene.Idle, "Idle")]
    [InlineData(WallpaperScene.Raphael, "Raphael")]
    public void SceneLabel_NamesEachScene(WallpaperScene scene, string expected)
    {
        Assert.Equal(expected, TrayIconHost.SceneLabel(scene));
    }

    [Fact]
    public void EveryModeAndSceneHasADistinctLabel()
    {
        Assert.Equal(TrayMenuController.Modes.Count, TrayMenuController.Modes.Select(TrayIconHost.ModeLabel).Distinct().Count());
        Assert.Equal(TrayMenuController.Scenes.Count, TrayMenuController.Scenes.Select(TrayIconHost.SceneLabel).Distinct().Count());
    }

    /// <summary>
    /// The tray (and app) icon is the generated Raphael mini figure (tools/tray-icon), embedded with
    /// every size the notification area and Explorer ask for.
    /// </summary>
    [Fact]
    public void TheTrayIconIsEmbedded_WithEveryRequestedSize()
    {
        using var stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream(TrayIconHost.IconResourceName);
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = reader.ReadBytes(16);
            sizes.Add(entry[0] == 0 ? 256 : entry[0]);
            Assert.Equal(32, BitConverter.ToUInt16(entry, 6));
        }

        Assert.Equal([16, 20, 24, 32, 48, 256], sizes);
    }
}
