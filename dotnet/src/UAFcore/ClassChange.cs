using UAF.Serialization;

namespace UAFcore;

/// <summary>Why a character cannot change class.</summary>
public enum ClassChangeRefusal
{
    None = 0,

    /// <summary>Monsters do not change class.</summary>
    Monster,

    /// <summary>The race's <c>m_canChangeClass</c> flag is clear.</summary>
    RaceCannot,

    /// <summary>The design has no race by that name at all.</summary>
    RaceUnknown,

    /// <summary>Already dual-classed once, which is the limit.</summary>
    AlreadyDualClassed,

    /// <summary>Every other class in the design said no.</summary>
    NoClassQualifies,
}

/// <summary>
/// Which classes a character may change to (<c>CHARACTER::CreateChangeClassList</c>,
/// <c>Char.cpp:7646</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision itself belongs to the design, not to the engine.</b> Every built-in rule —
/// alignment against the new baseclasses, a minimum of 15 or 17 in the old class's prime
/// abilities — is commented out in <c>CanChangeToClass</c> (<c>Char.cpp:7553</c>), with the reason
/// written above it: the designer asked "the engine to have no part in this decision" and wanted
/// scripts to do all the work. What runs instead is two script hooks.
/// </para>
/// <para>
/// <b>Both hooks must answer <c>Y</c>, and they are asked of different classes.</b>
/// <c>CanChangeFromClass</c> runs on the class being left, with the target's id in
/// <c>hookParameters[5]</c>; <c>CanChangeToClass</c> runs on the class being joined, with the
/// origin's id. Either one silent is a refusal.
/// </para>
/// <para>
/// <b>Silence is a refusal, and that is the shipped behaviour.</b> <c>hookParameters[0]</c> starts
/// empty and is emptied again between the two calls, so a class carrying no such script leaves it
/// empty, fails <c>!= 'Y'</c>, and the change is refused. A design that has not written these
/// scripts has a permanently dark CHANGE CLASS entry — in the reference as much as here.
/// </para>
/// <para>
/// <b>So the hook is a parameter.</b> Running it needs the class's special-ability scripts
/// compiled and interpreted, which is the scripting phase; everything around it is ported, and
/// <see cref="NoScripts"/> is what the engine does today.
/// </para>
/// </remarks>
public static class ClassChange
{
    /// <summary>
    /// The answer with no scripting layer: no.
    /// </summary>
    /// <remarks>
    /// Not a stub standing in for the real rule — it <i>is</i> the real rule for a class with no
    /// <c>CanChangeFromClass</c>/<c>CanChangeToClass</c> script, which is every class in every
    /// shipped design.
    /// </remarks>
    public static bool NoScripts(string from, string to) => false;

    /// <summary>
    /// The classes this character could change to, in the design's own order.
    /// </summary>
    /// <param name="race">
    /// The character's race record, or null when the design has none by that name — which the
    /// reference treats as a refusal rather than as permission.
    /// </param>
    /// <param name="classIds">Every class in the design, including the character's own.</param>
    /// <param name="canChange">
    /// The two script hooks, as one predicate over (from, to). <see cref="NoScripts"/> until the
    /// scripting phase lands.
    /// </param>
    /// <remarks>
    /// <b>The character's current class is skipped by name</b>, and <c>CLASS_ID</c> derives from
    /// <c>CString</c>, so the comparison is case-sensitive.
    /// </remarks>
    public static IReadOnlyList<string> Options(Character who, RaceRecord? race,
                                                IEnumerable<string> classIds,
                                                Func<string, string, bool> canChange,
                                                out ClassChangeRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(classIds);
        ArgumentNullException.ThrowIfNull(canChange);

        // KindOf, not the raw byte: a record saved while its subject was in a party carries the
        // in-party flag in the top bit, and GetType() masks it off before comparing.
        if (EventNpc.KindOf(who.Record) == MonsterType)
        {
            refusal = ClassChangeRefusal.Monster;
            return [];
        }

        if (race is null)
        {
            refusal = ClassChangeRefusal.RaceUnknown;
            return [];
        }

        if (race.CanChangeClass == 0)
        {
            refusal = ClassChangeRefusal.RaceCannot;
            return [];
        }

        if (IsDualClass(who))
        {
            refusal = ClassChangeRefusal.AlreadyDualClassed;
            return [];
        }

        List<string> options =
        [
            .. classIds.Where(id => !string.Equals(id, who.ClassId, StringComparison.Ordinal)
                                    && canChange(who.ClassId, id)),
        ];

        refusal = options.Count > 0 ? ClassChangeRefusal.None
                                    : ClassChangeRefusal.NoClassQualifies;
        return options;
    }

    /// <summary>Whether the party menu's CHANGE CLASS entry lights up.</summary>
    /// <remarks>
    /// <b>The entry is dark outside a training hall whatever the answer here</b>
    /// (<c>RunEvent.cpp:2522</c>) — the same gate that darkens TRAIN, applied before the list is
    /// ever built.
    /// </remarks>
    public static bool CanChangeClass(Character who, RaceRecord? race,
                                      IEnumerable<string> classIds,
                                      Func<string, string, bool> canChange) =>
        Options(who, race, classIds, canChange, out _).Count > 0;

    /// <summary>
    /// Changes the character's class (<c>CHARACTER::HumanChangeClass</c>, <c>Char.cpp:7770</c>).
    /// </summary>
    /// <param name="newBaseclasses">The new class's baseclass list, in its own order.</param>
    /// <remarks>
    /// <para>
    /// <b>Every existing baseclass drops to level 0 and keeps its old level as its previous
    /// one.</b> Nothing is removed — a fighter who becomes a cleric still has a fighter row, at
    /// level 0 with <c>previousLevel</c> set — and that row is what
    /// <see cref="IsDualClass"/> then finds, which is what makes the change one-way.
    /// </para>
    /// <para>
    /// <b>Experience is not reset.</b> Only rows added here start at zero; the old ones keep
    /// everything they had earned.
    /// </para>
    /// <para>
    /// <b>Every carried item is unreadied</b>, whether or not the new class could have used it.
    /// </para>
    /// <para>
    /// <b>The reference's duplicate check reads the wrong index.</b> Its inner loop tests
    /// <c>PeekBaseclassStats(i)</c> — the <i>outer</i> loop's counter — where <c>j</c> is plainly
    /// meant, so it compares one arbitrary row instead of searching them all, and reads out of
    /// bounds as soon as the new class has more baseclasses than the character has rows. The
    /// intent is unambiguous and the buggy form has no defined behaviour to transcribe, so this
    /// searches properly.
    /// </para>
    /// <para>
    /// Two script hooks bracket all of it — <c>CHANGE_CLASS_FROM</c> on the class being left and
    /// <c>CHANGE_CLASS_TO</c> on the one being joined. Neither changes anything the engine does;
    /// both are the scripting phase.
    /// </para>
    /// </remarks>
    public static void Apply(Character who, string newClassId,
                             IEnumerable<string> newBaseclasses)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(newClassId);
        ArgumentNullException.ThrowIfNull(newBaseclasses);

        foreach (var progress in who.Baseclasses)
        {
            progress.PreviousLevel = progress.CurrentLevel;
            progress.CurrentLevel = 0;
        }

        who.ClassId = newClassId;

        foreach (string baseclassId in newBaseclasses)
        {
            if (who.Baseclass(baseclassId) is not null)
            {
                continue;
            }

            who.AddBaseclass(new BaseclassProgress(baseclassId, currentLevel: 1,
                                                   previousLevel: 0, experience: 0));
        }

        for (int i = 0; i < who.Items.Count; i++)
        {
            who.Items[i] = who.Items[i] with { ReadyLocation = ReadiedLocation.NotReady };
        }
    }

    /// <summary>
    /// <c>CHARACTER::IsDualClass</c> (<c>Char.cpp:7454</c>): any baseclass with a previous level.
    /// </summary>
    /// <remarks>
    /// The seven <c>prevXxxLevel</c> fields it used to test are commented out; what stands is a
    /// sweep of <c>baseclassStats</c> for a non-zero <c>previousLevel</c>, which is the field
    /// changing class sets.
    /// </remarks>
    public static bool IsDualClass(Character who)
    {
        ArgumentNullException.ThrowIfNull(who);

        return who.Baseclasses.Any(b => b.PreviousLevel > 0);
    }

    /// <summary>
    /// What <c>GetType()</c> returns for a monster (<c>MONSTER_TYPE</c>, <c>Externs.h:967</c>).
    /// </summary>
    /// <remarks>
    /// The three values are <c>CHAR_TYPE</c> 1, <c>NPC_TYPE</c> 2, <c>MONSTER_TYPE</c> 3 — so the
    /// obvious guess of 2 for a monster is an NPC.
    /// </remarks>
    public const byte MonsterType = (byte)CombatantKind.Monster;
}
