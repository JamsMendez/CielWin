using CielWin.App.Composition;

namespace CielWin.App.Tests.Composition;

/// <summary>
/// <see cref="FileTrace"/>: the production trace sink. Best-effort by contract: it appends one
/// timestamped line per record, keeps the file bounded, and never throws into its caller.
/// </summary>
public sealed class FileTraceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CielWinFileTraceTests", Guid.NewGuid().ToString("N"));

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
    public void Record_AppendsOneTimestampedLinePerCall_CreatingTheDirectory()
    {
        var path = Path.Combine(_directory, "trace.log");
        var trace = new FileTrace(path, clock: () => new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero));

        trace.Record("first");
        trace.Record("second");

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("2026-10-01T08:30:00", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(" first", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(" second", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Record_PastTheSizeCap_RollsTheFileOverToOneBackup()
    {
        var path = Path.Combine(_directory, "trace.log");
        var trace = new FileTrace(path, maxBytes: 64);

        trace.Record(new string('a', 80));
        trace.Record("after");

        Assert.Contains(new string('a', 80), File.ReadAllText(path + ".1"));
        var current = File.ReadAllText(path);
        Assert.Contains("after", current);
        Assert.DoesNotContain(new string('a', 80), current);
    }

    [Fact]
    public void Record_WhenThePathCannotBeWritten_NeverThrows()
    {
        Directory.CreateDirectory(_directory);
        // The path IS a directory, so every append fails.
        var trace = new FileTrace(_directory);

        trace.Record("lost");
    }

    [Fact]
    public void ResolveDefaultPath_LivesBesideTheSettingsFile()
    {
        Assert.Equal(
            Path.GetDirectoryName(SettingsFile.ResolvePath()),
            Path.GetDirectoryName(FileTrace.ResolveDefaultPath()));
    }
}
