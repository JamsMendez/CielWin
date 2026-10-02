using System.IO;

namespace CielWin.App.Composition;

/// <summary>
/// The production trace sink: one timestamped line per record in
/// <c>%LOCALAPPDATA%\CielWin\trace.log</c>, rolled over to a single <c>.1</c> backup past a size cap.
/// </summary>
/// <remarks>
/// Best-effort by contract: callers trace from tray clicks, hotkeys, the HTTP thread and the
/// wallpaper thread, and a lost line must never cost any of them. Every IO failure is swallowed.
/// Thread-safe. Callers never pass paths, tokens or exception messages, only type names.
/// </remarks>
public sealed class FileTrace(string path, long maxBytes = FileTrace.DefaultMaxBytes, Func<DateTimeOffset>? clock = null)
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    /// <summary>Beside settings.conf: one directory for everything CielWin writes.</summary>
    public static string ResolveDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CielWin",
            "trace.log");

    public void Record(string line)
    {
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var file = new FileInfo(path);
                if (file.Exists && file.Length >= maxBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }

                File.AppendAllText(path, $"{_clock():O} {line}{Environment.NewLine}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
