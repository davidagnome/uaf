namespace UAFcore;

/// <summary>What turning did to one monster.</summary>
public enum TurnResult
{
    /// <summary>Nothing — the cleric could not reach this one.</summary>
    NoEffect = 0,

    /// <summary>It flees the map.</summary>
    Turned = -1,

    /// <summary>It is removed outright.</summary>
    Destroyed = -2,
}

/// <summary>
/// What a design says about turning one kind of monster (<c>TURN_DATA</c>, <c>Combatants.h:150</c>).
/// </summary>
/// <param name="UndeadType">
/// The turning category — the name the <c>TURN_ATTEMPT</c> script returns, not the monster's own
/// id. Several monsters share a category.
/// </param>
/// <param name="NumberToTurn">How many of this monster a successful attempt reaches.</param>
/// <param name="Destroys">
/// Whether they are destroyed rather than turned (<c>whatToDo == 2</c>).
/// </param>
public readonly record struct TurnData(string UndeadType, int NumberToTurn, bool Destroys);

/// <summary>
/// Turning undead (<c>COMBAT_DATA::TurnUndead</c>, <c>Combatants.cpp:6311</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The AD&amp;D turning table is dead code.</b> `UndeadTurnTable` and
/// <c>GetUndeadTurnValueByHD</c> are complete and correct in the reference
/// (<c>GameRules.cpp:506</c>, <c>:538</c>) — thirteen undead rows against fourteen cleric levels —
/// but their only caller, the exported <c>GetUndeadTurnValue</c>, was stubbed when the undead type
/// stopped being an enum: it tests for a sentinel, calls <c>NotImplemented(0x145ab)</c>, and
/// returns zero, with the line that would reach the table left commented out beside it
/// (<c>:629</c>). Nothing else calls either function. So the table is never consulted, and
/// <b>turning is entirely design-scripted</b> through the <c>TURN_ATTEMPT</c> hook. Not ported, for
/// the same reason the other dead branches were not.
/// </para>
/// <para>
/// What remains — and what this is — is the application half: given which categories the cleric
/// managed to affect and how many of each, walk the combatants and turn or destroy them.
/// </para>
/// </remarks>
public static class TurnUndead
{
    /// <summary>
    /// Applies a turning attempt.
    /// </summary>
    /// <param name="turnDataOf">
    /// The design's turning record for a combatant, or null if it is not undead at all.
    /// </param>
    /// <param name="reached">
    /// How many of each category the attempt reached — the result of the <c>TURN_ATTEMPT</c>
    /// script crossed with the design's <c>numToTurn</c> figures. Categories absent from this are
    /// untouched.
    /// </param>
    /// <returns>What happened to each combatant that anything happened to, in combatant order.</returns>
    /// <remarks>
    /// <para>
    /// <b>Two passes, and the first ignores anyone already running.</b> A monster with status
    /// <c>Fled</c> or <c>Running</c> is skipped on pass 0 and considered on pass 1, so a standing
    /// monster is always turned in preference to one already leaving. Without the two passes a
    /// cleric could spend the whole attempt on monsters that were fleeing anyway.
    /// </para>
    /// <para>
    /// <b>The dead and the gone are skipped on both passes</b>, so they never consume a slot.
    /// </para>
    /// </remarks>
    public static List<(int Combatant, TurnResult Result)> Resolve(
        IReadOnlyList<Combatant> combatants,
        Func<Combatant, TurnData?> turnDataOf,
        IReadOnlyDictionary<string, int> reached)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(turnDataOf);
        ArgumentNullException.ThrowIfNull(reached);

        var results = new List<(int, TurnResult)>();
        var used = reached.ToDictionary(e => e.Key, _ => 0);

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var target in combatants)
            {
                if (target.Status is CharacterStatus.Gone or CharacterStatus.Dead)
                {
                    continue;
                }

                if (pass == 0
                    && target.Status is CharacterStatus.Fled or CharacterStatus.Running)
                {
                    continue;
                }

                if (turnDataOf(target) is not { } data
                    || !used.TryGetValue(data.UndeadType, out int spent)
                    || spent >= reached[data.UndeadType]
                    || results.Any(r => r.Item1 == target.Index))
                {
                    continue;
                }

                used[data.UndeadType] = spent + 1;
                results.Add((target.Index,
                             data.Destroys ? TurnResult.Destroyed : TurnResult.Turned));
            }
        }

        return results;
    }

    /// <summary>
    /// Carries out what <see cref="Resolve"/> decided.
    /// </summary>
    /// <param name="cleric">Who turned them — a turned monster flees <i>from</i> somebody.</param>
    /// <remarks>
    /// <b>A turned monster is set running, not removed.</b> Its status becomes
    /// <see cref="CharacterStatus.Running"/> and its last attacker is set to the cleric, which is
    /// how it knows which way to run (<c>Combatants.cpp:6472</c>). A destroyed one becomes
    /// <see cref="CharacterStatus.Gone"/> and leaves the map. Both have their turn ended.
    /// </remarks>
    public static void Apply(IReadOnlyList<Combatant> combatants, CombatMap map,
                             TurnQueue queue, Combatant cleric,
                             IEnumerable<(int Combatant, TurnResult Result)> results)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(cleric);
        ArgumentNullException.ThrowIfNull(results);

        foreach (var (index, result) in results)
        {
            var target = combatants[index];
            target.EndTurn(queue);

            switch (result)
            {
                case TurnResult.Turned:
                    target.Status = CharacterStatus.Running;
                    target.IsTurned = true;
                    target.LastAttacker = cleric.Index;
                    break;

                case TurnResult.Destroyed:
                    target.Status = CharacterStatus.Gone;
                    map.Remove(target.X, target.Y, target.Icon.Width, target.Icon.Height);
                    break;
            }
        }
    }

    /// <summary>
    /// Whether a combatant may attempt to turn (<c>CanTurnUndead</c>, <c>Combatant.cpp:7654</c>).
    /// </summary>
    /// <param name="turnLevel">
    /// The best turning level across the character's baseclasses — a baseclass's level minus its
    /// <c>m_turnUndeadLevel</c>.
    /// </param>
    /// <remarks>
    /// <b>The test is against 99, not zero.</b> <c>GetTurnUndeadLevel() &lt; 99</c> is the whole
    /// condition, so 99 is the sentinel for "this character does not turn" and any lower value —
    /// including a negative one — passes.
    /// </remarks>
    public const int CannotTurn = 99;

    /// <inheritdoc cref="CannotTurn"/>
    public static bool CanTurn(Combatant combatant, int turnLevel)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        return !combatant.IsDone() && turnLevel < CannotTurn;
    }
}
