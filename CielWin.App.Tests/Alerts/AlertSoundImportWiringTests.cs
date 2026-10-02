using System.IO;
using CielWin.App.Alerts;
using CielWin.App.Tests.Composition;
using CielWin.App.Tray;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// Importing and removing alert sounds from the tray, through the whole composition: the file is
/// copied into the sounds folder, the setting is persisted, the next alert plays it, and the menu
/// shows the remove entries and the mute toggle only when they apply. The dialog is the harness's
/// fake; the folders are temporary.
/// </summary>
public sealed class AlertSoundImportWiringTests : IDisposable
{
    private readonly CompositionHarness _harness = new();
    private readonly string _sourceDirectory = Path.Combine(Path.GetTempPath(), $"cielwin-source-{Guid.NewGuid():N}");

    public void Dispose()
    {
        foreach (var directory in new[] { _harness.SoundsDirectory, _sourceDirectory })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private string Source(string name)
    {
        Directory.CreateDirectory(_sourceDirectory);
        var path = Path.Combine(_sourceDirectory, name);
        File.WriteAllText(path, "sound bytes");
        return path;
    }

    private TrayMenuController Tray => _harness.Tray!.Controller;

    [Fact]
    public void WithNoSoundImported_AnAlertIsSilent_AndTheMenuHidesTheToggleAndTheRemoveEntries()
    {
        using var composition = _harness.Wire(new Settings());

        _harness.Server.Options.HandleAlert("failed:1");

        Assert.Equal([null], _harness.Sounds.PlayedFiles);
        Assert.False(Tray.IsVisible(TrayMenuEntry.AlertSounds));
        Assert.False(Tray.IsVisible(TrayMenuEntry.RemoveFailedSound));
        Assert.False(Tray.IsVisible(TrayMenuEntry.RemoveWarningSound));
    }

    [Fact]
    public void ImportingFromTheTray_CopiesPersistsAndTheNextAlertPlaysIt()
    {
        using var composition = _harness.Wire(new Settings(WallpaperScene: WallpaperScene.Idle));
        _harness.PickedSoundFile = Source("my alarm.M4A");

        Tray.ImportAlertSound(AlertKind.Failed);

        Assert.Equal([AlertKind.Failed], _harness.SoundPicks);
        var saved = Assert.Single(_harness.Saves);
        Assert.Equal(new Settings(WallpaperScene: WallpaperScene.Idle, FailedSound: "failed.m4a"), saved);
        Assert.True(File.Exists(Path.Combine(_harness.SoundsDirectory, "failed.m4a")));
        Assert.Contains("alert-sound imported kind=failed", _harness.Trace);

        _harness.Server.Options.HandleAlert("failed:1");

        Assert.Equal([Path.Combine(_harness.SoundsDirectory, "failed.m4a")], _harness.Sounds.PlayedFiles);
    }

    [Fact]
    public void AfterImportingOnlyTheFailedSound_TheMenuShowsRemoveFailedAndTheToggle()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.PickedSoundFile = Source("a.wav");

        Tray.ImportAlertSound(AlertKind.Failed);

        Assert.True(Tray.IsVisible(TrayMenuEntry.RemoveFailedSound));
        Assert.False(Tray.IsVisible(TrayMenuEntry.RemoveWarningSound));
        Assert.True(Tray.IsVisible(TrayMenuEntry.AlertSounds));
    }

    [Fact]
    public void AWarningAlert_DoesNotBorrowTheFailedSound()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.PickedSoundFile = Source("a.wav");
        Tray.ImportAlertSound(AlertKind.Failed);

        _harness.Server.Options.HandleAlert("warning:1");

        Assert.Equal([null], _harness.Sounds.PlayedFiles);
    }

    [Fact]
    public void RemovingFromTheTray_DeletesTheFile_ClearsTheSetting_AndHidesTheEntries()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.PickedSoundFile = Source("a.mp3");
        Tray.ImportAlertSound(AlertKind.Warning);

        Tray.RemoveAlertSound(AlertKind.Warning);

        Assert.Null(_harness.Saves[^1].WarningSound);
        Assert.Empty(Directory.GetFiles(_harness.SoundsDirectory));
        Assert.False(Tray.IsVisible(TrayMenuEntry.RemoveWarningSound));
        Assert.False(Tray.IsVisible(TrayMenuEntry.AlertSounds));
        Assert.Contains("alert-sound removed kind=warning", _harness.Trace);
    }

    [Fact]
    public void ACancelledDialog_ChangesNothing()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.PickedSoundFile = null;

        Tray.ImportAlertSound(AlertKind.Failed);

        Assert.Empty(_harness.Saves);
        Assert.False(Directory.Exists(_harness.SoundsDirectory));
    }

    [Fact]
    public void AnUnsupportedFile_IsRejectedAndTraced_WithoutAPath()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.PickedSoundFile = Source("song.ogg");

        Tray.ImportAlertSound(AlertKind.Warning);

        Assert.Empty(_harness.Saves);
        Assert.Contains("alert-sound import-rejected kind=warning reason=unsupported-format", _harness.Trace);
        Assert.DoesNotContain(_harness.Trace, line => line.Contains(_sourceDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFailedCopy_IsTracedByTypeName_AndKeepsTheSetting()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.PickedSoundFile = Path.Combine(_sourceDirectory, "gone.wav");

        Tray.ImportAlertSound(AlertKind.Failed);

        Assert.Empty(_harness.Saves);
        Assert.Contains(_harness.Trace, line => line.StartsWith("alert-sound import-failed kind=failed error=", StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Trace, line => line.Contains("gone.wav", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AThrowingDialog_IsTraced_AndChangesNothing()
    {
        using var composition = _harness.Wire(new Settings());
        _harness.SoundPickThrows = true;

        Tray.ImportAlertSound(AlertKind.Failed);

        Assert.Empty(_harness.Saves);
        Assert.Contains("alert-sound pick-failed kind=failed error=InvalidOperationException", _harness.Trace);
    }

    [Fact]
    public void ASettingNamingAMissingFile_StartsNormally_AndTheMenuStillOffersRemove()
    {
        using var composition = _harness.Wire(new Settings(WarningSound: "warning.wav"));

        Assert.True(Tray.IsVisible(TrayMenuEntry.RemoveWarningSound));

        _harness.Server.Options.HandleAlert("warning:1");

        Assert.Equal([Path.Combine(_harness.SoundsDirectory, "warning.wav")], _harness.Sounds.PlayedFiles);
    }
}
