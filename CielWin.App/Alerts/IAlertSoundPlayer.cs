namespace CielWin.App.Alerts;

/// <summary>
/// Plays the sound of an alert kind. Called on the UI thread, once per newly displayed alert; must
/// return at once (playback runs in the background). No sound ships with CielWin: a kind with no
/// sound plays nothing.
/// </summary>
public interface IAlertSoundPlayer
{
    void Play(AlertKind kind);
}
