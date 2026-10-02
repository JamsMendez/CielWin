namespace CielWin.Interop;

/// <summary>
/// The reply vocabulary of the alert command handler, also used as the plain-text body of every HTTP
/// response: <see cref="OkReply"/>, or a single <c>"error: &lt;reason&gt;"</c> line built with
/// <see cref="FormatError"/>.
/// </summary>
public static class AlertReplyProtocol
{
    /// <summary>The reply to a command that was accepted.</summary>
    public const string OkReply = "ok";

    private const string ErrorPrefix = "error: ";

    /// <summary>Builds the one-line reply for a rejected command.</summary>
    public static string FormatError(string reason) => ErrorPrefix + reason;
}
