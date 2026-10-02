using CielWin.App.Composition;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// <see cref="StartupSettings.Load"/>: the production settings read. First run writes the commented
/// defaults; an existing file is only read; an unreadable one is reported and never written.
/// </summary>
public sealed class StartupSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CielWinStartupSettingsTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _trace = [];

    private string SettingsPath => Path.Combine(_directory, "settings.conf");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MissingFile_WritesTheDefaults_AndCanSave()
    {
        var result = StartupSettings.Load(SettingsPath, _trace.Add);

        Assert.Equal(SettingsLoadStatus.Missing, result.Status);
        Assert.True(result.CanSave);
        Assert.Equal(Settings.Default, Settings.Parse(File.ReadAllText(SettingsPath)));
    }

    [Fact]
    public void ExistingFile_IsReadAndNeverRewritten()
    {
        Directory.CreateDirectory(_directory);
        const string text = "# mine\nscene = idle\n";
        File.WriteAllText(SettingsPath, text);

        var result = StartupSettings.Load(SettingsPath, _trace.Add);

        Assert.Equal(WallpaperScene.Idle, result.Settings.WallpaperScene);
        Assert.Equal(text, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void LockedFile_IsUnreadable_TracedByTypeOnly_AndLeftUntouched()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "scene = raphael\n");

        SettingsLoadResult result;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = StartupSettings.Load(SettingsPath, _trace.Add);
        }

        Assert.False(result.CanSave);
        Assert.Equal("scene = raphael\n", File.ReadAllText(SettingsPath));
        Assert.Contains(_trace, line => line.Contains("settings-file read-failed error=IOException", StringComparison.Ordinal));
        Assert.DoesNotContain(_trace, line => line.Contains(_directory, StringComparison.OrdinalIgnoreCase));
    }
}
