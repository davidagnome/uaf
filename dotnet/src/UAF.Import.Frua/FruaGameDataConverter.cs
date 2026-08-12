using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// Overlays a DOS FRUA design's header onto an existing UAF design.
/// </summary>
/// <remarks>
/// <para>
/// <b>An import mutates a design; it does not build one.</b> That is how the reference works and
/// it is not an implementation detail — <c>ImportGameDat</c> assigns into the live
/// <c>globalData</c>, and the editor has a design open when the menu item runs. It is also why
/// <c>-config</c> takes a design <i>directory</i> and why <c>frua-import-oracle.sh</c> seeds a
/// scratch copy of <c>DefaultDesign</c> before invoking it.
/// </para>
/// <para>
/// <b>The alternative does not work.</b> <see cref="GlobalStatsWriter.CanWrite"/> requires a money
/// table and a difficulty table, and says of the first that there is no default to invent: it
/// decides what every coin in the design is worth. FRUA carries neither, so a header built from
/// nothing could not be written at all. Overlaying onto a template keeps them.
/// </para>
/// </remarks>
public static class FruaGameDataConverter
{
    /// <summary>Substituted for a blank design name, as the reference does.</summary>
    public const string NoName = "NoName FRUA Design";

    /// <summary>
    /// Returns <paramref name="template"/> with the FRUA design's header applied.
    /// </summary>
    /// <param name="template">
    /// An existing design's globals — normally <c>DefaultDesign</c>'s — supplying everything FRUA
    /// has no equivalent for: the money and difficulty tables, fonts, art and title screens.
    /// </param>
    /// <param name="game">The FRUA <c>game001.dat</c>.</param>
    /// <param name="startLevel">
    /// The level the party begins on, and the one whose entry point supplies the start position.
    /// Null leaves the template's position alone.
    /// </param>
    public static GlobalStatsPrefix Apply(GlobalStatsPrefix template, FruaGameData game,
                                          FruaLevel? startLevel = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(game);

        var start = StartPosition(game, startLevel);

        return template with
        {
            DesignName = game.DesignName,

            // FRUA stores these one-based and FruaGameData has already decremented them.
            StartLevel = game.StartLevel,
            StartX = start.X,
            StartY = start.Y,
            StartFacing = start.Facing,

            StartExp = (int)game.StartExperience,

            // START_EXP_VALUE: the reference sets the type alongside the value, because an
            // imported design always states an amount rather than deriving one from a level.
            StartExpType = StartExpValue,

            StartPlatinum = (int)game.StartPlatinum,
            StartGem = (int)game.StartGems,
            StartJewelry = (int)game.StartJewelry,

            Keys = Objects(game.SpecialKeys, template.Keys),
            SpecialItems = Objects(game.SpecialItems, template.SpecialItems),
        };
    }

    /// <summary>
    /// <c>START_EXP_VALUE</c> — the experience is an amount, not a level lookup.
    /// </summary>
    public const int StartExpValue = 0;

    /// <summary>
    /// Where the party starts: the chosen entry point of the starting level.
    /// </summary>
    /// <remarks>
    /// <b>The reference clamps the position into the level.</b> An entry point outside the map is
    /// pulled to its edge rather than refused (<c>OnImportfruadesign</c>), which matters because a
    /// design may name an entry point the level does not have.
    /// </remarks>
    private static (byte X, byte Y, byte Facing) StartPosition(FruaGameData game, FruaLevel? level)
    {
        if (level is null)
        {
            return (0, 0, 0);
        }

        int index = Math.Clamp(game.StartExperienceProfile, 0, level.EntryPoints.Count - 1);
        var entry = level.EntryPoints[index];

        return ((byte)Math.Clamp(entry.X, 0, level.Width - 1),
                (byte)Math.Clamp(entry.Y, 0, level.Height - 1),
                (byte)(entry.Facing == FruaFacing.Unknown ? 0 : (int)entry.Facing));
    }

    /// <summary>
    /// FRUA's named keys and items, overlaid onto the template's slots.
    /// </summary>
    /// <remarks>
    /// The identifiers and per-object attributes come from the template, since FRUA has only a
    /// name; a design with fewer template slots than FRUA has names simply takes the ones it can.
    /// </remarks>
    private static IReadOnlyList<SpecialObject> Objects(IReadOnlyList<string> names,
                                                        IReadOnlyList<SpecialObject> template)
    {
        var objects = new List<SpecialObject>(names.Count);

        for (int i = 0; i < names.Count; i++)
        {
            objects.Add(i < template.Count
                ? template[i] with { Name = names[i] }
                : new SpecialObject(names[i], i + 1, 0, 0, string.Empty, 1, []));
        }

        return objects;
    }
}
