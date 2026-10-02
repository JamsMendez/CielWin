using System.IO;

namespace CielWin.App;

/// <summary>
/// Owns the on-disk settings read and write: the pure format lives in <see cref="Settings"/>.
/// </summary>
/// <remarks>
/// Every failure here degrades to the defaults rather than throwing. A settings file is a
/// convenience; an app that refuses to start because one could not be read has turned a convenience
/// into a liability.
/// </remarks>
public static class SettingsFile
{
    /// <summary>
    /// <c>%LOCALAPPDATA%\CielWin\settings.conf</c>, beside the HTTP token file: one directory for
    /// everything CielWin writes.
    /// </summary>
    public static string ResolvePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CielWin",
            "settings.conf");

    /// <summary>Reads the settings at <see cref="ResolvePath"/>.</summary>
    public static Settings Load() => Load(ResolvePath());

    /// <summary>
    /// Reads the settings at <paramref name="path"/>. A missing or unreadable file yields
    /// <see cref="Settings.Default"/> -- first run happens before the file exists, and startup must
    /// not depend on it.
    /// </summary>
    public static Settings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Settings.Parse(File.ReadAllText(path)) : Settings.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Settings.Default;
        }
    }

    /// <summary>Reads, or first writes and then reads, the settings at <see cref="ResolvePath"/>.</summary>
    public static Settings LoadOrCreate(Action<string>? onDiagnostic = null) =>
        LoadOrCreate(ResolvePath(), onDiagnostic);

    /// <summary>
    /// When <paramref name="path"/> does not exist yet, WRITES <see cref="Settings.Default"/> there
    /// (the same commented template a later save produces) before returning it, so a fresh machine
    /// ends up with a real settings.conf to hand-edit. When it already exists this is exactly
    /// <see cref="Load(string)"/>: the file is read, never rewritten, not even to normalise it.
    /// </summary>
    /// <remarks>
    /// The write goes through <see cref="Save(string, Settings, Action{string}?)"/>, so a first run
    /// that cannot create the file keeps running on the in-memory defaults instead of crashing.
    /// </remarks>
    /// <param name="path">Where to read from, or write the defaults to when missing.</param>
    /// <param name="onDiagnostic">See <see cref="Save(string, Settings, Action{string}?)"/>.</param>
    public static Settings LoadOrCreate(string path, Action<string>? onDiagnostic = null)
    {
        if (File.Exists(path))
        {
            return Load(path);
        }

        Save(path, Settings.Default, onDiagnostic);
        return Settings.Default;
    }

    /// <summary>Writes <paramref name="settings"/> to <see cref="ResolvePath"/>.</summary>
    public static void Save(Settings settings, Action<string>? onDiagnostic = null) =>
        Save(ResolvePath(), settings, onDiagnostic);

    /// <summary>
    /// Writes <paramref name="settings"/> to <paramref name="path"/>, creating the directory if it
    /// is not there yet.
    /// </summary>
    /// <param name="path">Where to write.</param>
    /// <param name="settings">What to write.</param>
    /// <param name="onDiagnostic">
    /// Invoked with the failed write's exception TYPE NAME ONLY (never the path or message) when the
    /// write fails. Optional: without it a failure is silent.
    /// </param>
    /// <remarks>
    /// Returns quietly on an IO failure rather than throwing. This runs from a tray menu click or a
    /// hotkey: the change the user just made has ALREADY taken effect on screen, and taking the
    /// process down because the preference could not be recorded would be disproportionate.
    /// </remarks>
    public static void Save(string path, Settings settings, Action<string>? onDiagnostic = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, settings.Serialize());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The diagnostic is best-effort: in production it writes a trace that usually lives in
            // the SAME folder that just refused this write. Every exception type is swallowed on
            // purpose, so a lost diagnostic line never costs a startup or a toggle.
            try
            {
                onDiagnostic?.Invoke(exception.GetType().Name);
            }
            catch (Exception)
            {
            }
        }
    }
}
