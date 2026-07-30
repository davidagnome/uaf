namespace UAF.Media;

/// <summary>
/// One character's cell in a <see cref="FontAtlas"/>.
/// </summary>
/// <remarks>
/// <see cref="Advance"/> is stored separately from the cell width even though the original always
/// has them equal — <c>CDXBitmapFont::Create</c> sets both from the same
/// <c>GetTextExtentPoint32</c> result (<c>CDXBitmapFont.cpp:209,219-222</c>), so the font has no
/// kerning, no side bearings, and no notion of an advance narrower than the drawn glyph. Keeping
/// the field distinct costs nothing and lets a TrueType rasteriser be honest about metrics it
/// genuinely has.
/// </remarks>
public readonly record struct Glyph(SurfaceRect Source, int Advance)
{
    public int Width => Source.Width;

    public int Height => Source.Height;
}

/// <summary>
/// A rasterised 256-cell character sheet: the ported form of <c>CDXBitmapFont</c>'s
/// <c>TextSurface</c> plus its <c>CDXBitmapFontArray</c> (<c>UAFWin/CDXBitmapFont.h:38</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>256 cells, not Unicode.</b> The original indexes by <c>unsigned char</c>
/// (<c>CDXBitmapFont.cpp:533</c>) over a single-byte codepage — the same Windows-1252 MBCS
/// assumption the serialization layer records. Text arriving from a design is bytes, and a glyph
/// index is one of those bytes.
/// </para>
/// <para>
/// <b>Colour is not baked in here.</b> The original rasterised a whole separate font per colour:
/// <c>AVAIL_FONT::LoadFont</c> loops <c>colorNum</c> from <c>whiteColor</c> to <c>silverColor</c>
/// and builds a <c>CDXBitmapFont</c> for each (<c>GlobalData.cpp:5964-5975</c>), because GDI drew
/// coloured text and the colour was fixed once <c>TextOut</c> had run. This port keeps one atlas
/// and tints at draw time. It is a deliberate deviation, and it is the better behaviour rather
/// than merely the cheaper one: GDI's antialiased edge pixels blend glyph colour toward the
/// background, and since the colour key only removes pixels matching the background <i>exactly</i>,
/// the original's non-white fonts carry fringes of the wrong hue. Tinting a coverage mask does not.
/// </para>
/// </remarks>
public sealed class FontAtlas
{
    /// <summary>The number of cells, <c>CDXBitmapFont::MAX_CHAR</c>.</summary>
    public const int CharacterCount = 256;

    private readonly Glyph[] glyphs;

    public FontAtlas(Surface sheet, Glyph[] glyphs)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(glyphs);

        if (glyphs.Length != CharacterCount)
        {
            throw new ArgumentException(
                $"expected {CharacterCount} glyphs, got {glyphs.Length}", nameof(glyphs));
        }

        Sheet = sheet;
        this.glyphs = glyphs;

        int maxWidth = 0, maxHeight = 0;
        foreach (var glyph in glyphs)
        {
            maxWidth = Math.Max(maxWidth, glyph.Width);
            maxHeight = Math.Max(maxHeight, glyph.Height);
        }
        MaxCharWidth = maxWidth;
        MaxCharHeight = maxHeight;
    }

    /// <summary>The sheet every glyph is blitted from.</summary>
    public Surface Sheet { get; }

    /// <summary><c>m_MaxCharWidth</c> (<c>CDXBitmapFont.cpp:224-227</c>).</summary>
    public int MaxCharWidth { get; }

    /// <summary><c>m_MaxCharHeight</c>.</summary>
    public int MaxCharHeight { get; }

    public Glyph this[byte character] => glyphs[character];

    /// <summary>
    /// Lays 256 cells out left to right, wrapping at <paramref name="sheetWidth"/>, which is how
    /// <c>CDXBitmapFont::Create</c> packs them (<c>CDXBitmapFont.cpp:213-233</c>).
    /// </summary>
    /// <remarks>
    /// The original hard-codes a 320-pixel sheet and a height of <c>Y + Height + 5</c>
    /// (<c>CDXBitmapFont.cpp:237</c>) — the five is slack for the last row's descenders. Both are
    /// reproduced so a rasteriser that measures the same glyphs lands them in the same cells,
    /// which is what makes a captured atlas from the C++ build directly comparable.
    /// </remarks>
    public static Glyph[] Layout(ReadOnlySpan<(int Width, int Height)> extents, int sheetWidth,
                                 out int sheetHeight)
    {
        if (extents.Length != CharacterCount)
        {
            throw new ArgumentException(
                $"expected {CharacterCount} extents, got {extents.Length}", nameof(extents));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sheetWidth);

        var glyphs = new Glyph[CharacterCount];
        int x = 0, y = 0, rowHeight = 0;

        for (int i = 0; i < CharacterCount; i++)
        {
            (int width, int height) = extents[i];

            // Wrap before placing, not after, so a glyph never straddles the right edge.
            if (x + width > sheetWidth && x > 0)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            glyphs[i] = new Glyph(SurfaceRect.FromBounds(x, y, width, height), width);
            x += width;
            rowHeight = Math.Max(rowHeight, height);
        }

        sheetHeight = y + rowHeight + 5;
        return glyphs;
    }

    /// <summary>The sheet width the original uses.</summary>
    public const int DefaultSheetWidth = 320;
}
