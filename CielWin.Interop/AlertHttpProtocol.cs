using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CielWin.Interop;

/// <summary>
/// The pure rules of the localhost HTTP alert endpoint: the route, the body ceiling, how a JSON body
/// becomes <c>kind:count</c> command text, and how the command handler's reply becomes an HTTP
/// status code.
/// </summary>
/// <remarks>
/// <para>
/// The body is <c>{ "warning": 2, "failed": 1, "duration": 5 }</c>, every field optional. It is
/// translated field by field, in the order written, into <c>"warning:2 failed:1 duration:5"</c> and
/// handed to the command handler. So this class checks only the JSON shape: the counts, the
/// duration range, repeated fields and "at least one group" stay owned by the command parser.
/// </para>
/// <para>
/// Replies use <see cref="AlertReplyProtocol"/>'s vocabulary (<c>"ok"</c> / <c>"error: ..."</c>) as
/// the response body; only the status code is HTTP-specific. An accepted alert answers
/// <c>"ok id=&lt;n&gt;"</c> (<see cref="FormatAccepted"/>) so the caller can clear it later; an
/// ignored (busy) request still answers plain <c>"ok"</c>.
/// </para>
/// <para>
/// <see cref="AlertsClearPath"/> takes <c>{}</c> (clear the held warning) or <c>{ "id": n }</c>
/// (clear that alert), see <see cref="TryParseClear"/>.
/// </para>
/// </remarks>
public static class AlertHttpProtocol
{
    /// <summary>The route that shows an alert.</summary>
    public const string AlertsPath = "/v1/alerts";

    /// <summary>The route that clears a held (or, by id, any) alert.</summary>
    public const string AlertsClearPath = "/v1/alerts/clear";

    /// <summary>The clear route's body cap: <c>{ "id": 2147483647 }</c> with room for whitespace.</summary>
    public const int ClearMaxBodyBytes = 64;

    /// <summary>
    /// Request bodies past this many bytes are rejected unread. The largest meaningful body is well
    /// under 100 bytes; this leaves room for whitespace without letting a caller stream megabytes.
    /// </summary>
    public const int MaxBodyBytes = 1024;

    /// <summary>The port used when the settings file does not name one.</summary>
    public const int DefaultPort = 43811;

    private const string InvalidJson = "body is not valid JSON";

    private const string AcceptedPrefix = AlertReplyProtocol.OkReply + " id=";

    private const string ClearIdField = "id";

    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "warning", "failed", "duration",
    };

    /// <summary>
    /// Translates <paramref name="body"/> into command text. Never throws: a malformed body is an
    /// everyday event from outside the process.
    /// </summary>
    public static bool TryTranslate(string? body, out string? command, out string? error)
    {
        command = null;

        if (body is null)
        {
            error = InvalidJson;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "body must be a JSON object";
                return false;
            }

            var tokens = new List<string>();
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!KnownFields.Contains(field.Name))
                {
                    error = $"unknown field '{field.Name}'";
                    return false;
                }

                if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out var number))
                {
                    error = $"field '{field.Name}' must be a whole number";
                    return false;
                }

                tokens.Add(string.Create(CultureInfo.InvariantCulture, $"{field.Name}:{number}"));
            }

            command = string.Join(' ', tokens);
            error = null;
            return true;
        }
        // InvalidOperationException: the body is valid JSON, but a lone escaped surrogate
        // ("\uD800") in a key cannot be turned into a .NET string -- still a malformed body.
        catch (Exception parseFailure) when (parseFailure is JsonException or InvalidOperationException)
        {
            error = InvalidJson;
            return false;
        }
    }

    /// <summary>
    /// Reads a clear-route body: <c>{}</c> (<paramref name="id"/> <see langword="null"/>, the held
    /// warning) or <c>{ "id": n }</c> with <c>n</c> a whole number &gt;= 1; nothing else. Never throws.
    /// </summary>
    public static bool TryParseClear(string? body, out int? id, out string? error)
    {
        id = null;
        error = InvalidJson;
        if (body is null)
        {
            return false;
        }

        int? parsed = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "body must be a JSON object";
                return false;
            }

            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(field.Name, ClearIdField, StringComparison.Ordinal))
                {
                    error = $"unknown field '{field.Name}'";
                    return false;
                }

                if (parsed is not null)
                {
                    error = $"field '{ClearIdField}' is repeated";
                    return false;
                }

                if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out var number) || number < 1)
                {
                    error = $"field '{ClearIdField}' must be a whole number >= 1";
                    return false;
                }

                parsed = number;
            }
        }
        // Same as TryTranslate: a lone escaped surrogate in a key is still a malformed body.
        catch (Exception parseFailure) when (parseFailure is JsonException or InvalidOperationException)
        {
            return false;
        }

        id = parsed;
        error = null;
        return true;
    }

    /// <summary>The reply to an accepted (queued) alert: <c>"ok id=&lt;n&gt;"</c>.</summary>
    public static string FormatAccepted(long id) =>
        string.Create(CultureInfo.InvariantCulture, $"{AcceptedPrefix}{id}");

    /// <summary>
    /// Whether <paramref name="reply"/> is a 202: plain <c>"ok"</c>, or <c>"ok id=&lt;n&gt;"</c> with
    /// <c>n</c> a positive decimal without a leading zero and nothing after it.
    /// </summary>
    public static bool IsAccepted(string reply)
    {
        if (reply == AlertReplyProtocol.OkReply)
        {
            return true;
        }

        if (!reply.StartsWith(AcceptedPrefix, StringComparison.Ordinal) || reply.Length == AcceptedPrefix.Length
            || reply[AcceptedPrefix.Length] == '0')
        {
            return false;
        }

        return reply.AsSpan(AcceptedPrefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    /// <summary>
    /// The status code for a reply from the alert command handler: 202 accepted (<see cref="IsAccepted"/>),
    /// 503 alerts disabled, 400 any other rejected command, 500 anything unrecognised.
    /// </summary>
    public static int StatusCodeFor(string reply) => reply switch
    {
        _ when IsAccepted(reply) => 202,
        _ when reply == AlertReplyProtocol.FormatError("alerts are disabled") => 503,
        _ when reply == AlertReplyProtocol.FormatError("internal error") => 500,
        _ when reply.StartsWith(AlertReplyProtocol.FormatError(string.Empty), StringComparison.Ordinal) => 400,
        _ => 500,
    };
}
