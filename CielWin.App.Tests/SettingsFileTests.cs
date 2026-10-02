using System.IO;

namespace CielWin.App.Tests;

/// <summary>
/// The settings file on disk. Always against a temporary path, never <see cref="SettingsFile.ResolvePath"/>:
/// a test that wrote the real file would rewrite the user's own settings on every run.
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"cielwin-settings-{Guid.NewGuid():N}");

    private string Path_ => Path.Combine(_directory, "settings.conf");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AMissingFile_LoadsTheDefaults()
    {
        Assert.Equal(Settings.Default, SettingsFile.Load(Path_));
    }

    [Fact]
    public void SaveThenLoad_ReturnsWhatWasSaved()
    {
        var saved = new Settings(HttpServerEnabled: false, WallpaperMode: WallpaperMode.SceneMini);

        SettingsFile.Save(Path_, saved);

        Assert.Equal(saved, SettingsFile.Load(Path_));
    }

    [Fact]
    public void Save_CreatesTheDirectoryWhenItIsMissing()
    {
        Assert.False(Directory.Exists(_directory));

        SettingsFile.Save(Path_, Settings.Default);

        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void SavingTwice_LeavesOnlyTheSecondValue()
    {
        SettingsFile.Save(Path_, Settings.Default with { HttpServerEnabled = false });
        SettingsFile.Save(Path_, Settings.Default with { HttpServerEnabled = true });

        Assert.True(SettingsFile.Load(Path_).HttpServerEnabled);
    }

    [Fact]
    public void ADirectoryWhereTheFileShouldBe_LoadsTheDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path_);

        Assert.Equal(Settings.Default, SettingsFile.Load(Path_));
    }

    [Fact]
    public void TheDefaultPath_IsSettingsConfUnderLocalAppDataCielWin()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CielWin",
            "settings.conf");

        Assert.Equal(expected, SettingsFile.ResolvePath());
    }

    [Fact]
    public void MissingFile_LoadOrCreate_WritesTheFullCommentedDefaultTemplate()
    {
        var settings = SettingsFile.LoadOrCreate(Path_);

        Assert.Equal(Settings.Default, settings);
        Assert.Equal(Settings.Default.Serialize(), File.ReadAllText(Path_), StringComparer.Ordinal);
    }

    [Fact]
    public void ExistingFile_LoadOrCreate_IsNeverRewritten()
    {
        var original = "http-server = off\nwallpaper-mode = html-mini\n";
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, original);

        var settings = SettingsFile.LoadOrCreate(Path_);

        Assert.Equal(original, File.ReadAllText(Path_), StringComparer.Ordinal);
        Assert.False(settings.HttpServerEnabled);
        Assert.Equal(WallpaperMode.SceneMini, settings.WallpaperMode);
    }

    [Fact]
    public void AnUnwritablePath_LoadOrCreate_ReturnsTheDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path_);

        Assert.Equal(Settings.Default, SettingsFile.LoadOrCreate(Path_));
    }

    [Fact]
    public void AnUnwritablePath_LoadOrCreate_ReportsTheFailureByTypeNameOnly()
    {
        Directory.CreateDirectory(Path_);
        var diagnostics = new List<string>();

        var settings = SettingsFile.LoadOrCreate(Path_, error => diagnostics.Add(error));

        Assert.Equal(Settings.Default, settings);
        var diagnostic = Assert.Single(diagnostics);
        Assert.True(
            diagnostic is nameof(IOException) or nameof(UnauthorizedAccessException),
            $"expected an IOException or UnauthorizedAccessException type name, got '{diagnostic}'");
        Assert.DoesNotContain(_directory, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_ToAnUnwritablePath_ReportsTheFailureByTypeNameOnly()
    {
        Directory.CreateDirectory(Path_);
        var diagnostics = new List<string>();

        SettingsFile.Save(Path_, Settings.Default, error => diagnostics.Add(error));

        var diagnostic = Assert.Single(diagnostics);
        Assert.True(diagnostic is nameof(IOException) or nameof(UnauthorizedAccessException));
    }

    [Fact]
    public void AThrowingDiagnostic_IsContained_AndLoadOrCreateStillReturnsTheDefaults()
    {
        Directory.CreateDirectory(Path_);
        var diagnosticRan = false;
        Settings? settings = null;

        var exception = Record.Exception(() => settings = SettingsFile.LoadOrCreate(Path_, _ =>
        {
            diagnosticRan = true;
            throw new IOException("trace unwritable too");
        }));

        Assert.Null(exception);
        Assert.True(diagnosticRan);
        Assert.Equal(Settings.Default, settings);
    }

    [Fact]
    public void Save_Succeeding_NeverInvokesTheDiagnostic()
    {
        var diagnostics = new List<string>();

        SettingsFile.Save(Path_, Settings.Default, error => diagnostics.Add(error));

        Assert.Empty(diagnostics);
    }
}
