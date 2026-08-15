namespace Rc2Axaml;

/// <summary>
/// Dialog-unit to pixel conversion, the same arithmetic Win32 <c>MapDialogRect</c> performs.
/// </summary>
/// <remarks>
/// <para>
/// A dialog resource stores every coordinate in <i>dialog units</i>, which are a fraction of the
/// dialog font's character cell rather than pixels: one horizontal DLU is a quarter of the average
/// character width, one vertical DLU is an eighth of the character height. So
/// </para>
/// <code>
/// px_x = dlu_x * baseUnitX / 4
/// px_y = dlu_y * baseUnitY / 8
/// </code>
/// <para>
/// <b>The base units come from the dialog's own font, not from the system.</b> Every dialog in
/// UAFWinEd.rc sets <c>DS_SETFONT</c> and names its font, so <c>GetDialogBaseUnits()</c> — which
/// answers for the old 8x16 System font — is the wrong source and would stretch every dialog by a
/// third. For <b>MS Sans Serif 8pt at 96 DPI</b>, the font used by 128 of the 131 dialogs, the
/// average character width is 6 pixels and the character height (<c>tmHeight</c>) is 13. Those are
/// the classic values behind the familiar "4 DLU = 6 px, 8 DLU = 13 px" rule of thumb, and they are
/// what the MFC dialog editor drew this file against.
/// </para>
/// <para>
/// <b>Truncating integer division is deliberate.</b> <c>MapDialogRect</c> works in integers, so
/// 7 DLU maps to 10 px (7*6/4 = 10.5 truncated), not 11. Rounding instead would put controls one
/// pixel away from where the original editor showed them, and since sizes are derived from mapped
/// edges (below) the error would compound.
/// </para>
/// <para>
/// <b>Sizes are the difference of two mapped edges, not a mapped size.</b> Windows maps the whole
/// rectangle — left, top, right, bottom — and the control's width is <c>right - left</c>. Mapping
/// <c>cx</c> on its own gives a different answer whenever the truncations fall differently: a
/// control at x=7 cx=7 occupies 10..21, i.e. 11 px wide, where <c>7*6/4</c> alone says 10. This is
/// why <see cref="MapRect"/> exists rather than a pair of scalar helpers.
/// </para>
/// </remarks>
public readonly record struct DialogUnits(int BaseUnitX, int BaseUnitY)
{
    /// <summary>MS Sans Serif 8pt at 96 DPI: 6 px average character width, 13 px cell height.</summary>
    public static readonly DialogUnits MsSansSerif8 = new(6, 13);

    /// <summary>
    /// MS Sans Serif 10pt at 96 DPI. One dialog — <c>IDD_GAMEVERSION</c>, UAFWinEd.rc:2485 — uses
    /// it, presumably by accident, since nothing about that dialog wants a bigger font.
    /// </summary>
    public static readonly DialogUnits MsSansSerif10 = new(8, 16);

    /// <summary>
    /// Base units for a dialog's <c>FONT</c> statement.
    /// </summary>
    /// <remarks>
    /// <c>MS Shell Dlg</c> is not a font: it is an alias the shell resolves per locale, and on the
    /// English systems this editor was built for it resolves to MS Sans Serif. The single dialog
    /// that names it (<c>IDD_FlowControl</c>) therefore gets the same units as its neighbours.
    /// Anything else falls back to the 8pt metrics and reports why, rather than inventing numbers.
    /// </remarks>
    public static DialogUnits ForFont(RcFont font, IList<string>? diagnostics = null)
    {
        bool sansSerif =
            font.Face.Equals("MS Sans Serif", StringComparison.OrdinalIgnoreCase) ||
            font.Face.Equals("MS Shell Dlg", StringComparison.OrdinalIgnoreCase);

        if (sansSerif)
        {
            switch (font.PointSize)
            {
                case 8: return MsSansSerif8;
                case 10: return MsSansSerif10;
            }
        }

        diagnostics?.Add(
            $"no measured base units for FONT {font.PointSize}, \"{font.Face}\" — " +
            "using MS Sans Serif 8pt metrics (6 x 13)");
        return MsSansSerif8;
    }

    /// <summary>
    /// Maps a control rectangle from dialog units to pixels, edge by edge, as Windows does.
    /// </summary>
    /// <returns>Left, top, width and height in device-independent pixels at 96 DPI.</returns>
    public (int Left, int Top, int Width, int Height) MapRect(int x, int y, int cx, int cy)
    {
        int left = x * BaseUnitX / 4;
        int top = y * BaseUnitY / 8;
        int right = (x + cx) * BaseUnitX / 4;
        int bottom = (y + cy) * BaseUnitY / 8;
        return (left, top, right - left, bottom - top);
    }

    /// <summary>Maps a vertical extent measured from the dialog's origin.</summary>
    public int MapY(int dlu) => dlu * BaseUnitY / 8;
}
