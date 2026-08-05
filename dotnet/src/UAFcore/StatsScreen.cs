using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The stats screen: six scores a player can shuffle points between, and a re-roll
/// (<c>CHOOSESTATS_MENU_DATA</c>, <c>RunEvent.cpp:4048</c>;
/// <c>handleChooseStatsInput</c>, <c>CharStatsForm.cpp:1937</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the same screen twice.</b> The generator shows it after the character is rolled, and
/// the party menu's MODIFY pushes it over a party member — <c>CHOOSESTATS_MENU_DATA(false)</c>,
/// where the flag means "use the existing character". <b>Both branches of that flag call
/// <c>generateNewCharacter</c>;</b> the only difference between them is which string goes in the
/// debug log when the generation fails. So MODIFY is not a wizard re-entered over a character, it
/// is a re-roll of one.
/// </para>
/// <para>
/// <b>The title is the only thing <c>AllowModifyStats</c> changes in practice.</b> It is a global
/// initialised to true (<c>Globals.cpp:588</c>) and assigned nowhere else, so the TAB and up/down
/// handling it guards is always live.
/// </para>
/// </remarks>
public sealed class StatsScreen
{
    /// <summary>The two menu entries: re-roll, or keep what is showing.</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [("REROLL", 0), ("ACCEPT", 0)];

    /// <summary>Which entry ends the screen. Item 2 in the reference, counting from one.</summary>
    public const int Accept = 1;

    /// <summary>The six scores TAB moves between, in the order the form lays them out.</summary>
    public static readonly string[] Abilities = RolledCharacter.AbilityNames;

    private readonly Func<string, AbilityLimits> limits;
    private readonly Func<AbilityScores, AbilityScores> normalise;
    private readonly Func<int?> strengthDice;
    private readonly Func<AbilityScores, int> hitPoints;

    private readonly Func<(AbilityScores? Scores, int MaxHitPoints)>? reroll;
    private readonly Action<StatsScreen>? accept;

    private int cachedPercentile;

    /// <summary>
    /// Opens the screen over a character.
    /// </summary>
    /// <param name="limits">The class's range for one ability, by name.</param>
    /// <param name="normalise">
    /// <c>UpdateStats</c>'s clamps — the race's limits and then the class's — applied to all six
    /// after any change.
    /// </param>
    /// <param name="strengthDice">The class's exceptional-strength percentile dice.</param>
    /// <param name="hitPoints">
    /// Recomputes the maximum from the seed. The reference zeroes <c>maxHitPoints</c> and calls
    /// <c>DetermineNewCharMaxHitPoints</c> after <b>every</b> adjustment, up or down.
    /// </param>
    /// <param name="reroll">
    /// Generates the character again — <c>generateNewCharacter</c>. Null for a screen with no
    /// re-roll behind it, which simply refuses.
    /// </param>
    public StatsScreen(AbilityScores scores, int maxHitPoints,
                       Func<string, AbilityLimits> limits,
                       Func<AbilityScores, AbilityScores> normalise,
                       Func<int?> strengthDice,
                       Func<AbilityScores, int> hitPoints,
                       Func<(AbilityScores? Scores, int MaxHitPoints)>? reroll = null,
                       Action<StatsScreen>? accept = null)
    {
        ArgumentNullException.ThrowIfNull(scores);

        this.reroll = reroll;
        this.accept = accept;
        this.limits = limits ?? throw new ArgumentNullException(nameof(limits));
        this.normalise = normalise ?? throw new ArgumentNullException(nameof(normalise));
        this.strengthDice = strengthDice ?? throw new ArgumentNullException(nameof(strengthDice));
        this.hitPoints = hitPoints ?? throw new ArgumentNullException(nameof(hitPoints));

        Scores = scores;
        MaxHitPoints = maxHitPoints;
        HitPoints = maxHitPoints;
    }

    /// <summary>The scores as they stand.</summary>
    public AbilityScores Scores { get; private set; }

    /// <summary>The recomputed maximum, and the current total which follows it.</summary>
    public int MaxHitPoints { get; private set; }

    /// <inheritdoc cref="MaxHitPoints"/>
    public int HitPoints { get; private set; }

    /// <summary>Points still to spend. Starts at zero and only a decrease adds to it.</summary>
    public int Available { get; private set; }

    /// <summary>
    /// Which ability is highlighted, or null before the first TAB.
    /// </summary>
    /// <remarks>
    /// <b>Up and down do nothing until something is highlighted.</b> The reference's
    /// <c>currentSelection</c> opens at -1 and both adjust functions fall through their switch to
    /// <c>return false</c>.
    /// </remarks>
    public int? Highlighted { get; private set; }

    /// <summary>Moves the highlight to the next ability, wrapping.</summary>
    /// <remarks>
    /// <b>From nothing it lands on strength</b>, the first tabbable field on the form
    /// (<c>TEXT_FORM::Tab</c>, <c>TextForm.cpp:282</c>).
    /// </remarks>
    public void Tab() =>
        Highlighted = Highlighted is int at ? (at + 1) % Abilities.Length : 0;

    /// <summary>Raises the highlighted score, if there is a point and the class allows it.</summary>
    public bool Raise() => Adjust(up: true);

    /// <summary>Lowers the highlighted score, if the class allows it.</summary>
    public bool Lower() => Adjust(up: false);

    private bool Adjust(bool up)
    {
        if (Highlighted is not int index)
        {
            return false;
        }

        string ability = Abilities[index];
        int before = ScoreOf(Scores, index);

        var change = up
            ? StatAdjustment.Increase(before, Available, limits(ability), Clamped(index))
            : StatAdjustment.Decrease(before, Available, limits(ability), Clamped(index));

        if (!change.Changed)
        {
            return false;
        }

        Available = change.Available;
        Scores = With(Scores, index, change.Score);

        if (index == 0)
        {
            var (percentile, cached) = StatAdjustment.StrengthPercentile(
                before, change.Score, cachedPercentile, strengthDice);

            cachedPercentile = cached;
            if (percentile is int found)
            {
                Scores = Scores with { StrengthMod = found };
            }
        }

        // maxHitPoints = 0; DetermineNewCharMaxHitPoints(hitpointSeed); hitPoints = maxHitPoints.
        // Note the last of those: shuffling a point around fully heals the character.
        MaxHitPoints = hitPoints(Scores);
        HitPoints = MaxHitPoints;

        return true;
    }

    /// <summary>
    /// <c>UpdateStats</c>'s clamps, narrowed to the one score being changed.
    /// </summary>
    /// <remarks>
    /// The reference clamps all six every time; only the one that moved can be out of range, and
    /// running the others through would let a score already outside its limits be silently
    /// corrected by an unrelated keypress.
    /// </remarks>
    private Func<int, int> Clamped(int index) =>
        candidate => ScoreOf(normalise(With(Scores, index, candidate)), index);

    /// <summary>Replaces the character with a freshly rolled one, keeping the old one on failure.</summary>
    /// <param name="rolled">
    /// The re-roll, or null when <c>generateNewCharacter</c> produced nothing. The reference tests
    /// <c>GetMaxHitPoints() == 0</c> and copies the pre-roll character back.
    /// </param>
    /// <remarks>
    /// <b>A re-roll does not refund or clear the points already spent.</b> The available count is a
    /// static in <c>handleChooseStatsInput</c> that only <c>CHOOSESTATS_initial</c> resets — and
    /// the re-roll path calls exactly that, so it does. The highlight and the cached percentile go
    /// with it.
    /// </remarks>
    public bool Reroll()
    {
        if (reroll is null)
        {
            return false;
        }

        var (rolled, maxHitPoints) = reroll();
        return Reroll(rolled, maxHitPoints);
    }

    /// <inheritdoc cref="Reroll()"/>
    public bool Reroll(AbilityScores? rolled, int maxHitPoints)
    {
        if (rolled is null || maxHitPoints == 0)
        {
            return false;
        }

        Scores = rolled;
        MaxHitPoints = maxHitPoints;
        HitPoints = maxHitPoints;

        Available = 0;
        Highlighted = null;
        cachedPercentile = 0;

        return true;
    }

    /// <summary>
    /// Writes what is showing back onto whatever the screen was opened over.
    /// </summary>
    /// <remarks>
    /// <b>The write-back belongs to the screen, not to the runner</b>, because the two callers
    /// put the result in different places: the generator holds it until the character is saved,
    /// and MODIFY puts it straight onto a party member.
    /// </remarks>
    public void Accepted() => accept?.Invoke(this);

    private static int ScoreOf(AbilityScores scores, int index) => index switch
    {
        0 => scores.Strength,
        1 => scores.Intelligence,
        2 => scores.Wisdom,
        3 => scores.Dexterity,
        4 => scores.Constitution,
        _ => scores.Charisma,
    };

    private static AbilityScores With(AbilityScores scores, int index, int value) => index switch
    {
        0 => scores with { Strength = value },
        1 => scores with { Intelligence = value },
        2 => scores with { Wisdom = value },
        3 => scores with { Dexterity = value },
        4 => scores with { Constitution = value },
        _ => scores with { Charisma = value },
    };
}
