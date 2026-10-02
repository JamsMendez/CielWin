using CielWin.App.Tray;

namespace CielWin.App.Tests.Tray;

/// <summary>
/// Which glyph each tray command wears. The choice is pure and pinned here; drawing it needs a live
/// desktop, a font and a DPI, and is verified by hand.
/// </summary>
public sealed class TrayGlyphsTests
{
    [Theory]
    [InlineData(TrayGlyphs.Exit)]
    [InlineData(TrayGlyphs.Mode)]
    [InlineData(TrayGlyphs.Scene)]
    public void EveryGlyphIsExactlyOneCharacter(string glyph)
    {
        Assert.Equal(1, glyph.Length);
    }

    /// <summary>
    /// Both Segoe icon fonts keep their glyphs in the Private Use Area; a code point outside it would
    /// render as a letter on a machine missing the font.
    /// </summary>
    [Theory]
    [InlineData(TrayGlyphs.Exit)]
    [InlineData(TrayGlyphs.Mode)]
    [InlineData(TrayGlyphs.Scene)]
    public void EveryGlyphSitsInThePrivateUseArea(string glyph)
    {
        Assert.InRange(glyph[0], '', '');
    }
}
