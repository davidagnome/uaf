namespace UAFedit.Spells;

/// <summary>
/// The words the original editor put on a spell's enumerated fields.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every list here is indexed by the value stored in the record.</b> The reference populates each
/// combo in enumerator order and reads the choice back with a raw cast of <c>GetCurSel()</c>
/// (<c>UAFWinEd/SpellDBDlgEx.cpp:735</c>), so the ordinal <i>is</i> the field — there is no lookup
/// table between them. A view can therefore bind <c>SelectedIndex</c> straight to the field, and
/// that is what makes these plain string lists rather than value/label pairs.
/// </para>
/// <para>
/// <b>The enumerators are not in a sensible order and must not be tidied.</b>
/// <c>SelectByHitDice</c> is 5, wedged between the circle and the line variants
/// (<c>Shared/GameRules.h:299</c>); sorting these lists, or grouping the area shapes together,
/// silently rewrites every spell in the design.
/// </para>
/// <para>
/// Strings are as the reference shows them (<c>UAFWinEd/Globtext.cpp</c>), not modernised: a
/// designer reading the original manual has to find the same words.
/// </para>
/// </remarks>
public static class SpellChoices
{
    /// <summary><c>spellTargetingType</c> (<c>GameRules.h:299</c>, <c>Globtext.cpp:368</c>).</summary>
    public static IReadOnlyList<string> Targeting { get; } =
    [
        "Self",
        "Selected by Count",
        "Whole Party",
        "Touched Targets",
        "Area: Circle",
        "Selected by Hit Dice",
        "Area: Line, Pick Start",
        "Area: Line, Pick End",
        "Area: Square",
        "Area: Cone",
    ];

    /// <summary><c>spellSaveVersusType</c> (<c>GameRules.h:330</c>, <c>Globtext.cpp:339</c>).</summary>
    public static IReadOnlyList<string> SaveVersus { get; } =
    [
        "Paralysis/Poison/Death Magic",
        "Petrification/Polymorph",
        "Rod/Staff/Wand",
        "Spell",
        "Breath Weapon",
    ];

    /// <summary><c>spellSaveEffectType</c> (<c>GameRules.h:327</c>, <c>Globtext.cpp:347</c>).</summary>
    public static IReadOnlyList<string> SaveResult { get; } =
    [
        "No Save",
        "Save Negates",
        "Save for Half",
        "Use Player THAC0",
    ];

    /// <summary><c>spellCastingTimeType</c> (<c>GameRules.h:336</c>, <c>Globtext.cpp:382</c>).</summary>
    public static IReadOnlyList<string> CastingTimeType { get; } =
        ["Immediate", "Initiative", "Rounds", "Turns"];

    /// <summary><c>spellDurationType</c> (<c>GameRules.h:333</c>, <c>Globtext.cpp:359</c>).</summary>
    public static IReadOnlyList<string> DurationRate { get; } =
    [
        "in rounds",
        "by damage taken",
        "in hours",
        "in days",
        "permanent",
        "by nbr attacks",
    ];

    /// <summary>The four stages a spell's art and sound both step through.</summary>
    /// <remarks>
    /// The record keeps <c>CastArt</c> and <c>CastSound</c> apart from the arrays — they are written
    /// at different points in the stream — but the dialog draws all five as one grid of
    /// Animation/Sound rows (<c>UAFWinEd.rc:3101</c>), so these are the labels for
    /// <c>Art[0..3]</c> and <c>Sounds[0..3]</c> only.
    /// </remarks>
    public static IReadOnlyList<string> Stages { get; } = ["In-Route", "Coverage", "Hit", "Linger"];

    /// <summary>The seven script slots, named and ordered as the script dropdown lists them.</summary>
    /// <remarks>
    /// <b>The dialog's order is not the wire order and the difference is not cosmetic.</b>
    /// <c>SpellScriptSlot</c> follows the stream, where Initiation and Termination sit at 2 and 3;
    /// the dropdown lists them <i>last</i>, after the three saving-throw scripts
    /// (<c>SpellDBDlgEx.cpp:1683</c>). Driving a picker off the enum's own order therefore puts two
    /// entries in the wrong place for anyone who knew the original.
    /// </remarks>
    public static IReadOnlyList<UAF.Serialization.SpellScriptSlot> ScriptOrder { get; } =
    [
        UAF.Serialization.SpellScriptSlot.Begin,
        UAF.Serialization.SpellScriptSlot.End,
        UAF.Serialization.SpellScriptSlot.SavingThrow,
        UAF.Serialization.SpellScriptSlot.SavingThrowSucceeded,
        UAF.Serialization.SpellScriptSlot.SavingThrowFailed,
        UAF.Serialization.SpellScriptSlot.Initiation,
        UAF.Serialization.SpellScriptSlot.Termination,
    ];

    /// <summary>What the dropdown calls one script slot (<c>SpellDBDlgEx.cpp:1683</c>).</summary>
    public static string ScriptName(UAF.Serialization.SpellScriptSlot slot) => slot switch
    {
        UAF.Serialization.SpellScriptSlot.Begin => "Spell Begin Script",
        UAF.Serialization.SpellScriptSlot.End => "Spell End Script",
        UAF.Serialization.SpellScriptSlot.SavingThrow => "Saving Throw Script",
        UAF.Serialization.SpellScriptSlot.SavingThrowSucceeded => "Saving Throw Succeeded Script",
        UAF.Serialization.SpellScriptSlot.SavingThrowFailed => "Saving Throw Failed Script",
        UAF.Serialization.SpellScriptSlot.Initiation => "Spell Initiation Script",
        UAF.Serialization.SpellScriptSlot.Termination => "Spell Termination Script",
        _ => slot.ToString(),
    };

    /// <summary>A label from one of these lists, or a readable placeholder for a stray value.</summary>
    /// <remarks>
    /// Designs do carry values outside the enum — nothing validates these on the way in — and a
    /// list column that showed a blank for one would hide it. The number is kept visible so the
    /// designer can see what is actually stored.
    /// </remarks>
    public static string Label(IReadOnlyList<string> choices, int value)
    {
        ArgumentNullException.ThrowIfNull(choices);

        return (uint)value < (uint)choices.Count ? choices[value] : $"? ({value})";
    }

    /// <summary>The <c>IN_CAMP</c> bit of <c>Restrictions</c> (<c>Shared/Spell.h:404</c>).</summary>
    public const int RestrictionInCamp = 0x01;

    /// <summary>The <c>IN_COMBAT</c> bit of <c>Restrictions</c> (<c>Shared/Spell.h:405</c>).</summary>
    public const int RestrictionInCombat = 0x02;

    /// <summary>
    /// What the six dice parameters are called for a given targeting type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Five of the six parameters have no fixed meaning — the targeting type decides what each
    /// one is.</b> The resource file carries only the placeholders "P1".."P5"
    /// (<c>UAFWinEd.rc:3096</c>) and <c>DoDataExchange</c> rewrites every label whenever the
    /// targeting combo changes (<c>SpellDBDlgEx.cpp:268</c> onwards). So <c>Parameters[1]</c> is a
    /// <i>Quantity</i> for a circle and a <i>Width</i> for a cone, and an editor that prints fixed
    /// headings is telling the designer something false about half the database.
    /// </para>
    /// <para>
    /// <b>An empty label means the field is disabled for that targeting type</b>, which the
    /// reference greys out rather than hiding. P4 and P5 are empty for all ten types — they are
    /// edit boxes with no reachable meaning, kept only because the record carries them.
    /// </para>
    /// <para>
    /// Index 0 is <c>Duration</c>, which does have a fixed meaning and is drawn outside the
    /// targeting group (<c>rc:3086</c>).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ParameterLabels(int targeting) => targeting switch
    {
        0 => ["Duration", "", "", "", "", ""],                                  // Self
        1 => ["Duration", "Quantity", "", "Range", "", ""],                     // Selected by Count
        2 => ["Duration", "", "", "", "", ""],                                  // Whole Party
        3 => ["Duration", "Quantity", "", "", "", ""],                          // Touched Targets
        4 => ["Duration", "Quantity", "Radius", "Range", "", ""],               // Area: Circle
        5 => ["Duration", "Number", "", "Range", "", ""],                       // By Hit Dice
        6 or 7 => ["Duration", "Width", "Length", "Range", "", ""],             // Area: Line
        8 => ["Duration", "Width", "Height", "Range", "", ""],                  // Area: Square
        9 => ["Duration", "Width", "Length", "Range", "", ""],                  // Area: Cone
        _ => ["Duration", "P1", "P2", "P3", "P4", "P5"],
    };
}
