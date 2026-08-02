namespace UAFcore;

/// <summary>
/// How a spell chooses what it lands on (<c>spellTargetingType</c>, <c>GameRules.h:299</c>).
/// </summary>
/// <remarks>
/// The numbering is serialized, so it is transcribed rather than tidied.
/// <para>
/// <b>What the shipped designs actually use</b>, out of 377 / 117 / 318 spells in
/// <c>SomethingWild</c>, <c>ci-tier3</c> and <c>Case</c>: <see cref="TouchedTargets"/> is the
/// commonest (127 / 54 / 103), then <see cref="SelectedByCount"/> (71 / 10 / 63) and
/// <see cref="Self"/> (64 / 19 / 63). <see cref="AreaSquare"/> (47 / 10 / 30) and
/// <see cref="AreaCircle"/> (34 / 17 / 30) carry the area spells between them;
/// <see cref="WholeParty"/> is nearly unused (3 / 2 / 2).
/// </para>
/// </remarks>
public enum SpellTargeting
{
    /// <summary>The caster only.</summary>
    Self = 0,

    /// <summary>A fixed number of individually chosen targets.</summary>
    SelectedByCount = 1,

    /// <summary>Every party member, wherever they are.</summary>
    WholeParty = 2,

    /// <summary>Individually chosen, but only within a square of the caster.</summary>
    TouchedTargets = 3,

    /// <summary>A circle centred on a chosen square.</summary>
    AreaCircle = 4,

    /// <summary>Targets taken until a total hit-dice limit is reached.</summary>
    SelectByHitDice = 5,

    /// <summary>A line starting at a chosen square, running away from the caster.</summary>
    AreaLinePickStart = 6,

    /// <summary>A line from the caster to a chosen square.</summary>
    AreaLinePickEnd = 7,

    /// <summary>A rectangle centred on a chosen square, rotated to the casting direction.</summary>
    AreaSquare = 8,

    /// <summary>A cone spreading from the caster towards a chosen square.</summary>
    AreaCone = 9,
}

/// <summary>
/// What a cast is allowed to hit, worked out before targets are chosen
/// (<c>CHARACTER::InitTargeting</c>, <c>Char.cpp:15549</c>).
/// </summary>
/// <param name="MaxTargets">
/// How many targets may be taken. <b>For the two line shapes this is the line's width in squares,
/// not a count</b> — <c>GetCombatantsInLine</c> passes <c>MaxTargets()</c> as its width parameter
/// (<c>Combatant.cpp:7999</c>). The same field means two different things depending on the shape.
/// </param>
/// <param name="MaxRange">
/// How far a target may be. Zero from the design means unlimited, and is stored as
/// <see cref="Unlimited"/>.
/// </param>
/// <param name="Width">Across the casting direction, for the area shapes.</param>
/// <param name="Height">Along it.</param>
/// <param name="SelectingUnits">
/// Whether the player picks combatants (true) or a map square (false). Every area shape picks a
/// square <i>in combat</i>; out of combat they all degenerate to the whole party.
/// </param>
/// <param name="MaxHitDice">
/// The hit-dice budget for <see cref="SpellTargeting.SelectByHitDice"/>, and zero for everything
/// else.
/// </param>
/// <param name="IsArea">Whether this cast covers squares rather than named combatants.</param>
public readonly record struct SpellTargetingSetup(
    int MaxTargets, int MaxRange, int Width, int Height,
    bool SelectingUnits, int MaxHitDice, bool IsArea)
{
    /// <summary>
    /// What a range of zero becomes (<c>if (range==0) range = 1000000</c>).
    /// </summary>
    public const int Unlimited = 1000000;

    /// <summary>
    /// The range <see cref="SpellTargeting.TouchedTargets"/> is given.
    /// </summary>
    /// <remarks>
    /// <b>Not 1, despite the name and the reference's own comment.</b> The comment above
    /// <c>NeedSpellTargeting</c> says "TouchedTargets: affects 1 target within range 1", and
    /// <c>//targets.m_MaxRange=1;</c> sits commented out beside the line that sets 9999. The reach
    /// is enforced instead by <c>m_maxRangeX</c> and <c>m_maxRangeY</c>, both 1 — a one-square box
    /// rather than a radius, which is the same thing on a square grid but arrives by a different
    /// route.
    /// </remarks>
    public const int TouchRange = 9999;
}

/// <summary>
/// Working out what a spell may target (<c>InitTargeting</c> and <c>NeedSpellTargeting</c>,
/// <c>Char.cpp:15549</c>, <c>Globals.cpp:4176</c>).
/// </summary>
public static class SpellTargets
{
    /// <summary>
    /// Whether the player has to pick anything, or the spell just goes off
    /// (<c>NeedSpellTargeting</c>, <c>Globals.cpp:4176</c>).
    /// </summary>
    /// <param name="inCombat">
    /// Out of combat only the party can be targeted, which collapses every area shape into "the
    /// whole party" and removes the need to pick.
    /// </param>
    /// <remarks>
    /// <see cref="SpellTargeting.Self"/> and <see cref="SpellTargeting.WholeParty"/> never need a
    /// selection. In combat everything else does; out of combat the area shapes do not.
    /// </remarks>
    public static bool NeedsSelection(SpellTargeting targeting, bool inCombat = true) =>
        targeting switch
        {
            SpellTargeting.Self or SpellTargeting.WholeParty => false,
            SpellTargeting.SelectedByCount or SpellTargeting.TouchedTargets
                or SpellTargeting.SelectByHitDice => true,
            _ => inCombat,
        };

    /// <summary>
    /// Sets up a cast (<c>InitTargeting</c>).
    /// </summary>
    /// <param name="targets">
    /// The evaluated target quantity. For area shapes this is the cap on how many combatants the
    /// area may catch; for the line shapes it is the line's width — see
    /// <see cref="SpellTargetingSetup.MaxTargets"/>.
    /// </param>
    /// <param name="range">The evaluated range. Zero means unlimited.</param>
    /// <param name="width">Across the casting direction.</param>
    /// <param name="height">Along it.</param>
    /// <param name="partySize">
    /// Used as the target cap for <see cref="SpellTargeting.WholeParty"/>, and for every area shape
    /// out of combat.
    /// </param>
    /// <remarks>
    /// <b>Out of combat, every area shape becomes the whole party.</b> Each area branch has an
    /// <c>else</c> whose comment reads "acts like ttype=WholeParty" — units rather than squares,
    /// the party size as the cap, and no range at all. The width and height the design supplied are
    /// dropped on the floor.
    /// </remarks>
    public static SpellTargetingSetup Setup(SpellTargeting targeting, int targets, int range,
                                            int width, int height, int partySize,
                                            bool inCombat = true)
    {
        int Ranged() => range == 0 ? SpellTargetingSetup.Unlimited : range;

        // Every area shape: squares in combat, the whole party outside it.
        SpellTargetingSetup Area() => inCombat
            ? new SpellTargetingSetup(targets, Ranged(), width, height,
                                      SelectingUnits: false, MaxHitDice: 0, IsArea: true)
            : new SpellTargetingSetup(partySize, MaxRange: 0, Width: 0, Height: 0,
                                      SelectingUnits: true, MaxHitDice: 0, IsArea: true);

        return targeting switch
        {
            SpellTargeting.Self =>
                new SpellTargetingSetup(1, 0, 0, 0, true, 0, false),

            SpellTargeting.SelectedByCount =>
                new SpellTargetingSetup(targets, Ranged(), 0, 0, true, 0, false),

            SpellTargeting.WholeParty =>
                new SpellTargetingSetup(partySize, 0, 0, 0, true, 0, false),

            SpellTargeting.TouchedTargets =>
                new SpellTargetingSetup(targets, SpellTargetingSetup.TouchRange, 0, 0,
                                        true, 0, false),

            // The hit-dice budget replaces the target count outright: MaxTargets is set to zero.
            SpellTargeting.SelectByHitDice =>
                new SpellTargetingSetup(0, Ranged(), 0, 0, true, targets, false),

            _ => Area(),
        };
    }

    /// <summary>
    /// Whether a caster may target a combatant, before range or line of sight
    /// (<c>C_AddTarget</c>, <c>Combatant.cpp:7912</c>).
    /// </summary>
    /// <param name="canTargetFriend">The spell's <c>CanTargetFriend</c>.</param>
    /// <param name="canTargetEnemy">The spell's <c>CanTargetEnemy</c>.</param>
    /// <remarks>
    /// <b>"Friend" means the caster's own side, not the party's.</b> The test is
    /// <c>targ.GetIsFriendly() == this-&gt;GetIsFriendly()</c>, so a monster casting a
    /// friends-only spell reaches other monsters. Getting this wrong makes every enemy buff heal
    /// the party instead.
    /// <para>
    /// A cast that is picking a map square rather than combatants refuses every combatant outright
    /// — <c>SelectingUnits</c> is the first test in the reference, before either side check.
    /// </para>
    /// </remarks>
    public static bool CanTarget(bool selectingUnits, bool casterIsFriendly,
                                 bool targetIsFriendly, bool canTargetFriend, bool canTargetEnemy)
    {
        if (!selectingUnits)
        {
            return false;
        }

        bool sameSide = targetIsFriendly == casterIsFriendly;
        return sameSide ? canTargetFriend : canTargetEnemy;
    }
}
