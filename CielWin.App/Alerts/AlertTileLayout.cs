namespace CielWin.App.Alerts;

/// <summary>
/// Turns a parsed <see cref="AlertCommand"/> into the ordered tile kinds to show and the grid to
/// place them in, mirroring how a tiling window manager lays out windows (gap applied by the page,
/// row-major fill). Pure and unit-tested directly (<c>AlertTileLayoutTests</c>); nothing here
/// touches a WebView2, a clock, or the desktop.
/// </summary>
public sealed record AlertTileLayout(IReadOnlyList<AlertKind> Tiles, int Columns, int Rows)
{
    /// <summary>
    /// The gap, in physical pixels, drawn around and between mosaic tiles. CielWin has no tiling
    /// engine and so no gap setting; 8 is the value the mosaic was designed around.
    /// </summary>
    public const int GapPixels = 8;

    /// <summary>Never more than this many tiles are shown at once, however many the command asked for.</summary>
    public const int MaxTiles = 8;

    /// <summary>
    /// Every failed tile first, then every warning tile, capped at <see cref="MaxTiles"/> (failed always wins past the cap), plus the grid that tile count maps to.
    /// </summary>
    public static AlertTileLayout From(AlertCommand command)
    {
        var failedCount = command.Groups.Where(group => group.Kind == AlertKind.Failed).Sum(group => group.Count);
        var warningCount = command.Groups.Where(group => group.Kind == AlertKind.Warning).Sum(group => group.Count);

        var tiles = new List<AlertKind>(Math.Min(failedCount + warningCount, MaxTiles));
        for (var i = 0; i < failedCount && tiles.Count < MaxTiles; i++) tiles.Add(AlertKind.Failed);
        for (var i = 0; i < warningCount && tiles.Count < MaxTiles; i++) tiles.Add(AlertKind.Warning);

        var (columns, rows) = GridFor(tiles.Count);
        return new AlertTileLayout(tiles, columns, rows);
    }

    /// <summary>
    /// The grid table: 1 -&gt; 1x1 (full display, as today);
    /// 2 -&gt; 2 columns x 1 row; 3-4 -&gt; 2x2; 5-6 -&gt; 3x2; 7+ -&gt; 4x2. Slots fill row-major; a
    /// grid with more slots than tiles leaves the rest blank (the page's job, not this one's).
    /// </summary>
    private static (int Columns, int Rows) GridFor(int tileCount) => tileCount switch
    {
        <= 1 => (1, 1),
        2 => (2, 1),
        <= 4 => (2, 2),
        <= 6 => (3, 2),
        _ => (4, 2),
    };
}
