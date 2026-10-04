using CielWin.Interop;

namespace CielWin.Interop.Tests;

/// <summary>
/// Pure rules for the HTTP alert endpoint: how a JSON body becomes the <c>kind:count</c> command
/// text, and how the handler's reply becomes a status code. Counts and duration limits are NOT
/// checked here -- the command parser owns them.
/// </summary>
public sealed class AlertHttpProtocolTests
{
    [Theory]
    [InlineData("""{"warning":2}""", "warning:2")]
    [InlineData("""{"failed":1}""", "failed:1")]
    [InlineData("""{"warning":2,"failed":1,"duration":5}""", "warning:2 failed:1 duration:5")]
    [InlineData("""{"duration":5,"failed":1,"warning":2}""", "duration:5 failed:1 warning:2")]
    [InlineData("""  { "warning" : 3 }  """, "warning:3")]
    [InlineData("""{}""", "")]
    public void TryTranslate_AValidBody_KeepsTheFieldsInTheOrderWritten(string body, string expected)
    {
        var ok = AlertHttpProtocol.TryTranslate(body, out var command, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, command);
    }

    [Fact]
    public void TryTranslate_ARepeatedField_IsPassedThroughForTheParserToReject() =>
        Assert.Equal("warning:1 warning:2", Translate("""{"warning":1,"warning":2}"""));

    [Fact]
    public void TryTranslate_ANegativeCount_IsPassedThroughForTheParserToReject() =>
        Assert.Equal("warning:-1", Translate("""{"warning":-1}"""));

    [Theory]
    [InlineData("", "body is not valid JSON")]
    [InlineData("{", "body is not valid JSON")]
    [InlineData("warning:2", "body is not valid JSON")]
    // Valid JSON, but a lone escaped surrogate cannot become a .NET string: reading the key throws
    // InvalidOperationException, not JsonException.
    [InlineData("""{"\uD800":1}""", "body is not valid JSON")]
    [InlineData("[1]", "body must be a JSON object")]
    [InlineData("2", "body must be a JSON object")]
    [InlineData("null", "body must be a JSON object")]
    [InlineData("""{"Warning":1}""", "unknown field 'Warning'")]
    [InlineData("""{"info":1}""", "unknown field 'info'")]
    [InlineData("""{"warning":"2"}""", "field 'warning' must be a whole number")]
    [InlineData("""{"warning":1.5}""", "field 'warning' must be a whole number")]
    [InlineData("""{"warning":true}""", "field 'warning' must be a whole number")]
    [InlineData("""{"duration":null}""", "field 'duration' must be a whole number")]
    [InlineData("""{"failed":99999999999}""", "field 'failed' must be a whole number")]
    public void TryTranslate_AnInvalidBody_FailsWithAShortReason(string body, string expected)
    {
        var ok = AlertHttpProtocol.TryTranslate(body, out var command, out var error);

        Assert.False(ok);
        Assert.Null(command);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void TryTranslate_ANullBody_Fails()
    {
        var ok = AlertHttpProtocol.TryTranslate(null, out var command, out var error);

        Assert.False(ok);
        Assert.Null(command);
        Assert.Equal("body is not valid JSON", error);
    }

    [Theory]
    [InlineData(AlertReplyProtocol.OkReply, 202)]
    [InlineData("error: alerts are disabled", 503)]
    [InlineData("error: 'warning:0' must be 1..16", 400)]
    [InlineData("error: internal error", 500)]
    [InlineData("something unexpected", 500)]
    public void StatusCodeFor_MapsTheHandlerReply(string reply, int expected) =>
        Assert.Equal(expected, AlertHttpProtocol.StatusCodeFor(reply));

    /// <summary>H1: an accepted alert answers <c>ok id=&lt;n&gt;</c>; anything else after "ok" is unrecognised.</summary>
    [Theory]
    [InlineData("ok id=1", 202)]
    [InlineData("ok id=42", 202)]
    [InlineData("ok id=", 500)]
    [InlineData("ok id=0", 500)]
    [InlineData("ok id=01", 500)]
    [InlineData("ok id=x", 500)]
    [InlineData("ok id=1 ", 500)]
    [InlineData("ok  id=1", 500)]
    [InlineData("okid=1", 500)]
    public void StatusCodeFor_AnAcceptedAlertWithItsId(string reply, int expected) =>
        Assert.Equal(expected, AlertHttpProtocol.StatusCodeFor(reply));

    [Fact]
    public void FormatAccepted_WritesTheId()
    {
        Assert.Equal("ok id=7", AlertHttpProtocol.FormatAccepted(7));
        Assert.True(AlertHttpProtocol.IsAccepted("ok id=7"));
        Assert.True(AlertHttpProtocol.IsAccepted(AlertReplyProtocol.OkReply));
        Assert.False(AlertHttpProtocol.IsAccepted("error: alerts are disabled"));
    }

    [Fact]
    public void TryTranslate_DurationZero_IsPassedThroughForTheParser() =>
        Assert.Equal("warning:1 duration:0", Translate("""{"warning":1,"duration":0}"""));

    [Theory]
    [InlineData("""{}""", null)]
    [InlineData("""  { }  """, null)]
    [InlineData("""{"id":1}""", 1)]
    [InlineData("""{ "id" : 12 }""", 12)]
    [InlineData("""{"id":2147483647}""", 2147483647)]
    public void TryParseClear_EmptyOrAnId(string body, int? expected)
    {
        var ok = AlertHttpProtocol.TryParseClear(body, out var id, out var error);

        Assert.True(ok, error);
        Assert.Null(error);
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("", "body is not valid JSON")]
    [InlineData("{", "body is not valid JSON")]
    [InlineData("""{"\uD800":1}""", "body is not valid JSON")]
    [InlineData("[1]", "body must be a JSON object")]
    [InlineData("null", "body must be a JSON object")]
    [InlineData("""{"warning":1}""", "unknown field 'warning'")]
    [InlineData("""{"Id":1}""", "unknown field 'Id'")]
    [InlineData("""{"id":0}""", "field 'id' must be a whole number >= 1")]
    [InlineData("""{"id":-1}""", "field 'id' must be a whole number >= 1")]
    [InlineData("""{"id":1.5}""", "field 'id' must be a whole number >= 1")]
    [InlineData("""{"id":"1"}""", "field 'id' must be a whole number >= 1")]
    [InlineData("""{"id":null}""", "field 'id' must be a whole number >= 1")]
    [InlineData("""{"id":2147483648}""", "field 'id' must be a whole number >= 1")]
    [InlineData("""{"id":1,"id":2}""", "field 'id' is repeated")]
    public void TryParseClear_AnythingElse_FailsWithAShortReason(string body, string expected)
    {
        var ok = AlertHttpProtocol.TryParseClear(body, out var id, out var error);

        Assert.False(ok);
        Assert.Null(id);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void TryParseClear_ANullBody_Fails()
    {
        Assert.False(AlertHttpProtocol.TryParseClear(null, out _, out var error));
        Assert.Equal("body is not valid JSON", error);
    }

    [Fact]
    public void ClearConstants_MatchCieLinux()
    {
        Assert.Equal("/v1/alerts/clear", AlertHttpProtocol.AlertsClearPath);
        Assert.Equal(64, AlertHttpProtocol.ClearMaxBodyBytes);
    }

    [Fact]
    public void Constants_MatchThePlan()
    {
        Assert.Equal("/v1/alerts", AlertHttpProtocol.AlertsPath);
        Assert.Equal(1024, AlertHttpProtocol.MaxBodyBytes);
        Assert.Equal(43811, AlertHttpProtocol.DefaultPort);
    }

    private static string? Translate(string body)
    {
        Assert.True(AlertHttpProtocol.TryTranslate(body, out var command, out var error), error);
        return command;
    }
}
