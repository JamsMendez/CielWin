using System.IO;
using CielWin.App.Alerts;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// <see cref="MediaAlertSoundPlayer"/>'s decisions, over a fake output: which file plays for which
/// kind, silence when a kind has no sound, and every failure traced without a path or message and
/// never thrown.
/// </summary>
public sealed class MediaAlertSoundPlayerTests
{
    private const string FailedPath = @"C:\Users\someone\AppData\Local\CielWin\sounds\failed.m4a";
    private const string WarningPath = @"C:\Users\someone\AppData\Local\CielWin\sounds\warning.wav";

    private readonly Dictionary<AlertKind, string?> _sounds = new()
    {
        [AlertKind.Failed] = FailedPath,
        [AlertKind.Warning] = WarningPath,
    };

    private readonly HashSet<string> _existing = [FailedPath, WarningPath];
    private readonly List<FakeOutput> _outputs = [];
    private readonly List<string> _trace = [];
    private bool _outputThrows;

    private MediaAlertSoundPlayer Create(Action<string>? trace = null) => new(
        kind => _sounds[kind],
        _existing.Contains,
        () =>
        {
            var output = new FakeOutput { Throws = _outputThrows };
            _outputs.Add(output);
            return output;
        },
        trace ?? _trace.Add);

    private IEnumerable<string> Played => _outputs.SelectMany(output => output.Played);

    [Theory]
    [InlineData(AlertKind.Failed, FailedPath)]
    [InlineData(AlertKind.Warning, WarningPath)]
    public void EachKind_PlaysItsOwnImportedFile(AlertKind kind, string expected)
    {
        Create().Play(kind);

        Assert.Equal([expected], Played);
        Assert.Empty(_trace);
    }

    [Fact]
    public void AKindWithNoSound_IsSilent_AndDoesNotBorrowTheOtherKindsSound()
    {
        _sounds[AlertKind.Warning] = null;

        Create().Play(AlertKind.Warning);

        Assert.Empty(Played);
        Assert.Empty(_trace);
    }

    [Fact]
    public void AMissingFile_IsTracedByReasonCode_AndSilent()
    {
        _existing.Remove(FailedPath);

        Create().Play(AlertKind.Failed);

        Assert.Empty(Played);
        Assert.Equal(["alert sound-skipped kind=failed reason=missing-file"], _trace);
    }

    [Fact]
    public void AnOutputThatThrows_IsTracedByTypeNameOnly_AndNeverEscapes()
    {
        _outputThrows = true;

        Create().Play(AlertKind.Warning);

        var line = Assert.Single(_trace);
        Assert.Equal("alert sound-failed kind=warning error=IOException", line);
        Assert.DoesNotContain("AppData", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AMediaFailureReportedLater_IsTracedByTypeNameOnly()
    {
        Create().Play(AlertKind.Failed);

        _outputs[0].Fail("InvalidDataException");

        Assert.Equal(["alert sound-failed kind=failed reason=media-failed error=InvalidDataException"], _trace);
    }

    [Fact]
    public void ALookupThatThrows_IsTraced_AndNeverEscapes()
    {
        var player = new MediaAlertSoundPlayer(
            _ => throw new InvalidOperationException(@"C:\secret"), _existing.Contains, () => new FakeOutput(), _trace.Add);

        player.Play(AlertKind.Failed);

        Assert.Equal(["alert sound-failed kind=failed error=InvalidOperationException"], _trace);
    }

    [Fact]
    public void ATraceThatAlsoThrows_StillNeverEscapes()
    {
        _outputThrows = true;

        Create(_ => throw new IOException("sink down")).Play(AlertKind.Failed);
    }

    [Fact]
    public void EachKindKeepsOneOutput_AndPlaysTheCurrentFileEveryTime()
    {
        var player = Create();
        player.Play(AlertKind.Failed);
        player.Play(AlertKind.Warning);

        _sounds[AlertKind.Failed] = WarningPath;
        player.Play(AlertKind.Failed);

        Assert.Equal(2, _outputs.Count);
        Assert.Equal([FailedPath, WarningPath], _outputs[0].Played);
    }

    private sealed class FakeOutput : IAlertSoundOutput
    {
        public List<string> Played { get; } = [];
        public bool Throws { get; init; }

        public event Action<string>? Failed;

        public void Play(string path)
        {
            if (Throws) throw new IOException(@"cannot open C:\Users\someone\AppData\Local\CielWin\sounds");
            Played.Add(path);
        }

        public void Fail(string errorType) => Failed?.Invoke(errorType);
    }
}
