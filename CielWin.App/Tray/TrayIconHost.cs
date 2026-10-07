using System.Drawing;
using System.Windows.Forms;
using CielWin.App.Alerts;

namespace CielWin.App.Tray;

/// <summary>
/// Thin WinForms <see cref="NotifyIcon"/>/<see cref="ContextMenuStrip"/> wrapper, the sole owner of
/// the tray icon and its menu. Holds no behavior beyond forwarding clicks to
/// <see cref="TrayMenuController"/> through <see cref="Guarded"/>; the rest needs a live notification
/// area and is verified by hand.
/// Constructed on the WPF UI thread, whose dispatcher loop pumps it.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _ownedIcon;
    private readonly ContextMenuStrip _menu;
    private readonly List<Image> _images = [];

    public TrayIconHost(TrayMenuController controller, Action<string> trace)
    {
        var modeItem = new ToolStripMenuItem(ModeMenuLabel) { Image = Track(TrayGlyphs.Render(TrayGlyphs.Mode)) };
        var modeItems = new List<(WallpaperMode Mode, ToolStripMenuItem Item)>();
        foreach (var mode in TrayMenuController.Modes)
        {
            var item = new ToolStripMenuItem(ModeLabel(mode));
            item.Click += Guarded("mode", () => controller.SelectMode(mode), trace);
            modeItem.DropDownItems.Add(item);
            modeItems.Add((mode, item));
        }

        var sceneItem = new ToolStripMenuItem("Scene") { Image = Track(TrayGlyphs.Render(TrayGlyphs.Scene)) };
        var sceneItems = new List<(WallpaperScene Scene, ToolStripMenuItem Item)>();
        foreach (var scene in TrayMenuController.Scenes)
        {
            var item = new ToolStripMenuItem(SceneLabel(scene));
            item.Click += Guarded("scene", () => controller.SelectScene(scene), trace);
            sceneItem.DropDownItems.Add(item);
            sceneItems.Add((scene, item));
        }

        var frameRateItem = new ToolStripMenuItem(FrameRateMenuLabel);
        var frameRateItems = new List<(int Fps, ToolStripMenuItem Item)>();
        foreach (var fps in TrayMenuController.FrameRates)
        {
            var item = new ToolStripMenuItem(FrameRateLabel(fps));
            item.Click += Guarded("frame-rate", () => controller.SelectFrameRate(fps), trace);
            frameRateItem.DropDownItems.Add(item);
            frameRateItems.Add((fps, item));
        }

        var alertSoundsItem = new ToolStripMenuItem(AlertSoundsLabel);
        alertSoundsItem.Click += Guarded("alert-sounds", controller.ToggleAlertSounds, trace);

        ToolStripMenuItem SoundItem(TrayMenuEntry entry, string item, Action click)
        {
            var menuItem = new ToolStripMenuItem(SoundEntryLabel(entry));
            menuItem.Click += Guarded(item, click, trace);
            return menuItem;
        }

        var exitItem = new ToolStripMenuItem("Exit") { Image = Track(TrayGlyphs.Render(TrayGlyphs.Exit)) };
        exitItem.Click += Guarded("exit", controller.Exit, trace);

        _menu = new ContextMenuStrip();
        var items = new Dictionary<TrayMenuEntry, ToolStripItem>
        {
            [TrayMenuEntry.Mode] = modeItem,
            [TrayMenuEntry.Scene] = sceneItem,
            [TrayMenuEntry.FrameRate] = frameRateItem,
            [TrayMenuEntry.ImportFailedSound] = SoundItem(
                TrayMenuEntry.ImportFailedSound, "import-failed-sound", () => controller.ImportAlertSound(AlertKind.Failed)),
            [TrayMenuEntry.ImportWarningSound] = SoundItem(
                TrayMenuEntry.ImportWarningSound, "import-warning-sound", () => controller.ImportAlertSound(AlertKind.Warning)),
            [TrayMenuEntry.RemoveFailedSound] = SoundItem(
                TrayMenuEntry.RemoveFailedSound, "remove-failed-sound", () => controller.RemoveAlertSound(AlertKind.Failed)),
            [TrayMenuEntry.RemoveWarningSound] = SoundItem(
                TrayMenuEntry.RemoveWarningSound, "remove-warning-sound", () => controller.RemoveAlertSound(AlertKind.Warning)),
            [TrayMenuEntry.AlertSounds] = alertSoundsItem,
            [TrayMenuEntry.Exit] = exitItem,
        };
        foreach (var entry in MenuOrder)
        {
            if (entry is TrayMenuEntry.ImportFailedSound or TrayMenuEntry.Exit)
            {
                _menu.Items.Add(new ToolStripSeparator());
            }

            _menu.Items.Add(items[entry]);
        }

        // The ticks and the shown entries are re-read on every open, never flipped by WinForms
        // (CheckOnClick stays off): the scene also changes over HTTP, and the controller is the only
        // owner of the state.
        void RefreshChecks()
        {
            alertSoundsItem.Checked = controller.AlertSoundsEnabled;
            foreach (var (entry, item) in items)
            {
                // Available, not Visible: Visible reads false whenever the menu is closed.
                item.Available = controller.IsVisible(entry);
            }

            foreach (var (mode, item) in modeItems)
            {
                item.Checked = controller.Mode == mode;
            }

            foreach (var (scene, item) in sceneItems)
            {
                item.Checked = controller.Scene == scene;
            }

            foreach (var (fps, item) in frameRateItems)
            {
                item.Checked = IsFrameRateChecked(fps, controller.FrameRate);
            }
        }

        RefreshChecks();
        _menu.Opening += (_, _) => RefreshChecks();

        _ownedIcon = LoadTrayIcon();
        _icon = new NotifyIcon
        {
            Icon = _ownedIcon ?? SystemIcons.Application,
            Text = "CielWin",
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    /// <summary>
    /// The embedded icon: the Raphael scene's mini figure on a transparent background, generated by
    /// <c>tools/tray-icon/render-raphael-mini.mjs</c> (also the executable's icon).
    /// </summary>
    public const string IconResourceName = "CielWin.App.Assets.raphael-mini.ico";

    /// <summary>
    /// The order the items appear in: the mode switch, the scene switch, the frame-rate switch, the
    /// sound group (imports, removes, the mute toggle), then exit. A separator precedes the sound group
    /// and exit.
    /// </summary>
    public static IReadOnlyList<TrayMenuEntry> MenuOrder { get; } =
    [
        TrayMenuEntry.Mode,
        TrayMenuEntry.Scene,
        TrayMenuEntry.FrameRate,
        TrayMenuEntry.ImportFailedSound,
        TrayMenuEntry.ImportWarningSound,
        TrayMenuEntry.RemoveFailedSound,
        TrayMenuEntry.RemoveWarningSound,
        TrayMenuEntry.AlertSounds,
        TrayMenuEntry.Exit,
    ];

    public const string ModeMenuLabel = "Scene Mode";

    public const string AlertSoundsLabel = "Alert sounds";

    public static string SoundEntryLabel(TrayMenuEntry entry) => entry switch
    {
        TrayMenuEntry.ImportFailedSound => "Import failed sound…",
        TrayMenuEntry.ImportWarningSound => "Import warning sound…",
        TrayMenuEntry.RemoveFailedSound => "Remove failed sound",
        TrayMenuEntry.RemoveWarningSound => "Remove warning sound",
        _ => entry.ToString(),
    };

    public static string ModeLabel(WallpaperMode mode) => mode switch
    {
        WallpaperMode.Scene => "Scene Wallpaper",
        WallpaperMode.SceneMini => "Scene Mini",
        _ => mode.ToString(),
    };

    public static string SceneLabel(WallpaperScene scene) => scene.ToString();

    public const string FrameRateMenuLabel = "Frame rate";

    public static string FrameRateLabel(int fps) => $"{fps} FPS";

    /// <summary>
    /// Whether the frame-rate item for <paramref name="itemFps"/> carries the check mark while the
    /// wallpaper runs at <paramref name="currentFps"/>: only the current rate's item is checked.
    /// </summary>
    internal static bool IsFrameRateChecked(int itemFps, int currentFps) => itemFps == currentFps;

    /// <summary>
    /// A click handler that runs <paramref name="click"/> and never lets it throw: the click arrives as
    /// a raw window message inside the WPF dispatcher loop, where an escaping exception ends the app.
    /// A failure is traced with the exception TYPE only (a message can hold a path); a throwing trace
    /// is swallowed too.
    /// </summary>
    internal static EventHandler Guarded(string item, Action click, Action<string> trace) => (_, _) =>
    {
        try
        {
            click();
        }
        catch (Exception error)
        {
            try
            {
                trace($"tray click-failed item={item} error={error.GetType().Name}");
            }
            catch
            {
            }
        }
    };

    private Image? Track(Image? image)
    {
        if (image is not null)
        {
            _images.Add(image);
        }

        return image;
    }

    /// <summary>
    /// The embedded multi-size icon at the notification area's size, so Windows picks the frame drawn
    /// for this DPI. Null (falls back to the system icon) rather than failing startup.
    /// </summary>
    private static Icon? LoadTrayIcon()
    {
        try
        {
            using var stream = typeof(TrayIconHost).Assembly.GetManifestResourceStream(IconResourceName);
            return stream is null ? null : new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception exception) when (exception is ArgumentException or System.IO.IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        // NotifyIcon does not own its Icon and a menu item does not own its Image.
        _ownedIcon?.Dispose();
        _menu.Dispose();
        foreach (var image in _images)
        {
            image.Dispose();
        }
    }
}
