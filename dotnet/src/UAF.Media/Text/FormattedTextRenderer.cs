namespace UAF.Media;

/// <summary>
/// The RGB each named colour resolves to, before a design overrides it.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>FONT_LIBRARY::LoadFonts</c> (<c>Shared/GlobalData.cpp:6088</c>), which is
/// the table every design starts from; <c>SetFontColor</c> lets one replace individual entries, so
/// this is a default rather than a constant.
/// </para>
/// <para>
/// <b><see cref="FontColor.BrightOrange"/> is not brighter than <see cref="FontColor.Orange"/></b>
/// — both are 255,128,0 in the shipped table. The two are distinct <i>tags</i> (<c>/O</c> and
/// <c>/T</c>) that happen to resolve identically until a design separates them, so they are kept
/// as separate entries rather than collapsed.
/// </para>
/// </remarks>
public static class FontPalette
{
    private static readonly uint[] Colors =
    [
        0xFFFFFFFF,   // White
        0xFFFFFF00,   // Yellow
        0xFFFF8000,   // Orange
        0xFFFF8000,   // BrightOrange -- deliberately the same as Orange
        0xFFFF0000,   // Red
        0xFF00FF00,   // Green
        0xFF8080FF,   // Blue        -- a light blue, not pure
        0xFF00FFFF,   // Cyan
        0xFF000000,   // Black
        0xFFFF00FF,   // Magenta
        0xFFC0C0C0,   // Silver
    ];

    /// <summary>The default ARGB for a named colour.</summary>
    public static uint Resolve(FontColor color)
    {
        int index = (int)color;
        return (uint)index < (uint)Colors.Length ? Colors[index] : Colors[0];
    }
}

/// <summary>
/// Draws wrapped, marked-up text — the counterpart to <see cref="TextFormatter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Wrapping and drawing both run the same scanner over the same bytes, which is how the original
/// works too: lines keep their markup after wrapping, and each one re-establishes its colour from
/// the preamble <see cref="FormattedTextScanner.GetString"/> put at its head. Nothing has to be
/// carried between lines.
/// </para>
/// <para>
/// <b>Colour is a draw-time tint, not eleven atlases.</b> The original rasterises a separate font
/// per colour because GDI baked the colour in at <c>TextOut</c> time
/// (<c>GlobalData.cpp:5964-5975</c>); a managed blitter has no such constraint, and tinting one
/// atlas avoids the wrong-hued fringes the original's non-white faces carry.
/// </para>
/// </remarks>
public static class FormattedTextRenderer
{
    /// <summary>
    /// Draws one already-wrapped line, applying its tags.
    /// </summary>
    /// <returns>The X coordinate one past the last glyph drawn.</returns>
    /// <remarks>
    /// Characters are flushed in runs and only when the colour changes, so a line with no tags in
    /// it is a single blit walk — the same cost as <see cref="BitmapFont.Draw"/>.
    /// </remarks>
    public static int DrawLine(Surface destination, BitmapFont font, int x, int y,
                               ReadOnlySpan<byte> line, Func<FontColor, uint>? palette = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(font);

        palette ??= FontPalette.Resolve;

        var scanner = new FormattedTextScanner();
        scanner.Initialize(line.ToArray(), 99999, (byte)'W', FontColor.White, 0, 0);

        var run = new List<byte>(line.Length);
        var runColor = FontColor.White;

        void Flush()
        {
            if (run.Count > 0)
            {
                x = font.Draw(destination, x, y, System.Runtime.InteropServices.CollectionsMarshal
                                                       .AsSpan(run),
                              transparent: true, tint: palette(runColor));
                run.Clear();
            }
        }

        FormattedTextStatus status;
        while ((status = scanner.NextChar()) != FormattedTextStatus.EndOfText)
        {
            if (status != FormattedTextStatus.Printable)
            {
                // Any tag can change the colour, so the accumulated run is committed before it
                // takes effect. Wrapping has already removed the line breaks, and a wait tag ends
                // the line rather than appearing inside one.
                continue;
            }

            if (scanner.CurrentColor != runColor)
            {
                Flush();
                runColor = scanner.CurrentColor;
            }

            run.Add(scanner.CurrentCharacter);
        }

        Flush();
        return x;
    }

    /// <summary>
    /// Draws the box currently selected in <paramref name="data"/> at the given origin.
    /// </summary>
    /// <param name="lineHeight">
    /// Vertical step per line. The original spaces lines by the font's tallest glyph, which is what
    /// <see cref="TextBoxMetrics.ForFont"/> derives the line count from, so the two agree.
    /// </param>
    /// <returns>The number of lines drawn.</returns>
    public static int DrawBox(Surface destination, BitmapFont font, TextDisplayData data,
                              int x, int y, int lineHeight = 0,
                              Func<FontColor, uint>? palette = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(data);

        int step = lineHeight > 0 ? lineHeight : font.Atlas.MaxCharHeight;
        int drawn = 0;

        foreach (var line in data.CurrentBox())
        {
            DrawLine(destination, font, x, y + (drawn * step), line.Text, palette);
            drawn++;
        }

        return drawn;
    }
}
