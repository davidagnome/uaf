namespace UAF.Scripting;

/// <summary>
/// The nine alignments, by the ordinal a character stores (<c>alignmentType</c>,
/// <c>GameRules.h:112</c>).
/// </summary>
/// <remarks>
/// <b>The order is by moral axis first, then ethical</b> — all three Goods, then all three
/// Neutrals, then all three Evils. Not the grid a player pictures, and the reason the five
/// predicates are written as membership tests rather than arithmetic on the ordinal.
/// </remarks>
public enum GpdlAlignmentValue
{
    LawfulGood = 0,
    NeutralGood = 1,
    ChaoticGood = 2,
    LawfulNeutral = 3,
    TrueNeutral = 4,
    ChaoticNeutral = 5,
    LawfulEvil = 6,
    NeutralEvil = 7,
    ChaoticEvil = 8,
}

/// <summary>
/// One of the five questions a script can ask about an alignment.
/// </summary>
/// <remarks>
/// <c>$AlignmentGood</c> and its four siblings. They are not a partition: an alignment can answer
/// yes to two of them, and <see cref="Neutral"/> overlaps every other one — see
/// <see cref="GpdlAlignment.Is"/>.
/// </remarks>
public enum GpdlAlignmentTest
{
    Good,
    Evil,
    Lawful,
    Neutral,
    Chaotic,
}

/// <summary>
/// The character sheet's text tables, and what the six <c>$Alignment*</c> calls make of an
/// alignment.
/// </summary>
/// <remarks>
/// <para>
/// <b>One home for these, shared with the character sheet.</b> The names are the reference's
/// <c>CharAlignmentTypeText</c>, <c>CharStatusTypeText</c> and <c>CharGenderTypeText</c>
/// (<c>UAFWin/CharStatsForm.cpp:54</c>), and both the sheet and the scripting calls print exactly
/// them — so a second copy would be a pair of tables free to drift apart.
/// </para>
/// <para>
/// All six alignment calls read the same accessor <c>$GET_CHAR_ALIGNMENT</c> does —
/// <c>GetAdjAlignment</c>, the alignment <i>after</i> spell effects — so nothing here needs the
/// host beyond that one stat. The same is true of <c>$Status</c>, <c>$Gender</c> and
/// <c>$HitPoints</c>.
/// </para>
/// </remarks>
public static class GpdlAlignment
{
    /// <summary>
    /// The names <c>$Alignment</c> answers with (<c>CharAlignmentTypeText</c>).
    /// </summary>
    /// <remarks>
    /// <b>Upper case, and a space rather than a hyphen.</b> A script comparing against
    /// <c>"Lawful Good"</c> gets no match; these are the exact strings the character sheet prints.
    /// </remarks>
    public static readonly string[] Names =
    [
        "LAWFUL GOOD", "NEUTRAL GOOD", "CHAOTIC GOOD",
        "LAWFUL NEUTRAL", "TRUE NEUTRAL", "CHAOTIC NEUTRAL",
        "LAWFUL EVIL", "NEUTRAL EVIL", "CHAOTIC EVIL",
    ];

    /// <summary>What <c>$Status</c> answers with (<c>CharStatusTypeText</c>).</summary>
    public static readonly string[] StatusNames =
    [
        "OKAY", "UNCONSCIOUS", "DEAD", "FLED", "PETRIFIED",
        "GONE", "ANIMATED", "TEMP GONE", "RUNNING", "DYING",
    ];

    /// <summary>
    /// What <c>$Gender</c> answers with (<c>CharGenderTypeText</c>).
    /// </summary>
    /// <remarks>
    /// <b>Two entries for three genders.</b> <c>genderType</c> is <c>Male=0, Female=1,
    /// Bishop=2</c> (<c>GameRules.h:104</c>) but this table has only two rows, so the reference
    /// reads off the end of it for a Bishop. <see cref="Text"/> answers empty there instead — there
    /// is nothing to reproduce faithfully about indexing past an array. The engine's other naming
    /// function agrees that the value has no name: <c>GetGenderName</c> returns <c>"??"</c> for it.
    /// </remarks>
    public static readonly string[] GenderNames = ["MALE", "FEMALE"];

    /// <summary>
    /// A name out of one of the tables, or empty when the ordinal is not in it.
    /// </summary>
    public static string Text(string[] table, int? ordinal)
    {
        ArgumentNullException.ThrowIfNull(table);

        return ordinal is { } value && value >= 0 && value < table.Length
            ? table[value]
            : string.Empty;
    }

    /// <summary>
    /// The name of an alignment, or empty when there is none.
    /// </summary>
    /// <param name="alignment">
    /// The stored ordinal, or null for an actor that did not resolve — which answers empty, the
    /// same as GPDL false.
    /// </param>
    /// <remarks>
    /// <b>An out-of-range ordinal answers empty rather than indexing past the table.</b> The
    /// reference indexes <c>CharAlignmentTypeText</c> with no check at all
    /// (<c>GPDLexec.cpp:7681</c>), so a character carrying a tenth value reads whatever follows the
    /// array — a divergence taken deliberately, since there is nothing useful to reproduce about
    /// reading off the end of a table.
    /// </remarks>
    public static string NameOf(int? alignment) => Text(Names, alignment);

    /// <summary>
    /// Whether an alignment answers yes to one of the five questions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The five are not a partition, and <see cref="GpdlAlignmentTest.Neutral"/> is the odd
    /// one.</b> Lawful and Chaotic test the ethical axis only — three alignments each. Good and
    /// Evil test the moral axis only — three each. But <c>$AlignmentNeutral</c> is true for
    /// <b>five</b>: the three ethical neutrals <i>and</i> Neutral Good and Neutral Evil
    /// (<c>GPDLexec.cpp:7723</c>). So a Neutral Good character answers yes to both
    /// <c>$AlignmentGood</c> and <c>$AlignmentNeutral</c>, and True Neutral is the only alignment
    /// that answers yes to exactly one of the five.
    /// </para>
    /// <para>
    /// This asymmetry is easy to "tidy" into a symmetric pair of axis tests, which would silently
    /// change which scripts fire for the two Neutral-something alignments.
    /// </para>
    /// </remarks>
    public static bool Is(int? alignment, GpdlAlignmentTest test)
    {
        if (alignment is not { } value
            || value < 0
            || value > (int)GpdlAlignmentValue.ChaoticEvil)
        {
            return false;
        }

        return (GpdlAlignmentValue)value switch
        {
            GpdlAlignmentValue.LawfulGood => test is GpdlAlignmentTest.Good
                                                  or GpdlAlignmentTest.Lawful,
            GpdlAlignmentValue.NeutralGood => test is GpdlAlignmentTest.Good
                                                   or GpdlAlignmentTest.Neutral,
            GpdlAlignmentValue.ChaoticGood => test is GpdlAlignmentTest.Good
                                                   or GpdlAlignmentTest.Chaotic,
            GpdlAlignmentValue.LawfulNeutral => test is GpdlAlignmentTest.Lawful
                                                     or GpdlAlignmentTest.Neutral,
            GpdlAlignmentValue.TrueNeutral => test is GpdlAlignmentTest.Neutral,
            GpdlAlignmentValue.ChaoticNeutral => test is GpdlAlignmentTest.Chaotic
                                                      or GpdlAlignmentTest.Neutral,
            GpdlAlignmentValue.LawfulEvil => test is GpdlAlignmentTest.Evil
                                                  or GpdlAlignmentTest.Lawful,
            GpdlAlignmentValue.NeutralEvil => test is GpdlAlignmentTest.Evil
                                                   or GpdlAlignmentTest.Neutral,
            _ => test is GpdlAlignmentTest.Evil or GpdlAlignmentTest.Chaotic,
        };
    }
}

/// <summary>
/// The values <c>$IndexOf</c> and <c>$MyIndex</c> answer with that are not ordinary indices
/// (<c>m_IndexOf</c>, <c>GPDLexec.cpp:7546</c>).
/// </summary>
public static class GpdlActorIndex
{
    /// <summary>
    /// An actor with no valid instance behind it.
    /// </summary>
    /// <remarks>
    /// <b>A sentence, not a number</b> — so a script doing arithmetic on it reads zero and one
    /// comparing it against a number silently fails. Kept as the literal because a design can test
    /// for it.
    /// </remarks>
    public const string InvalidContext = "Invalid Context";

    /// <summary>
    /// What a character the party built during play answers, whatever its instance
    /// (<c>FLAG_CREATED_CHARACTER</c>).
    /// </summary>
    public const string CreatedCharacter = "-2";

    /// <summary>
    /// Added to the index of a combatant that joined mid-fight
    /// (<c>NewCombatantInstanceOffset</c>, <c>Externs.h:1549</c>).
    /// </summary>
    /// <remarks>
    /// Large enough that it cannot be mistaken for a party position or a combat order — which is
    /// the whole point, since all three share one number.
    /// </remarks>
    public const int NewCombatantOffset = 10000;
}
