namespace UAF.Media;

/// <summary>How <see cref="BitmapFont.DrawAligned"/> places text in its field.</summary>
/// <remarks>
/// Values are the original's (<c>CDXBitmapFont.h:18,22</c>). Left is 0 by omission — the header
/// defines only centre and right, and everything else falls through to left.
/// </remarks>
public enum TextAlign
{
    Left = 0,
    Center = 1,
    Right = 3,
}

/// <summary>
/// Draws text from a <see cref="FontAtlas"/> — the ported half of <c>CDXBitmapFont</c>
/// (<c>UAFWin/CDXBitmapFont.cpp</c>) that has nothing to do with GDI.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a blit and an accumulator: measure by summing advances, draw by blitting each
/// cell and stepping X. That is genuinely all the original does once its atlas exists
/// (<c>CDXBitmapFont.cpp:533-547</c>), which is why this half ports cleanly while the rasteriser
/// needs a decision — see <see cref="IFontRasterizer"/>.
/// </para>
/// <para>
/// <b>Text is bytes, not a string.</b> Glyph lookup is by <c>unsigned char</c> over a single-byte
/// codepage, so the API takes <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/>. A
/// <see cref="string"/> overload encodes through Windows-1252 first. Taking UTF-16 directly would
/// silently index the atlas with the wrong value for every character above 127 — which is exactly
/// the accented range these designs use.
/// </para>
/// </remarks>
public sealed class BitmapFont(FontAtlas atlas)
{
    /// <summary>
    /// Windows-1252, the codepage every string in these files uses. Registered by
    /// <c>UAF.Serialization</c> too; doing it here as well keeps this assembly standalone.
    /// </summary>
    private static readonly System.Text.Encoding Ansi = CreateAnsiEncoding();

    private static System.Text.Encoding CreateAnsiEncoding()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    public FontAtlas Atlas { get; } = atlas ?? throw new ArgumentNullException(nameof(atlas));

    /// <summary>Encodes a string to the single-byte codepage the atlas is indexed by.</summary>
    public static byte[] Encode(string text) => Ansi.GetBytes(text ?? string.Empty);

    /// <summary><c>GetCharacterWidth</c> (<c>CDXBitmapFont.cpp:735</c>).</summary>
    public int GetCharacterWidth(byte character) => Atlas[character].Advance;

    /// <summary><c>GetCharacterHeight</c>.</summary>
    public int GetCharacterHeight(byte character) => Atlas[character].Height;

    /// <summary>
    /// <c>GetTextWidth</c> (<c>CDXBitmapFont.cpp:754</c>): the sum of advances.
    /// </summary>
    /// <param name="length">
    /// How many characters to measure; <b>negative means unlimited</b>. That is not a convenience
    /// added here — the original counts down and tests <c>length != 0</c>, so -1 walks away from
    /// zero and never terminates the loop, and every call site relies on it.
    /// </param>
    public int GetTextWidth(ReadOnlySpan<byte> text, int length = -1)
    {
        int width = 0;
        for (int i = 0; i < text.Length && length != 0; i++)
        {
            width += Atlas[text[i]].Advance;
            length--;
        }
        return width;
    }

    /// <inheritdoc cref="GetTextWidth(ReadOnlySpan{byte}, int)"/>
    public int GetTextWidth(string text, int length = -1) => GetTextWidth(Encode(text), length);

    /// <summary>
    /// <c>Draw</c> / <c>DrawTrans</c> (<c>CDXBitmapFont.cpp:525</c>, <c>:577</c>) — the two differ
    /// only in whether the blit honours the atlas's colour key.
    /// </summary>
    /// <returns>The X coordinate one past the last glyph drawn.</returns>
    public int Draw(Surface destination, int x, int y, ReadOnlySpan<byte> text,
                    bool transparent = true, uint? tint = null, int length = -1)
    {
        ArgumentNullException.ThrowIfNull(destination);

        for (int i = 0; i < text.Length && length != 0; i++)
        {
            var glyph = Atlas[text[i]];
            DrawGlyph(destination, x, y, glyph, transparent, tint);
            x += glyph.Advance;
            length--;
        }

        return x;
    }

    /// <inheritdoc cref="Draw(Surface, int, int, ReadOnlySpan{byte}, bool, uint?, int)"/>
    public int Draw(Surface destination, int x, int y, string text, bool transparent = true,
                    uint? tint = null, int length = -1) =>
        Draw(destination, x, y, Encode(text), transparent, tint, length);

    /// <summary>
    /// <c>DrawClipped</c> / <c>DrawTransClipped</c>: the same walk, with the destination's clip
    /// rectangle narrowed for the duration.
    /// </summary>
    /// <remarks>
    /// The clip is restored afterwards even if the caller passed something the surface has to
    /// clamp, because leaving a narrowed clip behind would silently truncate whatever drew next —
    /// a bug that surfaces far from its cause.
    /// </remarks>
    public int DrawClipped(Surface destination, int x, int y, ReadOnlySpan<byte> text,
                           SurfaceRect clip, bool transparent = true, uint? tint = null,
                           int length = -1)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var saved = destination.ClipRect;
        try
        {
            destination.ClipRect = clip;
            return Draw(destination, x, y, text, transparent, tint, length);
        }
        finally
        {
            destination.ClipRect = saved;
        }
    }

    /// <inheritdoc cref="DrawClipped(Surface, int, int, ReadOnlySpan{byte}, SurfaceRect, bool, uint?, int)"/>
    public int DrawClipped(Surface destination, int x, int y, string text, SurfaceRect clip,
                           bool transparent = true, uint? tint = null, int length = -1) =>
        DrawClipped(destination, x, y, Encode(text), clip, transparent, tint, length);

    /// <summary>
    /// <c>DrawAligned</c> / <c>DrawAlignedTrans</c> (<c>CDXBitmapFont.cpp:311</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overflow behaviour is the part worth transcribing exactly rather than improving. When
    /// the text is wider than <paramref name="width"/>, the original does not clip mid-glyph and
    /// does not ellipsise: it adds each glyph's width to a running total, stops as soon as that
    /// total <i>exceeds</i> the field, and otherwise draws (<c>CDXBitmapFont.cpp:346-355</c>). A
    /// character landing exactly on the boundary is therefore drawn, not dropped.
    /// </para>
    /// <para>
    /// The genuine quirk is one line earlier: the centred branch is guarded by
    /// <c>TWidth &lt; Width</c>, strictly. Text exactly as wide as its field takes the truncating
    /// path instead of the centring one. Nothing visible follows from it here — the offset would
    /// have been zero and the truncating path draws every character — but it is why the condition
    /// below is <c>&lt;</c> rather than <c>&lt;=</c>.
    /// </para>
    /// <para>
    /// Right alignment has no overflow branch in the original at all — it computes an origin from
    /// the full text width and draws everything, so over-long right-aligned text runs off the left
    /// of its field. Reproduced, because a design's layout may well depend on it.
    /// </para>
    /// </remarks>
    public int DrawAligned(Surface destination, int x, int y, int width, ReadOnlySpan<byte> text,
                           TextAlign align, bool transparent = true, uint? tint = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        int textWidth = GetTextWidth(text);

        switch (align)
        {
            case TextAlign.Center when textWidth < width:
                return Draw(destination, x + ((width - textWidth) / 2), y, text, transparent, tint);

            case TextAlign.Center:
            {
                // Overflowing: draw only what fits, dropping the first character that would push
                // the running total past the field.
                int running = 0;
                int cursor = x;
                foreach (byte character in text)
                {
                    var glyph = Atlas[character];
                    running += glyph.Advance;
                    if (running > width)
                    {
                        break;
                    }

                    DrawGlyph(destination, cursor, y, glyph, transparent, tint);
                    cursor += glyph.Advance;
                }
                return cursor;
            }

            case TextAlign.Right:
                return Draw(destination, x + width - textWidth, y, text, transparent, tint);

            default:
                return Draw(destination, x, y, text, transparent, tint);
        }
    }

    /// <inheritdoc cref="DrawAligned(Surface, int, int, int, ReadOnlySpan{byte}, TextAlign, bool, uint?)"/>
    public int DrawAligned(Surface destination, int x, int y, int width, string text,
                           TextAlign align, bool transparent = true, uint? tint = null) =>
        DrawAligned(destination, x, y, width, Encode(text), align, transparent, tint);

    /// <summary>
    /// Blits one cell, optionally recolouring it on the way.
    /// </summary>
    /// <remarks>
    /// Tinting is what replaces the original's one-atlas-per-colour scheme. A null tint blits the
    /// sheet unchanged, which is the path a rasteriser that already baked the colour in would use.
    /// </remarks>
    private void DrawGlyph(Surface destination, int x, int y, Glyph glyph, bool transparent,
                           uint? tint)
    {
        if (glyph.Width <= 0 || glyph.Height <= 0)
        {
            return;
        }

        if (tint is null)
        {
            if (transparent)
            {
                Blitter.BlitTransparent(destination, x, y, Atlas.Sheet, glyph.Source);
            }
            else
            {
                Blitter.BlitOpaque(destination, x, y, Atlas.Sheet, glyph.Source);
            }
            return;
        }

        TintBlit(destination, x, y, glyph.Source, tint.Value, transparent);
    }

    /// <summary>
    /// Blits a cell with every non-key pixel replaced by <paramref name="colour"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately a flat replacement rather than a multiply. The atlas cells this port targets
    /// are solid-colour glyphs on a keyed background — the shape carries the information, not the
    /// intensity — so multiplying would darken a white glyph toward the tint twice over. A
    /// coverage-based rasteriser that emits antialiased edges will want a weighted blend instead,
    /// which is a change to make when such a rasteriser exists rather than in anticipation of one.
    /// </remarks>
    private void TintBlit(Surface destination, int x, int y, SurfaceRect source, uint colour,
                          bool transparent)
    {
        var sheet = Atlas.Sheet;
        uint key = sheet.ColorKey ?? 0;
        bool keyed = transparent && sheet.ColorKey.HasValue;

        for (int row = 0; row < source.Height; row++)
        {
            int destinationY = y + row;
            if (destinationY < destination.ClipRect.Top || destinationY >= destination.ClipRect.Bottom)
            {
                continue;
            }

            for (int column = 0; column < source.Width; column++)
            {
                int destinationX = x + column;
                if (destinationX < destination.ClipRect.Left ||
                    destinationX >= destination.ClipRect.Right)
                {
                    continue;
                }

                uint pixel = sheet[source.Left + column, source.Top + row];
                if (keyed && pixel == key)
                {
                    continue;
                }

                destination[destinationX, destinationY] = colour | 0xFF000000u;
            }
        }
    }
}
