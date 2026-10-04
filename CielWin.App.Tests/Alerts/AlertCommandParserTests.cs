using CielWin.App.Alerts;

namespace CielWin.App.Tests.Alerts;

/// <summary>
/// The grammar <c>--alert</c> accepts: <c>kind:count</c> tokens separated by whitespace, an
/// optional <c>duration:seconds</c>.
/// </summary>
/// <remarks>
/// Never throws: bad input comes from outside the process, over the local HTTP endpoint, and a malformed
/// command must be rejected and logged, not crash the wallpaper. Every failing case here asserts
/// <see cref="AlertCommandParseResult.Success"/> is <see langword="false"/> and that
/// <see cref="AlertCommandParseResult.Error"/> names the offending token, never an exception.
/// </remarks>
public sealed class AlertCommandParserTests
{
    [Fact]
    public void TwoKinds_ParseInCommandOrder_WithTheDefaultDuration()
    {
        var result = AlertCommandParser.Parse("warning:2 failed:1");

        Assert.True(result.Success);
        var command = result.Command!;
        Assert.Equal(
            [new AlertGroup(AlertKind.Warning, 2), new AlertGroup(AlertKind.Failed, 1)],
            command.Groups);
        Assert.Equal(TimeSpan.FromSeconds(5), command.Duration);
    }

    [Fact]
    public void AnExplicitDuration_OverridesTheDefault()
    {
        var result = AlertCommandParser.Parse("failed:3 duration:8");

        Assert.True(result.Success);
        Assert.Equal([new AlertGroup(AlertKind.Failed, 3)], result.Command!.Groups);
        Assert.Equal(TimeSpan.FromSeconds(8), result.Command!.Duration);
    }

    /// <summary>Kind and key names are case-insensitive; the token grammar is not otherwise loose.</summary>
    [Fact]
    public void KeysAndKindsAreCaseInsensitive()
    {
        var result = AlertCommandParser.Parse("WARNING:2 Duration:10");

        Assert.True(result.Success);
        Assert.Equal([new AlertGroup(AlertKind.Warning, 2)], result.Command!.Groups);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Command!.Duration);
    }

    /// <summary>Runs of whitespace and surrounding padding are all just separators.</summary>
    [Fact]
    public void ExtraWhitespaceBetweenAndAroundTokensIsIgnored()
    {
        var result = AlertCommandParser.Parse("  warning:1   failed:2  \t ");

        Assert.True(result.Success);
        Assert.Equal(
            [new AlertGroup(AlertKind.Warning, 1), new AlertGroup(AlertKind.Failed, 2)],
            result.Command!.Groups);
    }

    /// <summary>The order tiles are written in is the order the layout later places them in.</summary>
    [Fact]
    public void GroupOrderFollowsWriteOrder_NotKindOrCount()
    {
        var result = AlertCommandParser.Parse("failed:2 warning:3");

        Assert.True(result.Success);
        Assert.Equal(
            [new AlertGroup(AlertKind.Failed, 2), new AlertGroup(AlertKind.Warning, 3)],
            result.Command!.Groups);
    }

    [Theory]
    [InlineData("warning:1 warning:2")]
    [InlineData("duration:5 warning:1 duration:9")]
    public void ARepeatedKey_IsAnError(string input)
    {
        var result = AlertCommandParser.Parse(input);

        Assert.False(result.Success);
        Assert.Null(result.Command);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void DurationAlone_WithNoWarningOrFailed_IsAnError()
    {
        var result = AlertCommandParser.Parse("duration:5");

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_IsAnError(string input)
    {
        Assert.False(AlertCommandParser.Parse(input).Success);
    }

    [Fact]
    public void NullInput_IsAnError_NotAnException()
    {
        var result = AlertCommandParser.Parse(null);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    /// <summary>16 tiles is allowed; one more, spread across both kinds, is not.</summary>
    [Fact]
    public void ExactlySixteenTiles_IsAllowed()
    {
        Assert.True(AlertCommandParser.Parse("warning:8 failed:8").Success);
    }

    [Fact]
    public void MoreThanSixteenTilesTotal_IsAnError()
    {
        Assert.False(AlertCommandParser.Parse("warning:16 failed:1").Success);
    }

    [Theory]
    [InlineData("warning2")]
    [InlineData(":5")]
    [InlineData("warning:")]
    [InlineData("foo:2")]
    [InlineData("warning:abc")]
    [InlineData("warning:-1")]
    [InlineData("warning:+1")]
    [InlineData("warning:1.5")]
    [InlineData("warning:0")]
    [InlineData("warning:17")]
    [InlineData("duration:61")]
    public void AMalformedToken_IsAnErrorNamingIt(string token)
    {
        // "duration:61" needs at least one kind to reach the range check.
        var input = token.StartsWith("duration", StringComparison.Ordinal) ? $"warning:1 {token}" : token;

        var result = AlertCommandParser.Parse(input);

        Assert.False(result.Success);
        Assert.Contains(token, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void InputLongerThanTheLimit_IsAnError()
    {
        var tooLong = "warning:1 " + new string(' ', 260);

        Assert.False(AlertCommandParser.Parse(tooLong).Success);
    }

    [Fact]
    public void InputAtExactlyTheLimit_IsStillParsed()
    {
        // "warning:1" padded with trailing spaces to exactly 256 characters.
        var atLimit = "warning:1" + new string(' ', 256 - "warning:1".Length);

        Assert.Equal(256, atLimit.Length);
        Assert.True(AlertCommandParser.Parse(atLimit).Success);
    }

    /// <summary>H1: <c>duration:0</c> holds a warning-only command until it is cleared.</summary>
    [Theory]
    [InlineData("warning:1 duration:0", 1)]
    [InlineData("WARNING:3 Duration:0", 3)]
    [InlineData("duration:0 warning:16", 16)]
    [InlineData("warning:1 duration:00", 1)]
    public void DurationZero_WithWarningOnly_IsAHeldCommand(string input, int count)
    {
        var result = AlertCommandParser.Parse(input);

        Assert.True(result.Success, result.Error);
        Assert.Equal([new AlertGroup(AlertKind.Warning, count)], result.Command!.Groups);
        Assert.Equal(TimeSpan.Zero, result.Command.Duration);
        Assert.True(result.Command.IsHeld);
        Assert.False(result.Command.HasFailed);
    }

    [Theory]
    [InlineData("failed:1 duration:0")]
    [InlineData("warning:1 failed:1 duration:0")]
    public void DurationZero_WithAnyFailedGroup_IsRejected(string input)
    {
        var result = AlertCommandParser.Parse(input);

        Assert.False(result.Success);
        Assert.Equal("'duration:0' requires warning only", result.Error);
    }

    [Fact]
    public void DurationZero_Alone_StillNeedsAGroup() =>
        Assert.Equal(
            "at least one 'warning:N' or 'failed:N' group is required",
            AlertCommandParser.Parse("duration:0").Error);

    [Fact]
    public void ATimedCommand_IsNotHeld_AndKnowsWhetherItHasAFailedTile()
    {
        var command = AlertCommandParser.Parse("warning:1 failed:1").Command!;

        Assert.False(command.IsHeld);
        Assert.True(command.HasFailed);
    }
}
