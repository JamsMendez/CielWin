namespace CielWin.App.Alerts;

/// <summary>One <c>kind:count</c> group from an alert command, in the order it was written.</summary>
public sealed record AlertGroup(AlertKind Kind, int Count);

/// <summary>
/// A successfully parsed alert command: the tile groups in command order, plus how long
/// they stay on screen. A zero <see cref="Duration"/> is a held warning: it stays until it is cleared
/// (or the queue's hold max runs out), and only a warning-only command can be held.
/// </summary>
public sealed record AlertCommand(IReadOnlyList<AlertGroup> Groups, TimeSpan Duration)
{
    /// <summary>The number of tiles this command asks for, summed across every group.</summary>
    public int TotalTiles => Groups.Sum(group => group.Count);

    /// <summary>Whether this is a held warning (<c>duration:0</c>), shown until cleared.</summary>
    public bool IsHeld => Duration == TimeSpan.Zero;

    /// <summary>Whether any group is a failed one.</summary>
    public bool HasFailed => Groups.Any(group => group.Kind == AlertKind.Failed);
}

/// <summary>
/// What <see cref="AlertCommandParser.Parse"/> returns: either the parsed command, or a short
/// message naming what was wrong with the input. Never an exception -- the input comes from
/// outside the process, over the local HTTP endpoint, and a malformed command is an everyday event, not one.
/// </summary>
public sealed record AlertCommandParseResult
{
    private AlertCommandParseResult(AlertCommand? command, string? error)
    {
        Command = command;
        Error = error;
    }

    /// <summary>The parsed command, or <see langword="null"/> when <see cref="Success"/> is <see langword="false"/>.</summary>
    public AlertCommand? Command { get; }

    /// <summary>
    /// A short, human-readable message naming the offending token, or <see langword="null"/> when
    /// <see cref="Success"/> is <see langword="true"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>Whether <see cref="Command"/> is present.</summary>
    public bool Success => Command is not null;

    public static AlertCommandParseResult Ok(AlertCommand command) => new(command, null);

    public static AlertCommandParseResult Fail(string error) => new(null, error);
}
