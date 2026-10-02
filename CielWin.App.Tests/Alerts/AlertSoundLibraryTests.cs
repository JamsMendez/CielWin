using System.IO;
using CielWin.App.Alerts;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// <see cref="AlertSoundLibrary"/>: the imported sounds folder. Always a temporary directory, never
/// <see cref="AlertSoundLibrary.ResolveDefaultDirectory"/>: a test must not touch the user's sounds.
/// </summary>
public sealed class AlertSoundLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cielwin-sounds-{Guid.NewGuid():N}");

    private string SoundsDirectory => Path.Combine(_root, "sounds");

    private string SourceDirectory => Path.Combine(_root, "source");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Source(string name, string content)
    {
        Directory.CreateDirectory(SourceDirectory);
        var path = Path.Combine(SourceDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string[] Imported() =>
        Directory.Exists(SoundsDirectory)
            ? Directory.GetFiles(SoundsDirectory).Select(path => Path.GetFileName(path)!).Order().ToArray()
            : [];

    [Theory]
    [InlineData("beep.wav", AlertKind.Failed, "failed.wav")]
    [InlineData("beep.MP3", AlertKind.Warning, "warning.mp3")]
    [InlineData("my sound.M4a", AlertKind.Failed, "failed.m4a")]
    public void Import_CopiesTheFileIntoTheFolder_NamedForItsKind(string sourceName, AlertKind kind, string expected)
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        var source = Source(sourceName, "sound bytes");

        var name = library.Import(kind, source);

        Assert.Equal(expected, name);
        Assert.Equal("sound bytes", File.ReadAllText(Path.Combine(SoundsDirectory, expected)));
        Assert.True(File.Exists(source), "the original is copied, never moved");
    }

    [Fact]
    public void Import_ReplacesThatKindsPreviousSound_EvenWithAnotherExtension()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        library.Import(AlertKind.Failed, Source("old.wav", "old"));
        library.Import(AlertKind.Warning, Source("warn.wav", "warn"));

        var name = library.Import(AlertKind.Failed, Source("new.m4a", "new"));

        Assert.Equal("failed.m4a", name);
        Assert.Equal(["failed.m4a", "warning.wav"], Imported());
        Assert.Equal("new", File.ReadAllText(Path.Combine(SoundsDirectory, "failed.m4a")));
    }

    [Fact]
    public void Import_WhenThePreviousSoundOfAnotherExtensionIsLocked_StillCommitsTheNewSound()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        library.Import(AlertKind.Failed, Source("old.wav", "old"));

        string name;
        // A player still holding the previous file must not turn a finished copy into a failed import.
        using (File.Open(Path.Combine(SoundsDirectory, "failed.wav"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            name = library.Import(AlertKind.Failed, Source("new.m4a", "new"));
        }

        Assert.Equal("failed.m4a", name);
        Assert.Equal("new", File.ReadAllText(Path.Combine(SoundsDirectory, "failed.m4a")));
    }

    [Fact]
    public void Import_OfTheSameExtension_OverwritesTheContent()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        library.Import(AlertKind.Warning, Source("a.mp3", "first"));

        library.Import(AlertKind.Warning, Source("b.mp3", "second"));

        Assert.Equal(["warning.mp3"], Imported());
        Assert.Equal("second", File.ReadAllText(Path.Combine(SoundsDirectory, "warning.mp3")));
    }

    [Fact]
    public void Import_OfTheAlreadyImportedFileItself_KeepsIt()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        library.Import(AlertKind.Failed, Source("a.wav", "kept"));

        var name = library.Import(AlertKind.Failed, Path.Combine(SoundsDirectory, "failed.wav"));

        Assert.Equal("failed.wav", name);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(SoundsDirectory, "failed.wav")));
    }

    [Theory]
    [InlineData("song.ogg")]
    [InlineData("song.flac")]
    [InlineData("song")]
    [InlineData("song.wav.txt")]
    public void Import_RejectsAnyOtherExtension_AndLeavesTheFolderAlone(string sourceName)
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        library.Import(AlertKind.Failed, Source("kept.wav", "kept"));

        Assert.Throws<NotSupportedException>(() => library.Import(AlertKind.Failed, Source(sourceName, "x")));

        Assert.Equal(["failed.wav"], Imported());
    }

    [Fact]
    public void Remove_DeletesOnlyThatKindsSound()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);
        library.Import(AlertKind.Failed, Source("a.mp3", "a"));
        library.Import(AlertKind.Warning, Source("b.wav", "b"));

        library.Remove(AlertKind.Failed);

        Assert.Equal(["warning.wav"], Imported());
    }

    [Fact]
    public void Remove_WhenNothingWasImported_DoesNothing()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);

        library.Remove(AlertKind.Warning);

        Assert.Empty(Imported());
    }

    [Fact]
    public void PathOf_ResolvesAFileNameInsideTheFolder()
    {
        var library = new AlertSoundLibrary(SoundsDirectory);

        Assert.Equal(Path.Combine(SoundsDirectory, "failed.wav"), library.PathOf("failed.wav"));
    }

    [Theory]
    [InlineData("failed.wav", true)]
    [InlineData("WARNING.M4A", true)]
    [InlineData("custom.mp3", true)]
    [InlineData(".wav", false)]
    [InlineData("failed.ogg", false)]
    [InlineData(@"..\failed.wav", false)]
    [InlineData(@"C:\sounds\failed.wav", false)]
    [InlineData("C:failed.wav", false)]
    [InlineData("sub/failed.wav", false)]
    [InlineData("", false)]
    public void IsSoundFileName_AcceptsOnlyABareFileNameOfASupportedFormat(string name, bool expected)
    {
        Assert.Equal(expected, AlertSoundLibrary.IsSoundFileName(name));
    }

    [Fact]
    public void TheDefaultFolder_SitsBesideTheSettingsFile()
    {
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(SettingsFile.ResolvePath())!, "sounds"),
            AlertSoundLibrary.ResolveDefaultDirectory());
    }

    [Fact]
    public void TheFileDialogFilter_OffersExactlyTheSupportedFormats()
    {
        Assert.Equal(["*.wav", "*.mp3", "*.m4a"], AlertSoundLibrary.Extensions.Select(extension => $"*{extension}"));
        Assert.EndsWith("|*.wav;*.mp3;*.m4a", AlertSoundLibrary.FileDialogFilter, StringComparison.Ordinal);
    }
}
