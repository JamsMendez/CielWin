namespace CielWin.App.Alerts;

/// <summary>
/// One "show" request for the preloaded scene layer: the ordered tile kinds to draw (wire strings
/// <c>"warning"</c>/<c>"failed"</c>, failed first and capped at 8, per <see cref="AlertTileLayout"/>),
/// the grid to place them in, and the gap to draw around and between them.
/// </summary>
/// <remarks>
/// <see cref="Gap"/> is in PHYSICAL pixels; callers pass <see cref="AlertTileLayout.GapPixels"/>. The
/// page converts it to CSS pixels with <c>devicePixelRatio</c>.
/// <para>
/// Carried unchanged through <see cref="AlertLayerPreloadState"/>'s pending/shown tracking to the
/// controller, which serializes it into the <c>{type:"show",...}</c> WebView2 message via
/// <see cref="AlertLayerMessages.Show"/>.
/// </para>
/// <para>
/// <c>WorkArea*</c> is the monitor's work area (<see cref="AlertLayerWorkArea"/>), PHYSICAL pixels,
/// RELATIVE to the layer surface (the primary monitor's own bounds). The page lays an N&gt;1 mosaic
/// out inside this rect instead of the whole canvas (N=1 still uses the whole canvas). Defaulted to
/// zero -- <see cref="AlertLayerWorkArea.Unavailable"/>'s own shape -- meaning "lay out on the whole
/// canvas".
/// </para>
/// </remarks>
public sealed record AlertShowRequest(
    IReadOnlyList<string> Tiles, int Columns, int Rows, int Gap, int DurationMilliseconds,
    int WorkAreaLeft = 0, int WorkAreaTop = 0, int WorkAreaWidth = 0, int WorkAreaHeight = 0);
