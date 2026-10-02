using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CielWin.App.Tray;

/// <summary>
/// The icons the tray menu wears, drawn from Windows' own icon font: <c>Segoe Fluent Icons</c> on
/// Windows 11, <c>Segoe MDL2 Assets</c> on 10. .NET's <see cref="SystemIcons"/> has none of these, and
/// a font renders at whatever size and DPI the notification area asks for.
/// </summary>
/// <remarks>
/// Every draw is best-effort: a missing font or any GDI+ failure leaves the item with its text and no
/// icon. A decoration is never worth failing startup over.
/// </remarks>
public static class TrayGlyphs
{
    /// <summary>Segoe icon-font code points, all in the Private Use Area where both fonts keep them.</summary>
    public const string Mode = "";

    /// <inheritdoc cref="Mode"/>
    public const string Scene = "";

    /// <inheritdoc cref="Mode"/>
    public const string Exit = "";

    /// <summary>
    /// Draws one glyph at the menu's icon size in the menu's text colour (so it follows light and dark
    /// themes), or <see langword="null"/> if it cannot be drawn.
    /// </summary>
    public static Image? Render(string glyph)
    {
        try
        {
            var size = SystemInformation.SmallIconSize;
            using var family = ResolveIconFont();
            if (family is null)
            {
                return null;
            }

            var bitmap = new Bitmap(size.Width, size.Height);
            using var font = new Font(family, size.Height * 0.66f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(SystemColors.MenuText);
            using var graphics = Graphics.FromImage(bitmap);
            using var centred = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, size.Width, size.Height), centred);
            return bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolved by NAME: GDI+ silently substitutes a default face for an unknown family, which would
    /// draw a missing-glyph box instead of failing where it can be caught.
    /// </summary>
    private static FontFamily? ResolveIconFont()
    {
        foreach (var name in (string[])["Segoe Fluent Icons", "Segoe MDL2 Assets"])
        {
            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}
