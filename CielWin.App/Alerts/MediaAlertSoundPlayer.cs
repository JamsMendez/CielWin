using System.IO;
using System.Windows.Media;

namespace CielWin.App.Alerts;

/// <summary>
/// Plays each kind's imported sound (<see cref="AlertSoundLibrary"/>) through WPF
/// <see cref="MediaPlayer"/>, which reads <c>.wav</c>, <c>.mp3</c> and <c>.m4a</c>. A kind with no
/// sound is silent: there is no fallback to the other kind.
/// </summary>
/// <remarks>
/// The file is looked up on every play, so an import or removal applies to the next alert. Each kind
/// keeps one output for the life of the process (a collected <see cref="MediaPlayer"/> would stop
/// mid-sound). Nothing escapes: a missing file is traced by reason code, any failure by exception
/// TYPE only (a message can hold a path), and the alert goes on silently.
/// </remarks>
internal sealed class MediaAlertSoundPlayer(
    Func<AlertKind, string?> resolvePath, Func<string, bool> fileExists, Func<IAlertSoundOutput> createOutput,
    Action<string> trace) : IAlertSoundPlayer
{
    private readonly Dictionary<AlertKind, IAlertSoundOutput> _outputs = [];

    /// <summary>The real player: <see cref="MediaPlayer"/> outputs on the calling (UI) thread.</summary>
    public static MediaAlertSoundPlayer CreateProduction(Func<AlertKind, string?> resolvePath, Action<string> trace) =>
        new(resolvePath, File.Exists, () => new MediaPlayerOutput(), trace);

    public void Play(AlertKind kind)
    {
        var name = AlertSoundLibrary.KindName(kind);
        try
        {
            if (resolvePath(kind) is not { } path)
            {
                return;
            }

            if (!fileExists(path))
            {
                Trace($"alert sound-skipped kind={name} reason=missing-file");
                return;
            }

            OutputFor(kind).Play(path);
        }
        catch (Exception error)
        {
            Trace($"alert sound-failed kind={name} error={error.GetType().Name}");
        }
    }

    private IAlertSoundOutput OutputFor(AlertKind kind)
    {
        if (_outputs.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var output = createOutput();
        var name = AlertSoundLibrary.KindName(kind);
        output.Failed += errorType => Trace($"alert sound-failed kind={name} reason=media-failed error={errorType}");
        _outputs[kind] = output;
        return output;
    }

    private void Trace(string line)
    {
        try
        {
            trace(line);
        }
        catch
        {
        }
    }

    /// <summary>
    /// <see cref="MediaPlayer"/> at full volume (its default is half). The file is closed once the
    /// sound ends or fails, so a later import can replace it.
    /// </summary>
    private sealed class MediaPlayerOutput : IAlertSoundOutput
    {
        private readonly MediaPlayer _player = new() { Volume = 1.0 };

        public MediaPlayerOutput()
        {
            _player.MediaEnded += (_, _) => _player.Close();
            _player.MediaFailed += (_, args) =>
            {
                _player.Close();
                Failed?.Invoke(args.ErrorException?.GetType().Name ?? "Unknown");
            };
        }

        public event Action<string>? Failed;

        public void Play(string path)
        {
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
    }
}

/// <summary>Where <see cref="MediaAlertSoundPlayer"/> sends a sound; a seam so its decisions are tested without audio.</summary>
internal interface IAlertSoundOutput
{
    /// <summary>Raised later, when the file turns out unplayable, with the error's TYPE NAME only.</summary>
    event Action<string>? Failed;

    /// <summary>Starts playing the file at <paramref name="path"/> and returns at once.</summary>
    void Play(string path);
}
