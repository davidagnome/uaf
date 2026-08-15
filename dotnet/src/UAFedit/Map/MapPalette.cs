using UAF.Data;
using UAFcore;

namespace UAFedit.Map;

/// <summary>An editor palette entry. Opaque by construction — the 2-D map has no alpha.</summary>
public readonly record struct MapColor(byte R, byte G, byte B)
{
    /// <summary>Packed <c>0xRRGGBB</c>, for tests and for the control's brush cache.</summary>
    public int Packed => (R << 16) | (G << 8) | B;
}

/// <summary>
/// The colours the 2-D map draws with — a design's <c>EDITOR_COLOR_<i>n</i></c> table
/// (<c>Shared/Globals.cpp:2471</c>) plus the four ways the editor indexes into it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The table is 192 entries and only sixteen are ever written.</b> <c>MAX_COLOR_SLOTS</c> is
/// <c>MAX_WALLSETS</c> (<c>Externs.h:864</c>), but every shipped <c>config.txt</c> declares
/// <c>EDITOR_COLOR_1</c>…<c>_16</c> and stops. The remaining 176 stay at their static-initialiser
/// value, which is black — the same black the empty cell is filled with. So in the original a
/// design that used wall slot 20 drew that wall <i>invisibly</i>.
/// </para>
/// <para>
/// <b>That is not hypothetical.</b> <c>Case</c>'s first level places 1,155 walls drawn from 48
/// different slots, 44 of them <i>above</i> 15 — <b>282 of its walls, a quarter of the level, are
/// black on black in the original editor</b>. So the faithful palette is reproduced and is the
/// default, and <see cref="FromConfig"/> takes a flag to fill the undeclared slots instead. The
/// choice is the caller's because the two answers are both defensible: matching what the design's
/// author saw, or showing the author what they actually drew.
/// </para>
/// <para>
/// <b>The four accessors are not four palettes.</b> <c>GetWallColor</c>, <c>GetBackdropColor</c>,
/// <c>GetZoneColor</c> and <c>GetEPColor</c> (<c>UAFWinEd/UAFWinEd.cpp:105-165</c>) are the same
/// function under four names, all returning <c>m_Colors[slot]</c>. Only
/// <see cref="Obstruction"/> permutes. They are kept apart here because the call sites mean
/// different things by their argument, and collapsing them loses the only documentation of which
/// index space is which.
/// </para>
/// <para>
/// Every index is clamped to slot 0 rather than rejected, which is the original's behaviour and is
/// why a level referencing a wall set the design does not have still draws.
/// </para>
/// </remarks>
public sealed class MapPalette
{
    /// <summary><c>MAX_COLOR_SLOTS</c> (<c>Externs.h:864</c>).</summary>
    public const int Slots = 192;

    /// <summary>How many <c>EDITOR_COLOR_<i>n</i></c> keys a shipped config actually declares.</summary>
    public const int DeclaredSlots = 16;

    /// <summary>The wall-set index that means "no wall" and is never drawn.</summary>
    public const int NoWall = 0;

    private readonly MapColor[] colors;
    private readonly bool[] configured;

    private MapPalette(MapColor[] colors, bool[] configured)
    {
        this.colors = colors;
        this.configured = configured;
    }

    /// <summary>
    /// The sixteen colours every shipped design declares, in slot order.
    /// </summary>
    /// <remarks>
    /// <b>The reference has no defaults at all</b> — <c>m_Colors</c> is a bare global and a design
    /// whose config omitted the keys would draw an entirely black map. These are used as the
    /// fallback anyway, because they are not a guess: <c>SomethingWild</c>, <c>Case</c>,
    /// <c>Ambassador's_Letter</c> and <c>dc-default</c> all declare byte-identical tables. It is
    /// the EGA sixteen.
    /// </remarks>
    public static IReadOnlyList<MapColor> DefaultColors { get; } =
    [
        new(0, 0, 0),        // 0  black — the empty cell, and "no wall"
        new(255, 0, 0),      // 1  red
        new(0, 255, 0),      // 2  green
        new(0, 0, 255),      // 3  blue
        new(255, 255, 0),    // 4  yellow
        new(0, 255, 255),    // 5  cyan
        new(255, 0, 255),    // 6  magenta
        new(128, 0, 0),      // 7  maroon — the cell fill in zone and entry-point modes
        new(0, 128, 0),      // 8  dark green
        new(0, 0, 128),      // 9  navy
        new(128, 128, 0),    // 10 olive
        new(0, 128, 128),    // 11 teal
        new(128, 0, 128),    // 12 purple
        new(128, 128, 128),  // 13 grey
        new(192, 192, 192),  // 14 silver — Blocked
        new(255, 255, 255),  // 15 white  — FalseDoor, grid corners, event and start markers
    ];

    /// <summary>The built-in palette: <see cref="DefaultColors"/> then black to slot 191.</summary>
    public static MapPalette Default { get; } = Build(null, fillUndeclared: false);

    /// <summary>
    /// Reads <c>EDITOR_COLOR_1</c>… from a design's config, falling back per slot.
    /// </summary>
    /// <param name="fillUndeclared">
    /// Whether to give the slots a config declared nothing for a generated colour rather than the
    /// original's black. Off by default — see the type remarks for why this is a real question and
    /// not a tidy-up.
    /// </param>
    /// <remarks>
    /// <b>The key is one-based and the slot is zero-based</b> (<c>Globals.cpp:2469</c>), so
    /// <c>EDITOR_COLOR_1</c> is the black at slot 0 and <c>EDITOR_COLOR_16</c> the white at 15. Off
    /// by one here shifts every wall colour and, worse, silently makes the "no wall" slot visible.
    /// </remarks>
    public static MapPalette FromConfig(DesignConfig? config, bool fillUndeclared = false) =>
        Build(config, fillUndeclared);

    private static MapPalette Build(DesignConfig? config, bool fillUndeclared)
    {
        var colors = new MapColor[Slots];
        var configured = new bool[Slots];

        for (int slot = 0; slot < DefaultColors.Count; slot++)
        {
            colors[slot] = DefaultColors[slot];
            configured[slot] = true;
        }

        if (config is not null)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                // Peeked rather than consumed: DesignConfig hands a token out once by default and
                // the editor may well rebuild the palette when a design is reloaded.
                if (config.TryGetInts($"EDITOR_COLOR_{slot + 1}", out int[] rgb, count: 3,
                                      consume: false))
                {
                    colors[slot] = new MapColor(Clamp(rgb[0]), Clamp(rgb[1]), Clamp(rgb[2]));
                    configured[slot] = true;
                }
            }
        }

        if (fillUndeclared)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                if (!configured[slot])
                {
                    colors[slot] = Generated(slot);
                }
            }
        }

        return new MapPalette(colors, configured);

        static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
    }

    /// <summary>
    /// A colour for a slot nobody declared: a hue ramp, never black and never the marker white.
    /// </summary>
    /// <remarks>
    /// The golden-angle step keeps successive slots far apart in hue, so a level using a run of
    /// them — which is how designs use them — reads as distinct walls rather than a gradient. This
    /// has no counterpart in the original; it is a colour for something that had none.
    /// </remarks>
    private static MapColor Generated(int slot)
    {
        double hue = (slot * 137.508) % 360.0;

        // Value alternates so that two slots landing on a similar hue still differ, and both stay
        // clear of the black the empty cell is filled with.
        double value = (slot % 2) == 0 ? 0.85 : 0.55;

        double c = value * 0.75;
        double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        double m = value - c;

        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new MapColor(Channel(r + m), Channel(g + m), Channel(b + m));

        static byte Channel(double v) => (byte)Math.Clamp(v * 255, 0, 255);
    }

    /// <summary>Whether a slot was actually declared, rather than left at the static black.</summary>
    public bool IsConfigured(int slot) => slot >= 0 && slot < Slots && configured[slot];

    /// <summary>The colour of a wall set's slot (<c>GetWallColor</c>).</summary>
    public MapColor Wall(int slot) => At(slot);

    /// <summary>The colour of a background slot (<c>GetBackdropColor</c>). Same table.</summary>
    public MapColor Backdrop(int slot) => At(slot);

    /// <summary>The colour of a zone (<c>GetZoneColor</c>). Same table.</summary>
    public MapColor Zone(int slot) => At(slot);

    /// <summary>The colour of an entry point (<c>GetEPColor</c>). Same table.</summary>
    public MapColor EntryPoint(int slot) => At(slot);

    /// <summary>
    /// The colour of a blockage (<c>GetObstructionColor</c>, <c>UAFWinEd.cpp:127</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only accessor that is not a straight lookup.</b> Blockage 2
    /// (<see cref="BlockageType.Blocked"/>) maps to slot 14 and blockage 3
    /// (<see cref="BlockageType.FalseDoor"/>) to slot 15 — the two brightest colours, because those
    /// are the two the author most needs to see — and everything from 4 up is shifted down two, so
    /// the eight keyed locks land on the mid-tones at slots 2 to 13 rather than colliding with
    /// them.
    /// </para>
    /// <para>
    /// Blockage 0 (<see cref="BlockageType.Open"/>) maps to slot 0, which is the same black the
    /// cell is filled with. That is not an oversight in either place: <c>DrawSquare</c> skips the
    /// obstruction mark entirely when the blockage is <c>OpenBlk</c>, so the colour is never asked
    /// for. It answers anyway, and this does too.
    /// </para>
    /// </remarks>
    public MapColor Obstruction(BlockageType blockage) => Obstruction((int)blockage);

    /// <inheritdoc cref="Obstruction(BlockageType)"/>
    public MapColor Obstruction(int blockage) => blockage switch
    {
        < 0 or >= Slots => At(0),
        0 => At(0),
        1 => At(1),
        2 => At(14),
        3 => At(15),
        _ => At(blockage - 2),
    };

    private MapColor At(int slot) =>
        colors[slot < 0 || slot >= Slots ? 0 : slot];
}
