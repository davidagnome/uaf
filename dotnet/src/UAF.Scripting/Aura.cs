namespace UAF.Scripting;

/// <summary>
/// What shape of ground an aura covers (<c>AURA_SHAPE</c>, <c>Combatants.h:227</c>).
/// </summary>
public enum AuraShape
{
    /// <summary>Covers nothing. Also what an unrecognised shape name falls back to.</summary>
    Null,

    /// <summary>
    /// Named, selectable, and <b>never implemented</b> — <c>DetermineGlobalCoverage</c> is
    /// <c>NotImplemented(0x321abe, false)</c> (<c>Combatants.cpp:8659</c>) and returns without
    /// touching a single cell. See <see cref="AuraCoverage"/>.
    /// </summary>
    Global,

    /// <summary>The only shape that computes anything: a sector of an annulus.</summary>
    AnnularSector,
}

/// <summary>
/// Which combatants can perceive an aura (<c>AURA_WAVELENGTH</c>, <c>Combatants.h:234</c>).
/// </summary>
/// <remarks>
/// <b>It is not decoration: <c>Visible</c> alone forces a recompute on movement.</b>
/// <c>CheckAuraPlacement</c> takes its early exit when a move happened but the wavelength is not
/// visible (<c>Combatants.cpp:8696</c>), so an X-ray or neutrino aura does not re-cover the map
/// merely because somebody walked.
/// </remarks>
public enum AuraWavelength
{
    /// <summary>The default every aura is created with.</summary>
    Visible,

    Xray,

    Neutrino,
}

/// <summary>
/// What an aura's position follows (<c>AURA_ATTACHMENT</c>, <c>Combatants.h:241</c>).
/// </summary>
public enum AuraAttachment
{
    /// <summary>Fixed. Its placement is never rechecked against a combatant.</summary>
    None,

    /// <summary>Follows a combatant's square.</summary>
    Combatant,

    /// <summary>Follows a combatant's square <i>and</i> their facing.</summary>
    CombatantFacing,

    /// <summary>Follows an explicit coordinate, set by <c>$AURA_Location</c>.</summary>
    Xy,
}

/// <summary>
/// The eight-way facing an aura reads off the combatant it is attached to
/// (<c>m_iMoveDir</c>, whose values are the <c>FACE_*</c> enum at <c>Externs.h:1039</c>).
/// </summary>
/// <remarks>
/// <b>These ordinals are not the compass order and not <c>UAFcore</c>'s <c>PathDirection</c>.</b>
/// The reference declares them <c>NORTH, EAST, SOUTH, WEST, NW, NE, SW, SE</c> — the four cardinals
/// first and the diagonals appended afterwards, in an order of their own. A port that casts a
/// compass-ordered direction to an int and calls it a facing gets a working equality test and a
/// silently wrong rotation, which is exactly what
/// <see cref="AnnularCoverage"/> would have inherited.
/// </remarks>
public enum AuraFacing
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
    NorthWest = 4,
    NorthEast = 5,
    SouthWest = 6,
    SouthEast = 7,
}

/// <summary>
/// What a square holds, as an aura's line-of-sight walk cares about it
/// (<c>OBSTICAL_TYPE</c>, <c>Drawtile.h:96</c>).
/// </summary>
/// <remarks>
/// Declared here rather than reused from <c>UAFcore</c>'s <c>ObstacleType</c> because the
/// dependency runs the other way. The values match, and the host maps between them.
/// </remarks>
public enum AuraObstacle
{
    None = 0,
    Wall = 1,
    Occupied = 2,
    OffMap = 3,
    LingeringSpell = 4,
}

/// <summary>
/// One buffer of an aura's placement properties — see <see cref="Aura"/> for why there are two.
/// </summary>
public sealed class AuraProperties
{
    public AuraWavelength Wavelength { get; set; } = AuraWavelength.Visible;

    public AuraShape Shape { get; set; } = AuraShape.Null;

    public int X { get; set; }

    public int Y { get; set; }

    public int CombatantIndex { get; set; }

    public int Size1 { get; set; }

    public int Size2 { get; set; }

    public int Size3 { get; set; }

    public int Size4 { get; set; }

    public string SpellId { get; set; } = string.Empty;

    public AuraAttachment Attachment { get; set; } = AuraAttachment.None;

    /// <summary>Copies every field of <paramref name="from"/> over this one.</summary>
    public void CopyFrom(AuraProperties from)
    {
        ArgumentNullException.ThrowIfNull(from);

        Wavelength = from.Wavelength;
        Shape = from.Shape;
        X = from.X;
        Y = from.Y;
        CombatantIndex = from.CombatantIndex;
        Size1 = from.Size1;
        Size2 = from.Size2;
        Size3 = from.Size3;
        Size4 = from.Size4;
        SpellId = from.SpellId;
        Attachment = from.Attachment;
    }
}

/// <summary>
/// A region of a combat map that runs scripts on whoever walks into it
/// (<c>struct AURA</c>, <c>Combatants.h:290</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every placement property is double-buffered, and every script writes the back buffer.</b> The
/// reference declares them as two-element arrays with the comment "[0] is the current value, [1] is
/// the new value", and all thirteen setter opcodes assign <c>[1]</c>. Nothing a script does takes
/// effect until <c>CheckAuraPlacement</c> compares the buffers, decides whether anything actually
/// moved, and copies <c>[1]</c> over <c>[0]</c>. <b>Reading a property back inside the same script
/// therefore cannot be done at all</b> — there is no getter for any of them, only
/// <c>$AURA_GetData</c> for the ten user slots.
/// </para>
/// <para>
/// <b><see cref="Facing"/> is the exception: one value, not two.</b> It is never written by a
/// script — only by the placement check, copying it off the combatant the aura is attached to — so
/// there is nothing to buffer.
/// </para>
/// <para>
/// <b>The ten user-data slots are the only script-readable state an aura has</b>, and the first
/// three are filled in by <c>$AURA_Create</c> from its 3rd, 4th and 5th arguments. There is no
/// bounds check on the index in either direction (<c>GPDLexec.cpp:1143</c>).
/// </para>
/// <para>
/// <b>The cell mask is one byte per square of the whole combat map, and only bit 0 is read.</b>
/// <c>MAX_TERRAIN_WIDTH</c> × <c>MAX_TERRAIN_HEIGHT</c> bytes, allocated per aura, tested as
/// <c>cells[i] &amp; 1</c>. The other seven bits are written by nothing.
/// </para>
/// </remarks>
public sealed class Aura
{
    /// <summary>How many user-data slots an aura has (<c>userData[10]</c>).</summary>
    public const int UserDataSlots = 10;

    public Aura(int id, int cellCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellCount);

        Id = id;
        Cells = new byte[cellCount];
    }

    /// <summary>
    /// Its identity (<c>auraID</c>), handed out by <see cref="AuraStore"/> and never reused.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The aura's own special abilities, which are what its scripts live in.
    /// </summary>
    /// <remarks>
    /// <b>Writable, unlike an item's or a monster's.</b> An aura is a live object rather than a
    /// database record, so <c>$AURA_AddSA</c> and <c>$AURA_RemoveSA</c> both work — which is the
    /// whole point of the family.
    /// </remarks>
    public SpecabList Abilities { get; } = new(readOnly: false);

    /// <summary>The ten slots, as strings. Slots 0..2 are seeded by <c>$AURA_Create</c>.</summary>
    public string[] UserData { get; } = CreateUserData();

    /// <summary>
    /// One byte per combat-map square; bit 0 set means the aura covers it.
    /// </summary>
    public byte[] Cells { get; }

    /// <summary>
    /// Which combatants were inside as of the last placement check, by index.
    /// </summary>
    /// <remarks>
    /// <b>This is the membership the enter/exit scripts are driven from</b>, not a derived view of
    /// <see cref="Cells"/>: a combatant is "in" precisely when this list says so, and the check
    /// reconciles the two.
    /// </remarks>
    public List<int> Combatants { get; } = [];

    /// <summary>The committed properties — <c>[0]</c>. What the aura currently <i>is</i>.</summary>
    public AuraProperties Current { get; } = new();

    /// <summary>The pending properties — <c>[1]</c>. What every setter opcode writes.</summary>
    public AuraProperties Pending { get; } = new();

    /// <summary>
    /// The facing copied off an attached combatant, single-buffered. See the class remarks.
    /// </summary>
    public AuraFacing Facing { get; set; }

    private static string[] CreateUserData()
    {
        var slots = new string[UserDataSlots];
        Array.Fill(slots, string.Empty);
        return slots;
    }
}
