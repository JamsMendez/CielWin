namespace CielWin.Interop.Tests.Win32;

/// <summary>
/// The gate that decides whether a desktop fact runs, pinned as a pure decision rather than felt
/// through the environment. The gate takes its inputs as arguments, so these facts are headless and
/// deterministic and carry no <c>RequiresDesktop</c> trait -- they exercise the DECISION, never a
/// desktop.
/// </summary>
/// <remarks>
/// The expensive probes arrive as delegates so they can be proven NOT to run before the opt-in
/// passes: enumerating processes before checking a variable costs every <c>dotnet test</c> run
/// something it never needed to spend.
/// </remarks>
public sealed class DesktopGateTests
{
    private static Func<bool> Never => () => false;

    private static Func<bool> Always => () => true;

    /// <summary>A probe that records whether anybody actually asked it.</summary>
    private sealed class CountingProbe(bool answer)
    {
        public int Calls { get; private set; }

        public bool Read()
        {
            Calls++;
            return answer;
        }
    }

    /// <summary>The default has to be OFF: these facts touch whatever the machine has open.</summary>
    [Fact]
    public void WithoutTheOptIn_EveryGateSkipsAndNamesTheVariable()
    {
        foreach (var reason in new[]
                 {
                     DesktopGate.OptInSkipReason(runFlag: null),
                     DesktopGate.SessionSkipReason(runFlag: null, Never),
                     DesktopGate.RaisedLayoutSkipReason(runFlag: null, Never, Always),
                 })
        {
            Assert.NotNull(reason);
            Assert.Contains(DesktopGate.RunFlagVariable, reason);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData(" 1")]
    [InlineData("")]
    public void WithSomethingOtherThanTheExactOptIn_TheGateStillSkips(string runFlag)
    {
        Assert.NotNull(DesktopGate.OptInSkipReason(runFlag));
        Assert.NotNull(DesktopGate.SessionSkipReason(runFlag, Never));
    }

    [Fact]
    public void WithTheOptInAndNothingInTheWay_EveryGateRuns()
    {
        Assert.Null(DesktopGate.OptInSkipReason("1"));
        Assert.Null(DesktopGate.SessionSkipReason("1", Never));
    }

    /// <summary>A running CielWin owns the wallpaper layer, so a fact attaching its own would fight it.</summary>
    [Fact]
    public void WithTheAppRunning_TheSessionGateSkipsAndSaysHowToStopIt()
    {
        var reason = DesktopGate.SessionSkipReason("1", Always);

        Assert.NotNull(reason);
        Assert.Contains("tray", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAppIsNotProbedUntilTheOptInPasses()
    {
        var probe = new CountingProbe(answer: true);

        Assert.NotNull(DesktopGate.SessionSkipReason(runFlag: null, probe.Read));
        Assert.Equal(0, probe.Calls);

        Assert.NotNull(DesktopGate.SessionSkipReason("1", probe.Read));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void WithTheLegacyLayout_TheRaisedLayoutGateSkipsAndNamesTheLayout()
    {
        var reason = DesktopGate.RaisedLayoutSkipReason("1", Never, isRaisedLayout: Never);

        Assert.NotNull(reason);
        Assert.Contains("legacy", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithTheRaisedLayout_TheRaisedLayoutGateRuns()
    {
        Assert.Null(DesktopGate.RaisedLayoutSkipReason("1", Never, isRaisedLayout: Always));
    }

    [Fact]
    public void WithTheAppRunning_TheRaisedLayoutGateSkipsEvenOnTheRaisedLayout()
    {
        Assert.NotNull(DesktopGate.RaisedLayoutSkipReason("1", Always, isRaisedLayout: Always));
    }

    /// <summary>Reading Progman's own style is a real Win32 call, not worth paying for without the opt-in.</summary>
    [Fact]
    public void TheRaisedLayoutIsNotProbedUntilTheOptInPasses()
    {
        var probe = new CountingProbe(answer: true);

        Assert.NotNull(DesktopGate.RaisedLayoutSkipReason(runFlag: null, Never, probe.Read));
        Assert.Equal(0, probe.Calls);

        Assert.NotNull(DesktopGate.RaisedLayoutSkipReason("1", Always, probe.Read));
        Assert.Equal(0, probe.Calls);

        Assert.NotNull(DesktopGate.RaisedLayoutSkipReason("1", Never, () => false));
        Assert.Null(DesktopGate.RaisedLayoutSkipReason("1", Never, () => true));
    }
}
