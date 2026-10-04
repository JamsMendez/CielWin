using System.Globalization;
using CielWin.App.Alerts;
using CielWin.Interop;

namespace CielWin.App;

/// <summary>Where the scene is shown.</summary>
public enum WallpaperMode
{
    /// <summary>The scene is composited over the desktop wallpaper, full screen.</summary>
    Scene,

    /// <summary>
    /// No wallpaper host: a small, always-on-top, click-through scene window sits at
    /// <see cref="Settings.MiniPosition"/> of the work area; the desktop background stays as Windows
    /// has it.
    /// </summary>
    SceneMini,
}

/// <summary>The work-area position the mini scene window sits in: four corners and four side midpoints, ordered clockwise.</summary>
public enum MiniPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    RightCenter,
    BottomRight,
    BottomCenter,
    BottomLeft,
    LeftCenter,
}

/// <summary>
/// Which of the four bundled scenes is shown. A closed, compile-time-fixed set, so no text from the
/// settings file or the HTTP API can reach a scene page URL as a raw, unvalidated folder segment.
/// Lives in this assembly (not <c>CielWin.Interop</c>) because the HTTP protocol there hands back a
/// validated lowercase name and leaves the mapping to the app.
/// </summary>
public enum WallpaperScene
{
    Processing,
    Explorer,
    Idle,
    Raphael,
}

/// <summary>The preferences CielWin keeps between runs.</summary>
/// <param name="HttpServerEnabled">
/// The ONE switch for the local HTTP server (<c>http-server</c>): off closes the port, which disables
/// scene switching and alerts together. On by default; the server is loopback-only and gated by a
/// bearer token.
/// </param>
/// <param name="HttpServerPort">The loopback TCP port (<c>http-server-port</c>), 1-65535.</param>
/// <param name="WallpaperMode"><c>wallpaper-mode</c>: <c>scene</c> or <c>scene-mini</c>.</param>
/// <param name="WallpaperScene">The current scene, persisted so it survives restarts (<c>scene</c>).</param>
/// <param name="MiniPosition">Where the mini window sits (<c>mini-position</c>).</param>
/// <param name="AlertSoundsEnabled">
/// Whether a newly shown alert plays its sound (<c>alert-sounds</c>). On by default, so a file written
/// before the key existed keeps sounds on.
/// </param>
/// <param name="FailedSound">
/// The imported sound a failed alert plays (<c>failed-sound</c>): a bare file name in
/// <see cref="AlertSoundLibrary"/>'s folder, or <see langword="null"/> (the default) for silence.
/// </param>
/// <param name="WarningSound">The same for a warning alert (<c>warning-sound</c>).</param>
/// <param name="AlertHoldMaxSeconds">
/// How long a held warning (<c>duration: 0</c>) may stay up without being cleared, counted from its
/// request (<c>alert-hold-max-seconds</c>), 10-3600, default 600. Hand-edited; read at start.
/// </param>
/// <remarks>
/// A flat <c>key = value</c> text file a person is expected to edit. Blank lines and <c>#</c> comments
/// are ignored, unknown keys (including every key CosmicWin had that CielWin dropped) are skipped,
/// and an unreadable value keeps the default, so a typo costs one setting instead of blocking
/// startup. The LAST assignment of a key wins. Legacy spellings read from CosmicWin files:
/// <c>wallpaper-mode = html|html-mini|mini</c>, <c>wallpaper-scene</c>, <c>mini-corner</c>,
/// <c>alert-http</c>, <c>alert-http-port</c>; when a file has both a legacy key and its replacement
/// the new key wins whatever the line order. Only the new names are ever written.
/// </remarks>
public sealed record Settings(
    bool HttpServerEnabled = true,
    int HttpServerPort = AlertHttpProtocol.DefaultPort,
    WallpaperMode WallpaperMode = WallpaperMode.Scene,
    WallpaperScene WallpaperScene = WallpaperScene.Processing,
    MiniPosition MiniPosition = MiniPosition.TopRight,
    bool AlertSoundsEnabled = true,
    string? FailedSound = null,
    string? WarningSound = null,
    int AlertHoldMaxSeconds = 600)
{
    public static Settings Default { get; } = new();

    private const string WallpaperModeKey = "wallpaper-mode";
    private const string HttpServerKey = "http-server";
    private const string LegacyHttpServerKey = "alert-http";
    private const string HttpServerPortKey = "http-server-port";
    private const string LegacyHttpServerPortKey = "alert-http-port";
    private const string SceneKey = "scene";
    private const string LegacySceneKey = "wallpaper-scene";
    private const string MiniPositionKey = "mini-position";
    private const string LegacyMiniPositionKey = "mini-corner";
    private const string AlertSoundsKey = "alert-sounds";
    private const string FailedSoundKey = "failed-sound";
    private const string WarningSoundKey = "warning-sound";
    private const string AlertHoldMaxSecondsKey = "alert-hold-max-seconds";

    /// <summary>Whether either kind has an imported sound (the tray hides the mute toggle otherwise).</summary>
    public bool HasAnySound => FailedSound is not null || WarningSound is not null;

    /// <summary>The imported sound's file name for <paramref name="kind"/>, or <see langword="null"/>.</summary>
    public string? SoundFor(AlertKind kind) => kind == AlertKind.Failed ? FailedSound : WarningSound;

    /// <summary>These settings with <paramref name="kind"/>'s sound set to <paramref name="fileName"/> (null clears it).</summary>
    public Settings WithSound(AlertKind kind, string? fileName) =>
        kind == AlertKind.Failed ? this with { FailedSound = fileName } : this with { WarningSound = fileName };

    private static readonly (string Name, WallpaperMode Value)[] ModeNames =
    [
        ("scene", WallpaperMode.Scene),
        ("scene-mini", WallpaperMode.SceneMini),
    ];

    // Spellings CosmicWin wrote; read, never written.
    private static readonly (string Name, WallpaperMode Value)[] LegacyModeNames =
    [
        ("html", WallpaperMode.Scene),
        ("html-mini", WallpaperMode.SceneMini),
        ("mini", WallpaperMode.SceneMini),
    ];

    private static readonly (string Name, WallpaperScene Value)[] SceneNames =
    [
        ("processing", WallpaperScene.Processing),
        ("explorer", WallpaperScene.Explorer),
        ("idle", WallpaperScene.Idle),
        ("raphael", WallpaperScene.Raphael),
    ];

    private static readonly (string Name, MiniPosition Value)[] MiniPositionNames =
    [
        ("top-left", MiniPosition.TopLeft),
        ("top-center", MiniPosition.TopCenter),
        ("top-right", MiniPosition.TopRight),
        ("right-center", MiniPosition.RightCenter),
        ("bottom-right", MiniPosition.BottomRight),
        ("bottom-center", MiniPosition.BottomCenter),
        ("bottom-left", MiniPosition.BottomLeft),
        ("left-center", MiniPosition.LeftCenter),
    ];

    /// <summary>
    /// Reads <paramref name="content"/> into settings, keeping the default for anything it does not
    /// state and anything it states unreadably.
    /// </summary>
    public static Settings Parse(string content)
    {
        var wallpaperMode = Default.WallpaperMode;
        WallpaperScene? scene = null, legacyScene = null;
        bool? httpServer = null, legacyHttpServer = null;
        int? port = null, legacyPort = null;
        MiniPosition? miniPosition = null, legacyMiniPosition = null;
        var alertSounds = Default.AlertSoundsEnabled;
        string? failedSound = null, warningSound = null;
        var alertHoldMaxSeconds = Default.AlertHoldMaxSeconds;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim().ToLowerInvariant();
            var value = line[(separatorIndex + 1)..].Trim();

            // Only a value we RECOGNISE moves a setting: guessing what a typo meant is worse than
            // leaving the default alone.
            switch (key)
            {
                case WallpaperModeKey:
                    if (TryReadName(value, ModeNames, out var mode) || TryReadName(value, LegacyModeNames, out mode))
                    {
                        wallpaperMode = mode;
                    }

                    break;
                case HttpServerKey:
                    httpServer = TryReadFlag(value) ?? httpServer;
                    break;
                case LegacyHttpServerKey:
                    legacyHttpServer = TryReadFlag(value) ?? legacyHttpServer;
                    break;
                case HttpServerPortKey:
                    port = TryReadPort(value) ?? port;
                    break;
                case LegacyHttpServerPortKey:
                    legacyPort = TryReadPort(value) ?? legacyPort;
                    break;
                case SceneKey:
                    if (TryReadName(value, SceneNames, out var sceneValue))
                    {
                        scene = sceneValue;
                    }

                    break;
                case LegacySceneKey:
                    if (TryReadName(value, SceneNames, out var legacySceneValue))
                    {
                        legacyScene = legacySceneValue;
                    }

                    break;
                case MiniPositionKey:
                    if (TryReadName(value, MiniPositionNames, out var positionValue))
                    {
                        miniPosition = positionValue;
                    }

                    break;
                case LegacyMiniPositionKey:
                    if (TryReadName(value, MiniPositionNames, out var legacyPositionValue))
                    {
                        legacyMiniPosition = legacyPositionValue;
                    }

                    break;
                case AlertSoundsKey:
                    alertSounds = TryReadFlag(value) ?? alertSounds;
                    break;
                case FailedSoundKey:
                    failedSound = TryReadSound(value);
                    break;
                case WarningSoundKey:
                    warningSound = TryReadSound(value);
                    break;
                case AlertHoldMaxSecondsKey:
                    alertHoldMaxSeconds = TryReadHoldMaxSeconds(value) ?? alertHoldMaxSeconds;
                    break;
            }
        }

        return new Settings(
            httpServer ?? legacyHttpServer ?? Default.HttpServerEnabled,
            port ?? legacyPort ?? Default.HttpServerPort,
            wallpaperMode,
            scene ?? legacyScene ?? Default.WallpaperScene,
            miniPosition ?? legacyMiniPosition ?? Default.MiniPosition,
            alertSounds,
            failedSound,
            warningSound,
            alertHoldMaxSeconds);
    }

    /// <summary>The file this instance would be written as, comment and all.</summary>
    public string Serialize() =>
        $"""
         # CielWin settings. Edited by hand or by the tray menu.
         # {WallpaperModeKey}: `scene` (default) shows the animated scene as the desktop wallpaper;
         # `scene-mini` leaves the wallpaper alone and shows a small always-on-top scene window
         # ({MiniPositionKey}).
         {WallpaperModeKey} = {NameOf(ModeNames, WallpaperMode)}

         # {HttpServerKey}: on (default) runs the local HTTP server for scene switching and alerts;
         # off closes the port and disables both. Loopback-only (127.0.0.1 / localhost, never
         # reachable over the network); its bearer token lives in
         # %LOCALAPPDATA%\CielWin\http.token, created the first time the server starts.
         {HttpServerKey} = {(HttpServerEnabled ? "on" : "off")}

         # {HttpServerPortKey}: the loopback TCP port the HTTP server listens on, 1-65535.
         {HttpServerPortKey} = {HttpServerPort.ToString(CultureInfo.InvariantCulture)}

         # {SceneKey}: the current scene: `processing` (default), `explorer`, `idle` or `raphael`.
         # Updated whenever the scene is switched, so it survives restarts.
         {SceneKey} = {NameOf(SceneNames, WallpaperScene)}

         # {MiniPositionKey}: where the `scene-mini` window sits in the work area: a corner
         # (`top-left`, `top-right` (default), `bottom-left`, `bottom-right`) or a side midpoint
         # (`top-center`, `right-center`, `bottom-center`, `left-center`).
         {MiniPositionKey} = {NameOf(MiniPositionNames, MiniPosition)}

         # {AlertSoundsKey}: on (default) plays a sound when an alert appears (the failed sound when
         # it has any failed tile, otherwise the warning sound); off keeps alerts silent. Also
         # toggled from the tray menu.
         {AlertSoundsKey} = {(AlertSoundsEnabled ? "on" : "off")}

         # {FailedSoundKey} / {WarningSoundKey}: the sound each alert kind plays, imported from the tray
         # menu (a .wav, .mp3 or .m4a copied into %LOCALAPPDATA%\CielWin\sounds\). Empty (default):
         # that kind is silent. No sound ships with CielWin.
         {FailedSoundKey} = {FailedSound}
         {WarningSoundKey} = {WarningSound}

         # {AlertHoldMaxSecondsKey}: how long a held warning (`duration: 0`) may stay up without being
         # cleared, counted from its request, 10-3600 (default 600). Read at start.
         {AlertHoldMaxSecondsKey} = {AlertHoldMaxSeconds.ToString(CultureInfo.InvariantCulture)}

         """;

    private static string NameOf<T>((string Name, T Value)[] names, T value) =>
        names.First(entry => EqualityComparer<T>.Default.Equals(entry.Value, value)).Name;

    private static bool TryReadName<T>(string text, (string Name, T Value)[] names, out T value)
    {
        foreach (var (name, candidate) in names)
        {
            if (text.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Accepts the spellings a person actually types: <c>on</c>/<c>true</c>/<c>1</c> and
    /// <c>off</c>/<c>false</c>/<c>0</c>. Anything else is <see langword="null"/>.
    /// </summary>
    private static bool? TryReadFlag(string value) => value.ToLowerInvariant() switch
    {
        "on" or "true" or "1" => true,
        "off" or "false" or "0" => false,
        _ => null,
    };

    /// <summary>
    /// A bare sound file name (see <see cref="AlertSoundLibrary.IsSoundFileName"/>); anything else,
    /// including an empty value, is no sound. Unlike the other keys an unusable value clears the
    /// sound rather than keeping an earlier one: silence is the safe reading.
    /// </summary>
    private static string? TryReadSound(string value) => AlertSoundLibrary.IsSoundFileName(value) ? value : null;

    /// <summary>Reads a hold max in seconds, 10-3600; anything else (or not a whole number) is <see langword="null"/>.</summary>
    private static int? TryReadHoldMaxSeconds(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 10 and <= 3600
            ? seconds
            : null;

    /// <summary>Reads a TCP port, 1-65535; anything else (or not a whole number) is <see langword="null"/>.</summary>
    private static int? TryReadPort(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? port
            : null;
}
