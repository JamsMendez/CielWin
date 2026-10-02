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
    /// not depend on it. Callers that later SAVE must use <see cref="TryLoad"/> instead, because this
    /// overload cannot tell an unreadable file from a missing one.
    /// </summary>
    public static Settings Load(string path) => TryLoad(path).Settings;

    /// <summary>
    /// Reads the settings at <paramref name="path"/> and says HOW it went, never throwing. An
    /// existing file that cannot be read (locked by a scanner or editor, transient IO error) yields
    /// <see cref="SettingsLoadStatus.Unreadable"/>: the defaults are only a stand-in, and saving them
    /// would overwrite the user's real file.
    /// </summary>
    /// <param name="onDiagnostic">
    /// Invoked with the failed read's exception TYPE NAME ONLY (never the path or message).
    /// </param>
    public static SettingsLoadResult TryLoad(string path, Action<string>? onDiagnostic = null)
    {
        try
        {
            return new SettingsLoadResult(Settings.Parse(File.ReadAllText(path)), SettingsLoadStatus.Loaded);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SettingsLoadResult(Settings.Default, SettingsLoadStatus.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report(onDiagnostic, exception);
            return new SettingsLoadResult(Settings.Default, SettingsLoadStatus.Unreadable);
        }
    }

    /// <summary>Reads, or first writes and then reads, the settings at <see cref="ResolvePath"/>.</summary>
    public static Settings LoadOrCreate(Action<string>? onDiagnostic = null) =>
        LoadOrCreate(ResolvePath(), onDiagnostic);

    /// <summary>
    /// When <paramref name="path"/> does not exist yet, WRITES <see cref="Settings.Default"/> there
    /// (the same commented template a later save produces) before returning it, so a fresh machine
    /// ends up with a real settings.conf to hand-edit. When it already exists this is exactly
    /// <see cref="TryLoad"/>: the file is read, never rewritten, not even to normalise it, and a
    /// read failure is reported through <paramref name="onDiagnostic"/>.
    /// </summary>
    /// <remarks>
    /// The write goes through <see cref="Save(string, Settings, Action{string}?)"/>, so a first run
    /// that cannot create the file keeps running on the in-memory defaults instead of crashing.
    /// </remarks>
    /// <param name="path">Where to read from, or write the defaults to when missing.</param>
    /// <param name="onDiagnostic">See <see cref="Save(string, Settings, Action{string}?)"/>.</param>
    public static Settings LoadOrCreate(string path, Action<string>? onDiagnostic = null)
    {
        var result = TryLoad(path, onDiagnostic);
        if (result.Status == SettingsLoadStatus.Missing)
        {
            Save(path, Settings.Default, onDiagnostic);
        }

        return result.Settings;
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

            // Write beside the target and move over it: a crash or power loss mid-write must leave
            // the previous file intact, not a truncated one the next start would parse as defaults.
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, settings.Serialize());
                if (File.Exists(path))
                {
                    File.Replace(temp, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // The original failure is the one worth reporting; a stray temp file is harmless.
                }

                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report(onDiagnostic, exception);
        }
    }

    /// <summary>
    /// The diagnostic is best-effort: in production it writes a trace that usually lives in the SAME
    /// folder that just refused the IO. Every exception type is swallowed on purpose, so a lost
    /// diagnostic line never costs a startup or a toggle.
    /// </summary>
    private static void Report(Action<string>? onDiagnostic, Exception exception)
    {
        try
        {
            onDiagnostic?.Invoke(exception.GetType().Name);
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>How reading the settings file went.</summary>
public enum SettingsLoadStatus
{
    /// <summary>The file was read and parsed.</summary>
    Loaded,

    /// <summary>There is no file yet (first run); the defaults are the real answer.</summary>
    Missing,

    /// <summary>A file exists but could not be read; the defaults are only a stand-in.</summary>
    Unreadable,
}

/// <summary>The settings plus whether they reflect the file on disk.</summary>
public readonly record struct SettingsLoadResult(Settings Settings, SettingsLoadStatus Status)
{
    /// <summary>
    /// <c>false</c> when saving could destroy the user's data: the file exists but was not read, so
    /// anything written would replace it with defaults. The composition root must then skip saves.
    /// </summary>
    public bool CanSave => Status != SettingsLoadStatus.Unreadable;
}
