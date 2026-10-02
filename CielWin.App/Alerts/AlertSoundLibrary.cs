using System.IO;

namespace CielWin.App.Alerts;

/// <summary>
/// The folder of imported alert sounds: one file per kind, named for it (<c>failed.m4a</c>,
/// <c>warning.wav</c>). No sound ships with CielWin; a kind is silent until the user imports one.
/// </summary>
/// <remarks>
/// Importing COPIES the chosen file, so moving or deleting the original later does not break the
/// sound. The settings file keeps only the copy's bare file name (<see cref="IsSoundFileName"/>), so
/// a hand-edited value can never point outside this folder. IO failures are thrown to the caller,
/// which traces them by type.
/// </remarks>
public sealed class AlertSoundLibrary(string directory)
{
    /// <summary>The formats WPF <c>MediaPlayer</c> plays that the import accepts, in dialog order.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".wav", ".mp3", ".m4a"];

    /// <summary>The import dialog's filter: exactly <see cref="Extensions"/>.</summary>
    public static string FileDialogFilter { get; } =
        $"Sound files (*.wav, *.mp3, *.m4a)|{string.Join(';', Extensions.Select(extension => $"*{extension}"))}";

    public string Directory { get; } = directory;

    /// <summary><c>%LOCALAPPDATA%\CielWin\sounds</c>, beside settings.conf.</summary>
    public static string ResolveDefaultDirectory() =>
        Path.Combine(Path.GetDirectoryName(SettingsFile.ResolvePath())!, "sounds");

    /// <summary>The lowercase name a kind goes by in file names and trace lines.</summary>
    public static string KindName(AlertKind kind) => kind == AlertKind.Failed ? "failed" : "warning";

    /// <summary>Whether <paramref name="path"/> ends in one of <see cref="Extensions"/>, any case.</summary>
    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A bare file name (no folder, drive or <c>..</c>) of a supported format: the only shape a sound
    /// setting may take.
    /// </summary>
    public static bool IsSoundFileName(string name) =>
        name.Length > 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && Path.GetFileName(name) == name
        && Path.GetFileNameWithoutExtension(name).Length > 0
        && IsSupported(name);

    public string PathOf(string fileName) => Path.Combine(Directory, fileName);

    /// <summary>
    /// Copies <paramref name="sourcePath"/> in as <paramref name="kind"/>'s sound, replacing the
    /// previous one whatever its extension, and returns the copy's file name.
    /// </summary>
    /// <exception cref="NotSupportedException">The file is not a <c>.wav</c>, <c>.mp3</c> or <c>.m4a</c>.</exception>
    public string Import(AlertKind kind, string sourcePath)
    {
        if (!IsSupported(sourcePath))
        {
            throw new NotSupportedException("Only .wav, .mp3 and .m4a sounds can be imported.");
        }

        var name = KindName(kind) + Path.GetExtension(sourcePath).ToLowerInvariant();
        var target = PathOf(name);
        System.IO.Directory.CreateDirectory(Directory);

        // Picking the already-imported copy itself keeps it: copying a file onto itself fails.
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            // Copied beside the target and moved over it, so a failed copy leaves the previous sound whole.
            var temp = $"{target}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(sourcePath, temp);
                File.Move(temp, target, overwrite: true);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        // The new sound is already in place, so a stale file of another extension that cannot be deleted (a player
        // still holding it) must not fail the import: it is left behind and cleared by the next import or remove.
        foreach (var extension in Extensions)
        {
            var other = PathOf(KindName(kind) + extension);
            if (!string.Equals(other, target, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(other);
            }
        }

        return name;
    }

    /// <summary>Deletes <paramref name="kind"/>'s imported sound, whatever its extension. Nothing there is fine.</summary>
    public void Remove(AlertKind kind)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var extension in Extensions)
        {
            File.Delete(PathOf(KindName(kind) + extension));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
        {
            // Best effort: a stray temp or stale file is harmless, and the caller's outcome is already decided.
        }
    }
}
