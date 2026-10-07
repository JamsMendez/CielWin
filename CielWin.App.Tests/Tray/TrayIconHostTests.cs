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
    public void TheMenuIsOrdered_ModeThenSceneThenFrameRateThenTheSoundGroupThenExit()
    {
        Assert.Equal(
            [
                TrayMenuEntry.Mode, TrayMenuEntry.Scene, TrayMenuEntry.FrameRate,
                TrayMenuEntry.ImportFailedSound, TrayMenuEntry.ImportWarningSound,
                TrayMenuEntry.RemoveFailedSound, TrayMenuEntry.RemoveWarningSound,
                TrayMenuEntry.AlertSounds, TrayMenuEntry.Exit,
            ],
            TrayIconHost.MenuOrder);
    }

    [Theory]
    [InlineData(TrayMenuEntry.ImportFailedSound, "Import failed sound…")]
    [InlineData(TrayMenuEntry.ImportWarningSound, "Import warning sound…")]
    [InlineData(TrayMenuEntry.RemoveFailedSound, "Remove failed sound")]
    [InlineData(TrayMenuEntry.RemoveWarningSound, "Remove warning sound")]
    public void TheSoundEntries_AreLabelledInEnglish(TrayMenuEntry entry, string expected)
    {
        Assert.Equal(expected, TrayIconHost.SoundEntryLabel(entry));
    }

    [Fact]
    public void TheAlertSoundsToggle_IsLabelledInEnglish()
    {
        Assert.Equal("Alert sounds", TrayIconHost.AlertSoundsLabel);
    }

    [Fact]
    public void EveryEntryAppearsExactlyOnce()
    {
        Assert.Equal(Enum.GetValues<TrayMenuEntry>().Order(), TrayIconHost.MenuOrder.Order());
    }

    [Fact]
    public void TheModeMenu_IsLabelledSceneMode()
    {
        Assert.Equal("Scene Mode", TrayIconHost.ModeMenuLabel);
    }

    [Theory]
    [InlineData(WallpaperMode.Scene, "Scene Wallpaper")]
    [InlineData(WallpaperMode.SceneMini, "Scene Mini")]
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

    [Theory]
    [InlineData(30, "30 FPS")]
    [InlineData(60, "60 FPS")]
    public void FrameRateLabel_NamesEachRate(int fps, string expected)
    {
        Assert.Equal(expected, TrayIconHost.FrameRateLabel(fps));
    }

    [Fact]
    public void TheFrameRateSubmenu_IsLabelledInEnglish()
    {
        Assert.Equal("Frame rate", TrayIconHost.FrameRateMenuLabel);
    }

    /// <summary>
    /// The check mark follows the controller's rate across a switch and back: the menu is re-checked on
    /// every open, so a stale or inverted mapping would show the wrong rate as current.
    /// </summary>
    [Fact]
    public void OnlyTheCurrentFrameRate_IsChecked_AcrossASwitchAndBack()
    {
        static string[] CheckedLabels(int current) =>
            TrayMenuController.FrameRates
                .Where(fps => TrayIconHost.IsFrameRateChecked(fps, current))
                .Select(TrayIconHost.FrameRateLabel)
                .ToArray();

        Assert.Equal(["60 FPS"], CheckedLabels(60));
        Assert.Equal(["30 FPS"], CheckedLabels(30));
        Assert.Equal(["60 FPS"], CheckedLabels(60));
    }

    [Fact]
    public void EveryModeAndSceneHasADistinctLabel()
    {
        Assert.Equal(TrayMenuController.Modes.Count, TrayMenuController.Modes.Select(TrayIconHost.ModeLabel).Distinct().Count());
        Assert.Equal(TrayMenuController.Scenes.Count, TrayMenuController.Scenes.Select(TrayIconHost.SceneLabel).Distinct().Count());
    }

    /// <summary>
    /// A click arrives as a raw WinForms message inside the WPF dispatcher loop: a throw escaping the
    /// handler would be an unhandled exception and end the app. The guard traces the item and the
    /// exception TYPE (never its message, which can hold a path) and returns normally.
    /// </summary>
    [Fact]
    public void AGuardedClickThatThrows_IsTracedByTypeName_AndDoesNotEscape()
    {
        var trace = new List<string>();
        var handler = TrayIconHost.Guarded("exit", () => throw new InvalidOperationException("C:\\secret\\path"), trace.Add);

        handler(this, EventArgs.Empty);

        var line = Assert.Single(trace);
        Assert.Equal("tray click-failed item=exit error=InvalidOperationException", line);
    }

    [Fact]
    public void AGuardedClick_RunsItsAction_WithoutTracing()
    {
        var trace = new List<string>();
        var clicks = 0;
        var handler = TrayIconHost.Guarded("scene", () => clicks++, trace.Add);

        handler(this, EventArgs.Empty);

        Assert.Equal(1, clicks);
        Assert.Empty(trace);
    }

    [Fact]
    public void AGuardedClick_WhoseTraceAlsoThrows_StillDoesNotEscape()
    {
        var handler = TrayIconHost.Guarded(
            "mode", () => throw new InvalidOperationException("boom"), _ => throw new IOException("sink down"));

        handler(this, EventArgs.Empty);
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

    /// <summary>
    /// The 16, 20 and 24 px frames get a contrast pass in tools/tray-icon/render-raphael-mini.mjs so the
    /// figure reads on a light taskbar. Thresholds come from measured values with wide margin:
    /// <list type="bullet">
    /// <item>Mostly opaque pixels (alpha &gt;= 192) cover at least 25% of the frame: measured 43-48% after
    /// the pass, 2% before it (the averaged disc was a pale, half-transparent blur).</item>
    /// <item>A dark rim: at least one dark (luma &lt; 80), solid (alpha &gt;= 128) pixel per pixel of width
    /// that touches the transparent outside: measured 49/56/67 after, 0 before.</item>
    /// <item>Still a figure, not a filled square: the four corners stay fully transparent and opaque pixels
    /// cover at most 75% of the frame.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void TheSmallTrayIconFrames_KeepTheirContrastPass()
    {
        using var stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream(TrayIconHost.IconResourceName);
        Assert.NotNull(stream);

        var frames = ReadSmallFrames(stream);

        Assert.Equal([16, 20, 24], frames.Select(frame => frame.Size));
        foreach (var (size, pixels) in frames)
        {
            int Alpha(int x, int y) => x < 0 || y < 0 || x >= size || y >= size ? 0 : pixels[(y * size) + x].A;

            var opaque = pixels.Count(pixel => pixel.A >= 192);
            var rim = 0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var (r, g, b, a) = pixels[(y * size) + x];
                    var luma = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                    var touchesOutside = Math.Min(Math.Min(Alpha(x + 1, y), Alpha(x - 1, y)), Math.Min(Alpha(x, y + 1), Alpha(x, y - 1))) < 64;
                    if (a >= 128 && luma < 80 && touchesOutside)
                    {
                        rim++;
                    }
                }
            }

            var area = size * size;
            Assert.True(opaque >= area * 0.25, $"{size} px: {opaque} of {area} pixels mostly opaque (pale frame)");
            Assert.True(opaque <= area * 0.75, $"{size} px: {opaque} of {area} pixels mostly opaque (filled frame)");
            Assert.True(rim >= size, $"{size} px: {rim} dark rim pixels, expected at least {size}");
            Assert.Equal([0, 0, 0, 0], new[] { Alpha(0, 0), Alpha(size - 1, 0), Alpha(0, size - 1), Alpha(size - 1, size - 1) });
        }
    }

    /// <summary>
    /// Decodes the icon frames of 24 px and below. They are 32-bit BMP entries: a BITMAPINFOHEADER, then
    /// bottom-up BGRA rows (the AND mask after them is ignored; the alpha channel is authoritative).
    /// </summary>
    private static List<(int Size, (byte R, byte G, byte B, byte A)[] Pixels)> ReadSmallFrames(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.ToArray();

        var frames = new List<(int, (byte, byte, byte, byte)[])>();
        var count = BitConverter.ToUInt16(data, 4);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            var size = data[entry] == 0 ? 256 : data[entry];
            if (size > 24)
            {
                continue;
            }

            var offset = BitConverter.ToInt32(data, entry + 12);
            var bits = offset + BitConverter.ToInt32(data, offset);
            Assert.Equal(32, BitConverter.ToUInt16(data, offset + 14));
            var pixels = new (byte, byte, byte, byte)[size * size];
            for (var y = 0; y < size; y++)
            {
                var row = bits + ((size - 1 - y) * size * 4);
                for (var x = 0; x < size; x++)
                {
                    var p = row + (x * 4);
                    pixels[(y * size) + x] = (data[p + 2], data[p + 1], data[p], data[p + 3]);
                }
            }

            frames.Add((size, pixels));
        }

        return frames;
    }
}
