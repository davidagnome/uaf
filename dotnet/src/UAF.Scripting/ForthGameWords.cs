namespace UAF.Scripting;

/// <summary>
/// The twenty-one words that read a <see cref="ForthCombatSummary"/>, and <c>RunTHINK</c>
/// (<c>Forth.cpp:2132</c>–<c>:2534</c>).
/// </summary>
public sealed partial class ForthMachine
{
    private ForthCombatSummary? summary;

    /// <summary>The action the reader words are currently pointed at (<c>pCSA</c>).</summary>
    private ForthAction? selectedAction;

    /// <summary>The combatant they are pointed at (<c>pCSC</c>).</summary>
    private ForthCombatant? selectedCombatant;

    /// <summary>
    /// Which end of each action <c>Me</c>/<c>He</c> last selected — the reference keeps this
    /// <i>per action</i>, in each one's own <c>pCSC</c>, rather than as one global mode.
    /// </summary>
    private bool readingHe;

    /// <summary>
    /// Runs the design's <c>THINK</c> over two candidate actions
    /// (<c>RunTHINK</c>, <c>Forth.cpp:2510</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both stacks are reset to empty before the call.</b> Whatever a previous comparison left
    /// behind is discarded, so a script with an unbalanced word cannot poison the next comparison —
    /// though <c>docolon</c>'s own check would have caught it first.
    /// </para>
    /// <para>
    /// <b>The selection starts at action A, reading <c>Me</c>.</b> Both actions have their
    /// combatant pointer set to their own actor, and the current one is A's.
    /// </para>
    /// <para>
    /// <b>A design with no <c>THINK</c> scores every pair 0</b> — the reference returns 0 rather
    /// than complaining, which makes every action equal and leaves the candidate order to the sort.
    /// </para>
    /// </remarks>
    /// <returns>A minus B: positive prefers A, negative prefers B, 0 no preference.</returns>
    public int RunThink(ForthCombatSummary combatSummary)
    {
        ArgumentNullException.ThrowIfNull(combatSummary);

        return RunAgainst("THINK", combatSummary);
    }

    /// <summary>
    /// Whether the design's script rejects one candidate action (the six <c>Run*Filter</c>
    /// functions, <c>Forth.cpp:2360</c>–<c>:2504</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A non-zero result rejects.</b> The reference's call sites read
    /// <c>if (RunRangedWeaponFilter(…) != 0) return;</c> — the action is simply not added. So the
    /// sense is the opposite of <c>MonsterAiScript.Survives</c>, which answers whether to keep it.
    /// </para>
    /// <para>
    /// <b>Both candidate slots hold the same action.</b> The filters set
    /// <c>pActionA = pActionB = pcsa</c>, so a filter word may say <c>A</c> or <c>B</c> and reach
    /// the one action either way — which is why the shipped filters open with <c>Me</c> or
    /// <c>He</c> and never select a candidate.
    /// </para>
    /// <para>
    /// <b>A design whose script omits a filter keeps every action.</b> The lookup fails, the
    /// reference returns 0, and 0 means keep.
    /// </para>
    /// </remarks>
    public bool Rejects(ForthAiFilter filter, ForthAction action,
                        IReadOnlyList<ForthCombatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(combatants);

        var one = new ForthCombatSummary
        {
            ActionA = action,
            ActionB = action,
            Combatants = combatants,
        };

        return RunAgainst(FilterWord(filter), one) != 0;
    }

    /// <summary>The script word each filter runs, named as the reference names it.</summary>
    private static string FilterWord(ForthAiFilter filter) => filter switch
    {
        ForthAiFilter.SpellCaster => "SpellCasterFilter",
        ForthAiFilter.SpellLikeAbility => "SpellLikeAbilityFilter",
        ForthAiFilter.Advance => "AdvanceFilter",
        ForthAiFilter.Judo => "JudoFilter",
        ForthAiFilter.MeleeWeapon => "MeleeWeaponFilter",
        ForthAiFilter.RangedWeapon => "RangedWeaponFilter",
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    /// <summary>
    /// The shape <c>RunTHINK</c> and all six filters share: point the reader words at a summary,
    /// reset both stacks, and run one word.
    /// </summary>
    /// <remarks>
    /// <b>Both stacks are reset to empty before the call.</b> Whatever a previous comparison left
    /// behind is discarded, so a script with an unbalanced word cannot poison the next one — though
    /// <c>docolon</c>'s own stack-effect check would have caught it first.
    /// </remarks>
    private int RunAgainst(string word, ForthCombatSummary combatSummary)
    {
        summary = combatSummary;
        readingHe = false;
        selectedAction = combatSummary.ActionA;
        selectedCombatant = selectedAction.Me;

        int cfa = Lookup(word);
        if (cfa == 0)
        {
            return 0;
        }

        m.Sp = ForthMemory.DataStackBase;
        m.Rp = ForthMemory.ReturnStackBase;
        exit = 0;
        m.Cfa = (ushort)cfa;

        DoColon();

        return m.Pop();
    }

    /// <summary>
    /// The bindings, in the order the kernel's <c>PRIM</c> lines name them.
    /// </summary>
    /// <remarks>
    /// <b><c>Me</c>/<c>He</c> and <c>A</c>/<c>B</c> are two independent selectors.</b> The first
    /// pair chooses which end of an action to read — the actor or the target — and sets it on
    /// <i>both</i> candidate actions at once. The second chooses which candidate. That is why the
    /// script reads <c>Me A A:Type B A:Type</c>: set the end, then take each action in turn.
    /// </remarks>
    private void AddGameWords()
    {
        primitives.AddRange(
        [
            SelectMe,                                   // 52 Me
            SelectHe,                                   // 53 He
            () => SelectAction(Summary.ActionA),        // 54 A
            () => SelectAction(Summary.ActionB),        // 55 B
            () => m.Push(Action.ActionType),            // 56 A:Type
            () => m.Push(Action.Damage),                // 57 A:Damage
            () => PushWeapon(w => w.Type),              // 58 W:Type
            () => PushWeapon(w => w.Range22),           // 59 W:Range
            () => PushWeapon(w => w.Protection),        // 60 W:Protection
            WeaponDamage,                               // 61 W:Damage
            () => PushWeapon(w => w.RateOfFire),        // 62 W:ROF
            () => PushWeapon(w => w.AttackBonus),       // 63 W:AttackBonus
            () => PushWeapon(w => w.Priority),          // 64 W:Priority
            ShieldNext,                                 // 65 Shield.Next
            ShieldReady,                                // 66 Shield.Ready!
            () => m.Push(Combatant.Fleeing),            // 67 Fleeing@
            () => m.Push(Combatant.State),              // 68 C:State

            // C:Distance and C:HasLineOfSight carry the combatant prefix and read the ACTION.
            // Both are properties of the pairing rather than of either party, and the naming is
            // the reference's.
            () => m.Push(Action.Distance22),            // 69 C:Distance
            () => m.Push(Combatant.Friendly),           // 70 C:Friendly
            () => m.Push(Combatant.AiBaseclass),        // 71 C:AIBaseclass
            () => m.Push(Action.HasLineOfSight),        // 72 C:HasLineOfSight
        ]);
    }

    private ForthCombatSummary Summary =>
        summary ?? throw new InvalidOperationException(
            "A Forth combat-summary word ran outside RunThink, so there is nothing to read.");

    private ForthAction Action =>
        selectedAction ?? throw new InvalidOperationException(
            "A Forth combat-summary word ran outside RunThink, so there is nothing to read.");

    private ForthCombatant Combatant =>
        selectedCombatant ?? throw new InvalidOperationException(
            "A Forth combat-summary word ran outside RunThink, so there is nothing to read.");

    private void SelectMe()
    {
        readingHe = false;
        selectedCombatant = Action.Me;
    }

    private void SelectHe()
    {
        readingHe = true;
        selectedCombatant = Action.He;
    }

    private void SelectAction(ForthAction action)
    {
        selectedAction = action;
        selectedCombatant = readingHe ? action.He : action.Me;
    }

    /// <summary>
    /// One field of the selected action's weapon, or 0 when it has none.
    /// </summary>
    /// <remarks>
    /// <b>The weapon belongs to the selected <i>combatant</i> and the ordinal to the selected
    /// <i>action</i>.</b> So <c>He W:Damage</c> indexes the target's weapon list with the actor's
    /// weapon number — which is meaningful only because the script always reads <c>W:</c> under
    /// <c>Me</c>. Nothing stops a design doing otherwise.
    /// </remarks>
    private void PushWeapon(Func<ForthWeapon, int> field)
    {
        int ordinal = Action.WeaponOrdinal;

        m.Push(ordinal == 0 || ordinal > Combatant.Weapons.Count
                   ? 0                                  // NotWeapon
                   : field(Combatant.Weapons[ordinal - 1]));
    }

    /// <summary>
    /// <c>W:Damage</c> — dice plus bonus, large or small.
    /// </summary>
    /// <remarks>
    /// <b>The size is always the <i>target's</i>, whichever end is selected.</b> It reads
    /// <c>pCSA->pHe->isLarge</c> directly rather than the selected combatant, so <c>He W:Damage</c>
    /// still sizes the damage by the target — which is the intent, and is the one place the
    /// selection is bypassed.
    /// </remarks>
    private void WeaponDamage()
    {
        int ordinal = Action.WeaponOrdinal;

        if (ordinal == 0 || ordinal > Combatant.Weapons.Count)
        {
            m.Push(0);
            return;
        }

        var weapon = Combatant.Weapons[ordinal - 1];

        m.Push(Action.He.IsLarge != 0
                   ? weapon.LargeDamageDice + weapon.LargeDamageBonus
                   : weapon.SmallDamageDice + weapon.SmallDamageBonus);
    }

    /// <summary>
    /// <c>Shield.Next</c> — advances a shield index on the top of the stack.
    /// </summary>
    /// <remarks>
    /// <b>It cycles through <i>n+1</i> values, not <i>n</i>.</b> The test is <c>i >= n</c> before
    /// incrementing, so from 0 a combatant with two shields yields 1, 2, 0, 1, 2 — index 2 is
    /// produced although only 0 and 1 exist. A script that reads a shield at the index it is
    /// handed has to guard for itself. Transcribed as written.
    /// </remarks>
    private void ShieldNext()
    {
        int n = Combatant.ShieldCount;
        int i = m.Stack(0);
        m.SetStack(0, i >= n ? 0 : i + 1);
    }

    /// <summary>
    /// <c>Shield.Ready!</c> — asks for a shield to be readied.
    /// </summary>
    /// <remarks>
    /// <b>It always writes combatant 0</b>, never the selected one. Combatant 0 is whoever is
    /// taking the turn, which is the only one who could ready anything — so the selection is
    /// deliberately ignored here.
    /// </remarks>
    private void ShieldReady()
    {
        int i = m.Pop();

        if (Summary.Combatants.Count > 0)
        {
            Summary.Combatants[0].ShieldToReady = i;
        }
    }
}
