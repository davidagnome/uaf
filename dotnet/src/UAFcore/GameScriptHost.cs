using UAF.Rules;
using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The engine behind a GPDL script (<c>IGpdlHost</c> against real game state).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GpdlUnhostedEnvironment"/> is the VM's own stand-in, useful for testing the bytecode
/// and nothing else. This is the first host backed by a running game: a script's attribute reads
/// and writes reach the design's global store and the party's own.
/// </para>
/// <para>
/// <b>What this host answers.</b> The attribute stores, the whole character block, the party
/// block, the combat queries against a live <see cref="CombatSession"/>, the database reads, and
/// both collection walks.
/// </para>
/// <para>
/// <b>What is still unhosted.</b> Everything inherited from the base that has no game behind it —
/// discourse, <c>$GREP</c> — and the sub-opcodes the VM itself refuses with a citation, of which
/// the aura family is the largest group. Three item fields
/// (<c>m_priorityAI</c>, <c>RangeMedium</c>, <c>RangeShort</c>) answer zero because they are
/// engine-side members this port's item record does not carry.
/// </para>
/// </remarks>
public sealed class GameScriptHost(Game game) : GpdlUnhostedEnvironment
{
    private readonly Game game = game ?? throw new ArgumentNullException(nameof(game));

    private AttributeList Store(GpdlAslScope scope) =>
        scope == GpdlAslScope.Party ? game.Party.Attributes : game.Globals;

    /// <inheritdoc/>
    public override string GetAsl(GpdlAslScope scope, string key) =>
        Store(scope).Find(key) ?? string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Written with no flags, as the reference does — see <see cref="IGpdlHost.SetAsl"/>. It still
    /// saves; only read-only entries are held back.
    /// </remarks>
    public override void SetAsl(GpdlAslScope scope, string key, string value) =>
        Store(scope).Insert(key, value);

    /// <inheritdoc/>
    public override bool HasAsl(GpdlAslScope scope, string key) =>
        Store(scope).Entry(key) is not null;

    /// <inheritdoc/>
    public override void DeleteAsl(GpdlAslScope scope, string key) =>
        Store(scope).Remove(key);

    /// <summary>
    /// Resolves the actor string a script pushed to one of the party.
    /// </summary>
    /// <remarks>
    /// <b>By character id, which is the unique id the reference settled on.</b> A dated comment
    /// records the change: "almost all functions use the uniqueID of the character rather than the
    /// party index. I decided that the few exceptions should be treated as 'bugs'"
    /// (<c>GPDLexec.cpp:1845</c>). The <i>combat order</i> alternative the same comment mentions is
    /// not resolved here — a script naming a combatant by its place in the fight finds nobody.
    /// <para>
    /// A name that resolves to nobody is not an error here. The reference puts a message box in
    /// front of the player and returns a null character whose store swallows the write
    /// (<c>GPDLexec.cpp:908</c>); a design with a typo'd actor therefore limps rather than stops,
    /// and it does the same here without the dialog.
    /// </para>
    /// </remarks>
    private Character? Resolve(string actor) =>
        game.Party.Members.FirstOrDefault(
            m => string.Equals(m.CharacterId, actor, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(m.Name, actor, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public override string GetCharAsl(string actor, string key) =>
        Resolve(actor)?.Attributes.Find(key) ?? string.Empty;

    /// <inheritdoc/>
    public override void SetCharAsl(string actor, string key, string value) =>
        Resolve(actor)?.Attributes.Insert(key, value);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>An integer stat is formatted plainly, because GPDL's stack holds only strings.</b> The
    /// reference pushes through <c>m_pushInteger1</c>, which does the same conversion — a script
    /// comparing a stat against a literal is comparing text.
    /// </remarks>
    public override string GetCharStat(string actor, GpdlCharStat stat)
    {
        // The sixteen creature traits are answered before anything else, because they are the one
        // family that reads off a COMBATANT rather than a Character -- the flags live on the
        // monster record, and Resolve only ever finds party members.
        if (GpdlCharStats.IsTrait(stat))
        {
            return Trait(actor, stat);
        }

        if (Resolve(actor) is not { } character)
        {
            return string.Empty;
        }

        return stat switch
        {
            GpdlCharStat.Name => character.Name,
            GpdlCharStat.HitPoints => Text(character.AdjustedHitPoints),
            GpdlCharStat.MaxHitPoints => Text(character.MaxHitPoints),
            GpdlCharStat.ArmorClass => Text(character.ArmorClass),
            GpdlCharStat.AdjustedArmorClass => Text(character.AdjustedArmorClass),
            GpdlCharStat.Thac0 => Text(character.Thac0),

            // No readied-item model on a character outside combat, so the two bonuses the
            // reference subtracts are zero here -- see Character.AdjustedThac0.
            GpdlCharStat.AdjustedThac0 => Text(character.AdjustedThac0()),
            GpdlCharStat.Experience => Text(character.TotalExperience),
            GpdlCharStat.ReadyToTrain => Text(character.ReadyToTrain ? 1 : 0),
            GpdlCharStat.Gender => Text((int)character.Gender),

            _ => AbilityLayers.Read(character, stat) is { } score ? Text(score) : string.Empty,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>Only the stats this port keeps as live state are written.</b> A <see cref="Character"/>
    /// reads age, movement, alignment, size, the two combat bonuses and the portrait index off its
    /// immutable record, so a script setting one of those changes nothing here where the reference
    /// would have changed it. The call still yields the empty string, as it does there — the
    /// reference's own setters end in <c>m_pushEmptyString</c> and report nothing either way, so a
    /// script cannot tell the difference from inside.
    /// </para>
    /// <para>
    /// <b>A value that is not a number is ignored rather than written as zero.</b> The reference
    /// pops through <c>m_popInteger1</c>, which is <c>atoi</c> and would give it a zero; refusing
    /// is the one deliberate divergence here, because silently zeroing a character's strength on a
    /// script typo is worse than doing nothing.
    /// </para>
    /// </remarks>
    public override void SetCharStat(string actor, GpdlCharStat stat, string value)
    {
        if (Resolve(actor) is not { } character
            || !int.TryParse(value, System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out int number))
        {
            return;
        }

        switch (stat)
        {
            case GpdlCharStat.HitPoints: character.HitPoints = number; break;
            case GpdlCharStat.MaxHitPoints: character.MaxHitPoints = number; break;
            case GpdlCharStat.Morale: character.Morale = number; break;
            case GpdlCharStat.Status: character.Status = (CharacterStatus)number; break;

            default:
                if (AbilityLayers.PermanentScore(stat) is { } ability)
                {
                    character.Abilities = Written(character.Abilities, ability, number);
                }
                break;
        }
    }

    // ---- the databases --------------------------------------------------------------------------

    /// <inheritdoc/>
    public override string ItemField(string itemId, GpdlItemField field)
    {
        if (game.Design.Item(itemId) is not { } item)
        {
            return string.Empty;
        }

        return field switch
        {
            GpdlItemField.CommonName => item.Names.UniqueName,
            GpdlItemField.IdName => item.Names.IdName,
            GpdlItemField.MaxRange => Text(item.Tail.RangeMax),
            GpdlItemField.AttackBonus => Text(item.Scalars.AttackBonus),

            // "$dice$sides$bonus" -- the delimiter leads, as it does for a class's baseclasses.
            GpdlItemField.DamageSmall =>
                $"${item.Combat.NbrDiceSm}${item.Combat.DmgDiceSm}${item.Combat.DmgBonusSm}",
            GpdlItemField.DamageLarge =>
                $"${item.Combat.NbrDiceLg}${item.Combat.DmgDiceLg}${item.Combat.DmgBonusLg}",

            // The AI priority and the two shorter ranges are read from members this port's item
            // record does not carry -- m_priorityAI, RangeMedium and RangeShort are engine-side
            // fields rather than serialized ones. Zero rather than a guess.
            _ => Text(0),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Rolled on every call</b>, as the reference rolls the race's dice — two asks about the
    /// same character give two answers.
    /// </remarks>
    public override int RaceMeasurement(string actor, bool weight)
    {
        if (Resolve(actor) is not { } character
            || game.Design.Races?.GetValueOrDefault(character.Race) is not { } race)
        {
            return 0;
        }

        return NewCharacter.Roll(weight ? race.Weight : race.Height,
                                 (count, sides) => DiceExpression.Roll(count, sides, game.Dice),
                                 _ => null, out _) ?? 0;
    }

    /// <inheritdoc/>
    public override int BaseclassProgression(string baseclassId, int value, bool wantExperience) =>
        game.Design.Baseclasses?.GetValueOrDefault(baseclassId) is { } baseclass
            ? BaseclassTable.Read(baseclass.ExperienceLevels, value, wantExperience)
            : 0;

    /// <inheritdoc/>
    public override string ClassBaseclasses(string classId)
    {
        if (game.Design.Classes?.GetValueOrDefault(classId) is not { } found)
        {
            return string.Empty;
        }

        // The delimiter leads: one baseclass is "$fighter", never a bare name.
        var text = new System.Text.StringBuilder();
        foreach (string baseclass in found.Baseclasses)
        {
            text.Append('$').Append(baseclass);
        }

        return text.ToString();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The loop counts down, and only the last answer survives.</b> `for (i = numCharacters-1;
    /// i >= 0; i--)` with `result =` on every pass — so what comes back is party member <i>zero</i>'s
    /// answer, and everyone else's is overwritten. A design using this for a yes/no test is really
    /// asking the first member.
    /// </para>
    /// <para>
    /// <b>One context frame for the whole walk, not one per member.</b> Each member's character
    /// context replaces the previous rather than nesting, so a script cannot reach the member
    /// before it.
    /// </para>
    /// <para>
    /// <b>It sets a source type the name lookup cannot name.</b>
    /// <c>ScriptSourceType_ForEachPrtyMember</c> has no case in <c>GetSourceTypeName</c>
    /// (<c>Specab.cpp:231</c>), so a script asking <c>$SA_SOURCE_TYPE()</c> inside this walk is
    /// told "Unknown".
    /// </para>
    /// </remarks>
    public override string ForEachPartyMember(string ability, string script)
    {
        string result = string.Empty;

        using var frame = Context.Push();
        Context.Source = GpdlScriptSource.Unknown;

        for (int i = game.Party.Count - 1; i >= 0; i--)
        {
            Context.Set(GpdlContext.Combatant, game.Party.Members[i].CharacterId);
            result = game.Scripts.Run(ability, script, this);
        }

        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>An actor that names nobody carries nothing</b>, which is the same answer as an empty
    /// pack — the reference errors into the interpreter and pushes false, and there is no
    /// interpreter error channel here.
    /// </remarks>
    public override string ForEachPossession(string actor, string script) =>
        Resolve(actor) is { } who
            ? PossessionWalk.Run(who.Items, script, game.Design.Item, game.Scripts, this)
            : string.Empty;

    // ---- combat ---------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool InCombat => game.InCombat;

    /// <inheritdoc/>
    public override int CombatRound => game.Combat?.Round.Round ?? 0;

    /// <summary>
    /// The combatant an actor string names, or null.
    /// </summary>
    /// <remarks>
    /// <b>By list index, which is what an actor string carries for a combatant.</b> The reference
    /// packs a source flag and an instance into <c>ActorType</c> and unpacks it with
    /// <c>m_StringToActor</c>; the port has no fight-independent combatant identity, so the index
    /// is the identity — and it is only meaningful while this fight is running.
    /// </remarks>
    private Combatant? Fighter(string actor) =>
        game.Combat is { } session
        && int.TryParse(actor, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int index)
        && index >= 0 && index < session.Combatants.Count
            ? session.Combatants[index]
            : null;

    /// <summary>
    /// One of the sixteen creature traits, for whoever the actor names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Anything that is not a monster in this fight gets the reference's literal.</b> Every
    /// accessor on <c>CHARACTER</c> tests <c>GetType() == MONSTER_TYPE</c> first and returns a
    /// constant otherwise — and two of those constants are <c>TRUE</c>, so falling through to
    /// "false" would make hold-person and charm fail against the party. A party member, an
    /// unresolvable actor and a combatant outside combat all take that path.
    /// </para>
    /// <para>
    /// The four bitfields have <b>overlapping values</b> and cannot be merged: bit 2 is
    /// <c>FormAnimal</c> in one and <c>CanBeHeldCharmed</c> in another, so each trait has to name
    /// its own field as well as its own bit.
    /// </para>
    /// </remarks>
    private string Trait(string actor, GpdlCharStat stat)
    {
        if (Fighter(actor) is not { Kind: CombatantKind.Monster } monster)
        {
            return GpdlCharStats.NonMonsterTrait(stat);
        }

        (uint Field, uint Bit) test = stat switch
        {
            GpdlCharStat.IsMammal => (monster.FormType, FormMammal),
            GpdlCharStat.IsAnimal => (monster.FormType, FormAnimal),
            GpdlCharStat.IsSnake => (monster.FormType, FormSnake),
            GpdlCharStat.IsGiant => (monster.FormType, FormGiant),
            GpdlCharStat.IsAlwaysLarge => (monster.FormType, FormLarge),

            GpdlCharStat.HasPoisonImmunity => (monster.ImmunityType, ImmunePoison),
            GpdlCharStat.HasDeathImmunity => (monster.ImmunityType, ImmuneDeath),
            GpdlCharStat.HasConfusionImmunity => (monster.ImmunityType, ImmuneConfusion),
            GpdlCharStat.HasVorpalImmunity => (monster.ImmunityType, ImmuneVorpal),

            GpdlCharStat.HasDwarfArmorClassPenalty => (monster.PenaltyType, PenaltyDwarfAc),
            GpdlCharStat.HasGnomeArmorClassPenalty => (monster.PenaltyType, PenaltyGnomeAc),
            GpdlCharStat.HasDwarfThac0Penalty => (monster.PenaltyType, PenaltyDwarfThac0),
            GpdlCharStat.HasGnomeThac0Penalty => (monster.PenaltyType, PenaltyGnomeThac0),
            GpdlCharStat.HasRangerDamagePenalty => (monster.PenaltyType, PenaltyRangerDamage),

            GpdlCharStat.CanBeHeldOrCharmed => (monster.MiscOptionsType, OptionCanBeHeldCharmed),
            _ => (monster.MiscOptionsType, OptionAffectedByDispelEvil),
        };

        return (test.Field & test.Bit) == test.Bit ? "1" : "0";
    }

    // MonsterFormType (Monster.h:60).
    private const uint FormMammal = 1;
    private const uint FormAnimal = 2;
    private const uint FormSnake = 4;
    private const uint FormGiant = 8;
    private const uint FormLarge = 16;

    // MonsterPenaltyType (Monster.h:87).
    private const uint PenaltyDwarfAc = 1;
    private const uint PenaltyGnomeAc = 2;
    private const uint PenaltyDwarfThac0 = 4;
    private const uint PenaltyGnomeThac0 = 8;
    private const uint PenaltyRangerDamage = 16;

    // MonsterImmunityType (Monster.h:110) -- note poison is 1 and death is 2, which is the
    // opposite of the order the $GET_HAS*IMMUNITY calls are declared in.
    private const uint ImmunePoison = 1;
    private const uint ImmuneDeath = 2;
    private const uint ImmuneConfusion = 4;
    private const uint ImmuneVorpal = 8;

    // MonsterMiscOptionsType (Monster.h:126).
    private const uint OptionCanBeHeldCharmed = 1;
    private const uint OptionAffectedByDispelEvil = 2;

    private static string ActorOf(Combatant? who) =>
        who is null ? string.Empty : Text(who.Index);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>In a fight the index is the combat order; out of one there is no number to give.</b> The
    /// reference answers a character's <i>unique id</i> outside combat — a number packed into
    /// <c>ActorType</c> — but the port identifies a party member by <c>CharacterId</c>, a string,
    /// and there is no integer behind it to report. Rather than invent one (a party position would
    /// be a different quantity wearing the same name, and the reference's own comment warns against
    /// exactly that confusion), a party member outside combat answers
    /// <see cref="GpdlActorIndex.InvalidContext"/>.
    /// </para>
    /// <para>
    /// <b>So <c>$IndexOf</c> is usable in combat and honest outside it.</b> A design relying on the
    /// out-of-combat number gets the literal rather than a plausible wrong index — which is the
    /// failure a script can actually notice.
    /// </para>
    /// </remarks>
    public override string IndexOf(string actor) =>
        Fighter(actor) is { } who
            ? Text(who.Index)
            : GpdlActorIndex.InvalidContext;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The text is measured but not yet drawn, and that asymmetry is deliberate.</b> The width
    /// is what the layout depends on — every column on a sheet is placed relative to how wide the
    /// last thing was — so measuring correctly is what makes a script's positions mean anything.
    /// Where the glyphs land on screen belongs with the character-sheet screen, which still renders
    /// through <see cref="CharacterSheetBuilder"/> rather than through a script.
    /// </para>
    /// <para>
    /// So a script can lay a sheet out and the port agrees with the reference about where
    /// everything goes; nothing appears until the sheet screen is driven from GPDL. The calls are
    /// recorded on <c>Drawn</c> either way, which is what a test reads.
    /// </para>
    /// <para>
    /// A design with no rasterizer measures zero, like the unhosted environment — the port's
    /// standing contract that a missing font degrades to no text rather than to a guess.
    /// </para>
    /// </remarks>
    public override int DrawText(string text, int x, int y, int color)
    {
        base.DrawText(text, x, y, color);

        return game.Design.Font(game.Design.RequestedFontHeight) is { } font
            ? font.GetTextWidth(text ?? string.Empty)
            : 0;
    }

    /// <inheritdoc/>
    public override string CombatantState(string actor) =>
        Fighter(actor) is { } who ? who.State.ToString() : string.Empty;

    /// <inheritdoc/>
    public override int CombatantLocation(int combatant, string axis)
    {
        if (game.Combat is not { } session
            || combatant < 0 || combatant >= session.Combatants.Count)
        {
            return -1;
        }

        // Only "X" is tested; anything else falls through to Y.
        return axis == "X" ? session.Combatants[combatant].X : session.Combatants[combatant].Y;
    }

    /// <inheritdoc/>
    public override int AvailableAttacks(string actor, int function, int value)
    {
        if (Fighter(actor) is not { } who)
        {
            return 0;
        }

        switch (function)
        {
            case 0: who.AvailableAttacks = value; break;
            case 1: who.AvailableAttacks += value; break;
        }

        return (int)who.AvailableAttacks;
    }

    /// <inheritdoc/>
    public override void TeleportCombatant(int combatant, int x, int y)
    {
        if (game.Combat is { } session
            && combatant >= 0 && combatant < session.Combatants.Count)
        {
            session.Combatants[combatant].X = x;
            session.Combatants[combatant].Y = y;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b><c>LAST_ATTACKER_OF</c> is not ported.</b> The port keeps no per-combatant record of who
    /// struck last, and inventing one would be a rule rather than a transcription. It answers the
    /// null actor, which is also what the reference answers out of combat. The other two are
    /// <see cref="CombatSelectors"/>, quirks and all.
    /// </remarks>
    public override string NearestTo(string actor, GpdlCombatantQuery query)
    {
        if (game.Combat is not { } session || Fighter(actor) is not { } from)
        {
            return NullActor;
        }

        return ActorOf(query switch
        {
            GpdlCombatantQuery.Nearest => CombatSelectors.Nearest(session.Combatants, from),
            GpdlCombatantQuery.NearestEnemy =>
                CombatSelectors.NearestEnemy(session.Combatants, from),
            _ => null,
        });
    }

    /// <inheritdoc/>
    public override string MostDamaged(GpdlDamageQuery query)
    {
        if (game.Combat is not { } session)
        {
            return NullActor;
        }

        bool friendly = query is GpdlDamageQuery.MostDamagedFriendly
                              or GpdlDamageQuery.LeastDamagedFriendly;

        bool lowest = query is GpdlDamageQuery.MostDamagedEnemy
                            or GpdlDamageQuery.MostDamagedFriendly;

        return ActorOf(CombatSelectors.ByHitPoints(session.Combatants, friendly, lowest));
    }

    // ---- the party ------------------------------------------------------------------------------

    /// <summary>Ten thousand and forty minutes in a day, as the reference splits them.</summary>
    private const int MinutesPerHour = 60;

    /// <inheritdoc/>
    public override string GetPartyValue(GpdlPartyValue value) => value switch
    {
        GpdlPartyValue.Days => Text(game.Minutes / (24 * MinutesPerHour)),
        GpdlPartyValue.Hours => Text(game.Minutes / MinutesPerHour % 24),
        GpdlPartyValue.Minutes => Text(game.Minutes % MinutesPerHour),
        GpdlPartyValue.Time => Text(game.Minutes),
        GpdlPartyValue.Size => Text(game.Party.Count),
        GpdlPartyValue.Facing => Text((int)game.Facing),

        // The uniquePartyID, not the index -- see IGpdlHost.GetPartyValue.
        _ => game.Party.Active is { } who ? who.Record.UniquePartyId.ToString() : "0",
    };

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The clock is one number here, so the three fields are not independently writable.</b>
    /// The reference holds days, hours and minutes as separate ints and lets a script set any of
    /// them past its range; <see cref="Game.Minutes"/> is a single total, so setting hours means
    /// rewriting the whole clock and an out-of-range hour folds into the day rather than being
    /// held. Named rather than worked around: the reference's unclamped fields are visible only
    /// through the same three getters, so a script that writes 99 hours and reads them back would
    /// see 99 there and 3 here.
    /// </remarks>
    public override void SetPartyValue(GpdlPartyValue value, string setting)
    {
        if (!int.TryParse(setting, System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out int number))
        {
            return;
        }

        switch (value)
        {
            case GpdlPartyValue.Time:
                game.SetMinutes(number);
                break;

            case GpdlPartyValue.Days:
                game.SetMinutes(number * 24 * MinutesPerHour
                                + game.Minutes % (24 * MinutesPerHour));
                break;

            case GpdlPartyValue.Hours:
                game.SetMinutes(game.Minutes / (24 * MinutesPerHour) * 24 * MinutesPerHour
                                + number * MinutesPerHour
                                + game.Minutes % MinutesPerHour);
                break;

            case GpdlPartyValue.Minutes:
                game.SetMinutes(game.Minutes / MinutesPerHour * MinutesPerHour + number);
                break;

            case GpdlPartyValue.ActiveCharacter:
                // Modulo, so an out-of-range index wraps rather than being refused.
                game.Party.ActiveCharacter =
                    game.Party.Count > 0 ? ((number % game.Party.Count) + game.Party.Count)
                                           % game.Party.Count
                                         : 0;
                break;

            case GpdlPartyValue.Facing:
                game.SetFacing((Facing)Math.Clamp(number, 0, 7));
                break;
        }
    }

    /// <inheritdoc/>
    public override string PartyLocation =>
        $"/{game.LevelIndex + 1}/{game.X}/{game.Y}";

    /// <inheritdoc/>
    public override int MoneyAvailable(int coinType)
    {
        double total = game.Party.Members.Sum(m => m.Purse.Total());

        if (coinType == 0)
        {
            return (int)total;
        }

        if (coinType is < 1 or > MoneyRules.MaxCoinTypes)
        {
            return 0;
        }

        return (int)game.Money.Convert(total, game.Money.BaseType,
                                       MoneyRules.ClassOf(coinType - 1));
    }

    /// <summary>
    /// The GPDL source an ability holds for a named script, or null.
    /// </summary>
    /// <remarks>
    /// <b>The design's abilities carry bare bodies, not whole functions.</b> The commented sample
    /// at the top of <c>specialAbilities.txt</c> shows a full <c>$PUBLIC $FUNC</c>, but every real
    /// entry is statements alone — which is why <see cref="SpecialAbilityScripts.Wrap"/> exists
    /// and why running one without it compiles nothing.
    /// </remarks>
    private string? AbilityScript(string abilityName, string scriptName) =>
        game.Design.SpecialAbilities
            .FirstOrDefault(a => string.Equals(a.Name, abilityName, StringComparison.Ordinal))
            ?.Script(scriptName);

    /// <summary>The ability names an object's specab block carries.</summary>
    private static IEnumerable<string> AbilityNames(SpecabBlock? block) =>
        block?.Pairs.Select(p => p.Key) ?? [];

    /// <inheritdoc/>
    public override string RunCharacterScripts(string actor, string scriptName)
    {
        if (Resolve(actor) is not { } character)
        {
            return string.Empty;
        }

        return SpecialAbilityScripts.Run(
            AbilityNames(character.Record.SpecialAbilities), AbilityScript, scriptName, this,
            onError: (ability, message) =>
                DebugLog.Add($"* * * * Script Error in {ability}[{scriptName}]: {message}"),

            // What the script is running for: $CharacterContext reads this back.
            contexts: new Dictionary<GpdlContext, string> { [GpdlContext.Character] = actor });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The spells affecting the character, not the character's own abilities</b> — and the
    /// results are <b>concatenated</b> rather than overwritten, which is the one place in the
    /// family that accumulates (<c>Char.cpp:11576</c>, <c>result += …</c>).
    /// </remarks>
    public override string RunSpellEffectScripts(string actor, string scriptName)
    {
        if (EffectsOf(actor) is not { } effects)
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder();

        foreach (var effect in effects.Effects)
        {
            if (SourceSpell(effect) is not { } spell)
            {
                continue;
            }

            // Both contexts: the spell whose abilities these are, and the character it is on.
            result.Append(SpecialAbilityScripts.Run(
                AbilityNames(spell.SpecialAbilities), AbilityScript, scriptName, this,
                contexts: new Dictionary<GpdlContext, string>
                {
                    [GpdlContext.Character] = actor,
                    [GpdlContext.Spell] = effect.SourceSpell,
                }));
        }

        return result.ToString();
    }

    /// <inheritdoc/>
    public override string CallGlobalScript(string abilityName, string scriptName) =>
        SpecialAbilityScripts.Run([abilityName], AbilityScript, scriptName, this);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Read without consuming.</b> <c>DesignConfig.TryGetValue</c> defaults to consuming the
    /// entry so that a loader can report what nothing claimed; a script asking twice must get the
    /// same answer both times, so this asks not to.
    /// </remarks>
    public override string ConfigValue(string token) =>
        !string.IsNullOrEmpty(token)
        && game.Design.Config.TryGetValue(token, out string value, consume: false)
            ? value
            : string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>A spell the design has no record of is refused before the character is touched.</b>
    /// Teaching one would put a name in a spellbook that nothing could ever cast.
    /// </remarks>
    public override bool KnowSpell(string actor, string spellId, bool know)
    {
        if (Resolve(actor) is not { } character
            || string.IsNullOrEmpty(spellId)
            || game.Design.Spell(spellId) is not { } spell)
        {
            return false;
        }

        if (know)
        {
            character.Book.Add(spellId, spell.Level);
            return true;
        }

        return character.Book.Remove(spellId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Matches on the effect's source, the same handle
    /// <see cref="RemoveCharacterModification"/> uses — but takes <b>every</b> match rather than
    /// one, because this names a script rather than a single change.
    /// </remarks>
    public override string RemoveSpellEffect(string actor, string scriptName)
    {
        if (EffectsOf(actor) is not { } effects || string.IsNullOrEmpty(scriptName))
        {
            return string.Empty;
        }

        int removed = effects.RemoveWhere(
            e => string.Equals(e.SourceSpell, scriptName, StringComparison.OrdinalIgnoreCase));

        return removed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public override void DumpCharacterSpecialAbilities(string actor)
    {
        if (Resolve(actor) is not { } character)
        {
            return;
        }

        foreach (var entry in character.Attributes.Entries)
        {
            DebugLog.Add(
                $" Character Special Ability = {entry.Key}; value = {entry.Value}");
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Not implemented: this port has no current-event picture to set.</b> The reference
    /// reaches for <c>m_pGPDLevent-&gt;pic</c> and returns early when there is no event running —
    /// which is the state this port is always in, so the early return is the whole behaviour.
    /// </remarks>
    public override void SmallPicture(string fileName)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Deliberately does nothing.</b> The reference calls <c>Sleep(ms)</c>, which blocks the
    /// whole game — including its rendering and input. Reproducing that in a port whose engine
    /// drives itself from a loop would freeze the window; a script that asks to pause gets the
    /// same empty result and the game keeps running.
    /// </remarks>
    public override void Sleep(int milliseconds)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Matches the effect's source spell, and refuses a spell the design does not have</b> —
    /// the reference checks <c>IsValidSpell</c> before walking anything, so a typo answers false
    /// rather than searching for a name nothing can carry.
    /// </remarks>
    public override bool IsAffectedBySpell(string actor, string spellId)
    {
        if (EffectsOf(actor) is not { } effects
            || string.IsNullOrEmpty(spellId)
            || game.Design.Spell(spellId) is null)
        {
            return false;
        }

        return effects.Effects.Any(
            e => string.Equals(e.SourceSpell, spellId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The character's own attributes are the fallback, not an afterthought.</b> The effects
    /// are searched first — each one's source spell, and whether that spell's ASL holds the name —
    /// and only when none does is the character's own ASL consulted. So an innate attribute
    /// answers true with no spell involved at all.
    /// </remarks>
    public override bool IsAffectedBySpellAttribute(string actor, string attribute)
    {
        if (string.IsNullOrEmpty(attribute))
        {
            return false;
        }

        if (EffectsOf(actor) is { } effects)
        {
            foreach (var effect in effects.Effects)
            {
                if (SourceSpell(effect) is { } spell
                    && spell.Attributes.Any(a => string.Equals(
                        a.Key, attribute, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return Resolve(actor)?.Attributes.Find(attribute) is not null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The three flags the reference sets are what make this findable again.</b>
    /// <c>AddTemporaryEffect</c> (<c>Char.cpp:12389</c>) marks the effect cumulative, script-made
    /// and timed — and it is the <b>timed</b> one that
    /// <see cref="RemoveCharacterModification"/> filters on, so an effect missing it could never
    /// be removed by a script that added it.
    /// </para>
    /// <para>
    /// <b>The stop time is the party's clock plus the duration</b>, in minutes — the only unit the
    /// caller lets through. <c>FromScript</c> is set for the same reason the flag is: it shifts
    /// the expiry test by one, so a script effect and a spell effect of the same length do not
    /// expire on the same tick.
    /// </para>
    /// </remarks>
    public override void ModifyCharacterAttribute(string attribute, int amount, int minutes,
                                                  string text, string source)
    {
        if (Resolve(CurrentActor) is not { } character || string.IsNullOrEmpty(attribute))
        {
            return;
        }

        character.Effects.Add(new ActiveSpellEffect(
            new UAF.Rules.SpellEffect(attribute, amount,
                            SpellEffectFlags.Cumulative
                            | SpellEffectFlags.Script
                            | SpellEffectFlags.TimedSpecialAbility),
            StopTime: game.Minutes + minutes,
            FromScript: true,

            // The source is the effect's handle: RemoveCharacterModification matches on it.
            SourceSpell: source));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Only timed effects are candidates, and only one goes.</b> The reference walks the list,
    /// skips anything without <c>EFFECT_TIMEDSA</c>, and returns on the first match — so a script
    /// that added three has to call this three times.
    /// </remarks>
    public override bool RemoveCharacterModification(string mask)
    {
        if (Resolve(CurrentActor) is not { } character)
        {
            return false;
        }

        var effects = character.Effects.Effects;

        for (int i = 0; i < effects.Count; i++)
        {
            if ((effects[i].Effect.Flags & SpellEffectFlags.TimedSpecialAbility) != 0
                && GpdlMask.Matches(mask, effects[i].SourceSpell))
            {
                // RemoveWhere takes every match, so the predicate has to identify this one.
                int index = i;
                int taken = 0;
                character.Effects.RemoveWhere(_ => taken++ == index);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whose character these calls act on.
    /// </summary>
    /// <remarks>
    /// <b>The reference uses <c>Dude()</c> — the script's current character context — which this
    /// port does not model.</b> The party's active character is the nearest thing, and it is what
    /// <see cref="CoinCount"/> already uses for the same reason.
    /// </remarks>
    private string CurrentActor =>
        game.Party.Members.Count == 0
            ? string.Empty
            : game.Party.Members[
                Math.Clamp(game.Party.ActiveCharacter, 0, game.Party.Members.Count - 1)].Name;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Quests are addressed by name here and by id everywhere else.</b> The reference's
    /// <c>questData.GetStage</c> takes the key a script writes, so the name has to be resolved to
    /// an id before the world can answer — the same seam <c>GameLogicBlockHost</c> crosses.
    /// </remarks>
    public override int QuestStage(string quest) =>
        game.LogicBlockHost?.QuestStage(quest) ?? 0;

    /// <inheritdoc/>
    public override void SetQuestStage(string quest, int stage) =>
        game.LogicBlockHost?.SetQuestStage(quest, stage);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>One bundle, and only of an item the design has.</b> A name the item database does not
    /// carry is refused rather than conjured — the reference locates the item before adding it.
    /// </remarks>
    public override bool GiveItem(string actor, string itemId)
    {
        if (Resolve(actor) is not { } character
            || string.IsNullOrEmpty(itemId)
            || game.Design.Item(itemId) is not { } record)
        {
            return false;
        }

        character.Items.Add(new ItemInstance(
            Key: 0,
            ItemId: itemId,
            LegacyItemId: 0,
            ReadyLocation: ReadiedLocation.NotReady,
            Quantity: Math.Max(record.Scalars.BundleQty, 1),
            Identified: 1,
            Charges: record.Scalars.NumCharges,
            Cursed: (byte)record.Scalars.Cursed,
            Paid: 0));

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The first match goes.</b> The reference prefers a copy whose key matches the script
    /// context's item — a script running from an item takes that one rather than a duplicate — but
    /// this port has no item context on a script, so the first is the only rule it can apply.
    /// </remarks>
    public override bool TakeItem(string actor, string itemId)
    {
        if (Resolve(actor) is not { } character || string.IsNullOrEmpty(itemId))
        {
            return false;
        }

        int index = character.Items.FindIndex(
            i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return false;
        }

        character.Items.RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>A monster answers its own name, not a type.</b> The reference pushes <c>monsterID</c>
    /// for the monster case, so this call is a type test for characters and NPCs and an identity
    /// for monsters — which is why the two literals carry at-signs and the third does not.
    /// </remarks>
    public override string CharacterType(string actor)
    {
        if (Fighter(actor) is { } fighter)
        {
            return fighter.Kind switch
            {
                CombatantKind.Character => IGpdlHost.PlayerCharacterType,
                CombatantKind.Npc => IGpdlHost.NpcType,
                _ => fighter.Name,
            };
        }

        // Outside combat only the party is reachable, and every member of it is a character.
        return Resolve(actor) is null ? string.Empty : IGpdlHost.PlayerCharacterType;
    }

    /// <inheritdoc/>
    public override string CharacterRace(string actor) =>
        Resolve(actor) is { } character ? character.Race : IGpdlHost.NoSuchCharacter;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Refused when the design has no race by that name.</b> The reference looks the name up in
    /// <c>raceData</c> and leaves the character alone when it is not there — a script cannot
    /// invent a race by assigning one.
    /// </remarks>
    public override bool SetCharacterRace(string actor, string race)
    {
        if (Resolve(actor) is not { } character
            || string.IsNullOrEmpty(race)
            || game.Design.Races?.ContainsKey(race) != true)
        {
            return false;
        }

        character.Race = race;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Coin type 0 is not a denomination, it is "do not convert".</b> The same shape as
    /// <see cref="MoneyAvailable"/> — see there for why the conversion runs through the design's
    /// own base type rather than assuming one.
    /// </remarks>
    public override int VaultMoneyAvailable(int coinType)
    {
        double total = 0.0;

        for (int vault = 0; vault < GlobalVaults.Count; vault++)
        {
            total += game.Vaults.MoneyIn(vault)?.Total() ?? 0.0;
        }

        if (coinType == 0)
        {
            return (int)total;
        }

        if (coinType is < 1 or > MoneyRules.MaxCoinTypes)
        {
            return 0;
        }

        return (int)game.Money.Convert(total, game.Money.BaseType,
                                       MoneyRules.ClassOf(coinType - 1));
    }

    /// <summary>
    /// Per-level attributes a script has written, over the design's own.
    /// </summary>
    /// <remarks>
    /// <b>An overlay, because <c>LevelStats.Attributes</c> is immutable.</b> The reference writes
    /// straight into <c>globalData</c>'s level stats, where a saved game later picks them up; the
    /// port's record cannot be mutated in place, so reads fall through to the design and writes
    /// land here. <b>They do not survive a save</b> — persisting them belongs with Phase 4a's
    /// save-game work, and pretending otherwise would be worse than saying so.
    /// </remarks>
    private readonly Dictionary<int, Dictionary<string, string>> levelAsl = [];

    /// <inheritdoc/>
    public override void SetLevelAsl(int level, string key, string value)
    {
        if (!levelAsl.TryGetValue(level, out var attributes))
        {
            attributes = [];
            levelAsl[level] = attributes;
        }

        attributes[key] = value;
    }

    /// <inheritdoc/>
    /// <remarks>The overlay first, then whatever the design shipped for that level.</remarks>
    public override string GetLevelAsl(int level, string key)
    {
        if (levelAsl.TryGetValue(level, out var attributes)
            && attributes.TryGetValue(key, out string? written))
        {
            return written;
        }

        // The design's own: LevelInfo is keyed by the ONE-based level number, as the script sees
        // it, so no adjustment here.
        // NOTE: game.Globals is the ATTRIBUTE list; the design header is game.Design.Globals.
        if (game.Design.Globals.Levels?.Levels.TryGetValue((uint)level, out var stats) == true)
        {
            foreach (var entry in stats.Attributes)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
        }

        return string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Removes from the overlay only.</b> A key the design shipped comes back on the next read,
    /// which is the honest consequence of not owning the design's own list.
    /// </remarks>
    public override void DeleteLevelAsl(int level, string key)
    {
        if (levelAsl.TryGetValue(level, out var attributes))
        {
            attributes.Remove(key);
        }
    }

    /// <summary>
    /// Map overrides a script has written, over the design's own.
    /// </summary>
    /// <remarks>
    /// The same overlay arrangement as <see cref="levelAsl"/>, and for the same reason:
    /// <c>WallOverrides</c> is an immutable record. Reads fall through to the design; writes land
    /// here and <b>do not survive a save</b>.
    /// </remarks>
    private readonly Dictionary<(GpdlMapOverrideKind Kind, int Level, int X, int Y, int Facing),
                                int> mapOverrides = [];

    /// <summary>
    /// Folds a script's coordinates onto the level's grid, and finds the level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The wrap is the map's geometry, not a bounds check.</b> The reference takes x and y
    /// modulo the level's width and height and folds negatives back up, so <c>-1</c> is the last
    /// column and a coordinate past the edge comes round the other side. Doing this before the
    /// overlay is looked up is what keeps a wrapped write and an unwrapped read of the same square
    /// agreeing.
    /// </para>
    /// <para>
    /// <b>The level number a script writes is one-based; the table is keyed from zero.</b>
    /// <c>GetMapOverride</c> indexes <c>stats[parameters[0] - 1]</c>, and the reference comments the
    /// point twice over — "there is no level 0". The design's table is written out under the raw
    /// <c>stats[]</c> index (<c>GlobalData.cpp:3547</c>), so the subtraction happens here.
    /// <b>The level-attribute family does not do this</b>, and the difference is real: those
    /// sub-opcodes index <c>stats[level]</c> with no adjustment at all.
    /// </para>
    /// </remarks>
    private bool TryLocate(int level, ref int x, ref int y, ref int facing, out LevelStats? stats)
    {
        stats = null;

        // One-based, and a script may name a level that does not exist.
        if (level is < 1 or > 255)
        {
            return false;
        }

        facing = ((facing % 4) + 4) % 4;

        if (game.Design.Globals.Levels?.Levels.TryGetValue((uint)(level - 1), out stats) != true
            || stats is null || stats.Height <= 0 || stats.Width <= 0)
        {
            // No such level, or one with no extent: the coordinates cannot be wrapped, so they
            // stand as given and the read will simply find nothing.
            return true;
        }

        x = ((x % stats.Width) + stats.Width) % stats.Width;
        y = ((y % stats.Height) + stats.Height) % stats.Height;
        return true;
    }

    /// <inheritdoc/>
    public override int GetMapOverride(
        GpdlMapOverrideKind kind, int level, int x, int y, int facing)
    {
        if (!TryLocate(level, ref x, ref y, ref facing, out var stats))
        {
            return GpdlMapOverride.None;
        }

        if (mapOverrides.TryGetValue((kind, level, x, y, facing), out int written))
        {
            return written;
        }

        // The design's own.
        return stats?.Overrides?.At((int)kind, x, y, facing) ?? GpdlMapOverride.None;
    }

    /// <inheritdoc/>
    public override void SetMapOverride(
        GpdlMapOverrideKind kind, int level, int x, int y, int facing, int value)
    {
        if (!TryLocate(level, ref x, ref y, ref facing, out _))
        {
            return;
        }

        var square = (kind, level, x, y, facing);

        // 255 clears rather than stores, and anything above it clamps down to 255 -- so a script
        // writing a number too large for a byte erases the square.
        if (value >= GpdlMapOverride.None)
        {
            // An explicit "none" has to be RECORDED, not just removed: the design may ship an
            // override for this square, and dropping the entry would let the design's own value
            // come back on the next read.
            mapOverrides[square] = GpdlMapOverride.None;
            return;
        }

        mapOverrides[square] = value;
    }

    /// <inheritdoc/>
    public override int CurrentLevel => game.LevelIndex + 1;

    /// <inheritdoc/>
    public override string GameVersion =>
        game.Design.Globals.Version.Value.ToString(
            "F8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The spell an effect came from, or null when nothing names it.
    /// </summary>
    /// <remarks>
    /// <b>An effect knows its source spell by name, not its level.</b> The level lives on the
    /// spell record, so every level test in this family is a database lookup rather than a field
    /// read — and an effect whose spell the design no longer carries has no level at all, which is
    /// why the reference's own <c>pSpell != NULL</c> guard matters.
    /// </remarks>
    private SpellRecord? SourceSpell(ActiveSpellEffect effect) =>
        string.IsNullOrEmpty(effect.SourceSpell) ? null : game.Design.Spell(effect.SourceSpell);

    /// <summary>The effect list an actor carries, whether they are fighting or not.</summary>
    private SpellEffectList? EffectsOf(string actor) =>
        Fighter(actor)?.Effects ?? Resolve(actor)?.Effects;

    /// <summary>Whether an effect is one a spell put there.</summary>
    /// <remarks>
    /// Both sweeps consider only <c>EFFECT_SPELL</c> and <c>EFFECT_SPELLSPECAB</c>; a dispel adds
    /// item special abilities separately, at level 12.
    /// </remarks>
    private static bool FromSpell(ActiveSpellEffect effect) =>
        (effect.Effect.Flags & (SpellEffectFlags.Spell |
                                SpellEffectFlags.SpellSpecialAbility)) != 0;

    /// <inheritdoc/>
    public override int RemoveSpellEffects(string actor, int level)
    {
        if (EffectsOf(actor) is not { } effects)
        {
            return 0;
        }

        return effects.RemoveWhere(
            e => FromSpell(e) && SourceSpell(e) is { } spell && spell.Level <= level);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Level 12 is the item threshold and appears nowhere else.</b> A dispel at 12 or above
    /// takes item special abilities as well, whatever spell they came from and whether or not that
    /// spell could be dispelled.
    /// </remarks>
    public override int DispelSpellEffects(string actor, int level)
    {
        if (EffectsOf(actor) is not { } effects)
        {
            return 0;
        }

        return effects.RemoveWhere(e =>
            (FromSpell(e) && SourceSpell(e) is { } spell
                          && spell.Level <= level && spell.CanBeDispelled != 0)
            || (level >= ItemSpecialAbilityDispelLevel
                && (e.Effect.Flags & SpellEffectFlags.ItemSpecialAbility) != 0));
    }

    /// <summary>The level at which a dispel starts taking item special abilities.</summary>
    public const int ItemSpecialAbilityDispelLevel = 12;

    /// <inheritdoc/>
    public override bool RemoveItemCurses(string actor)
    {
        if (Resolve(actor) is not { } character)
        {
            return false;
        }

        for (int i = 0; i < character.Items.Count; i++)
        {
            if (character.Items[i].Cursed != 0)
            {
                character.Items[i] = character.Items[i] with { Cursed = 0 };
            }
        }

        return true;
    }

    /// <summary>The coin a one-based ordinal names, or null when it names none.</summary>
    /// <remarks>
    /// <b>Ordinal 0 is refused rather than wrapped.</b> The reference clamps an ordinal above the
    /// maximum back to 1 but never checks the lower bound, so <c>$COINCOUNT(0)</c> indexes
    /// <c>Coins[-1]</c> — a read behind the array. Refusing is the only defensible reading; there
    /// is no value to reproduce.
    /// </remarks>
    private Coin? CoinAt(int ordinal) =>
        ordinal is >= 1 and <= MoneyRules.MaxCoinTypes
            ? game.Money[MoneyRules.ClassOf(ordinal - 1)]
            : null;

    /// <inheritdoc/>
    public override string CoinName(int ordinal) => CoinAt(ordinal)?.Name ?? string.Empty;

    /// <inheritdoc/>
    public override double CoinRate(int ordinal) => CoinAt(ordinal)?.Rate ?? 0.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Counts what the <b>active character</b> carries, not the party — the reference reaches for
    /// <c>Dude()</c>, which is the script's current character context.
    /// </remarks>
    public override int CoinCount(int ordinal)
    {
        if (CoinAt(ordinal) is null || game.Party.Members.Count == 0)
        {
            return 0;
        }

        int index = Math.Clamp(game.Party.ActiveCharacter, 0, game.Party.Members.Count - 1);

        return game.Party.Members[index].Purse[MoneyRules.ClassOf(ordinal - 1)];
    }

    /// <inheritdoc/>
    public override bool IsInParty(string actor) => Resolve(actor) is not null;

    /// <inheritdoc/>
    public override void SetPartyXY(int x, int y) => game.QueuePartyMove(x, y);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The fight's own store, or the unhosted one when there is no fight.</b> The reference has
    /// a single global <c>combatData</c> whose aura list is simply empty out of combat; here the
    /// list belongs to the <see cref="CombatSession"/>, so out of combat the base class's store
    /// stands in. Either way an aura opcode outside combat finds nothing current and takes its
    /// error branch, which is what the reference does too.
    /// </remarks>
    public override AuraStore Auras => game.Combat?.Auras ?? base.Auras;

    /// <inheritdoc/>
    public override IAuraWorld AuraWorld =>
        game.Combat is { } session ? new CombatAuraWorld(session, this) : base.AuraWorld;

    /// <summary>
    /// A live fight, as an aura sees it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Facing is <c>MoveDirection</c>, not <c>Facing</c>.</b> The reference compares
    /// <c>m_iMoveDir</c> — the eight-way direction of the last step — where the port's
    /// <see cref="Combatant.Facing"/> only ever flips east or west for drawing. An aura attached
    /// with <c>CombatantFacing</c> turns with the walk, not with the sprite.
    /// </para>
    /// <para>
    /// <b>And it has to be translated, not cast.</b> <c>m_iMoveDir</c> holds <c>FACE_*</c> values
    /// (<c>Externs.h:1039</c>), which run <c>N, E, S, W, NW, NE, SW, SE</c>;
    /// <see cref="PathDirection"/> runs clockwise round the compass. The two agree only on north.
    /// Casting one to the other survives every equality test in the placement check and then
    /// rotates an annular wedge to the wrong quarter of the map.
    /// </para>
    /// </remarks>
    private sealed class CombatAuraWorld(CombatSession session, GameScriptHost host) : IAuraWorld
    {
        public int MapWidth => session.Map.Width;

        public int MapHeight => session.Map.Height;

        public int CombatantCount => session.Combatants.Count;

        public (int X, int Y, AuraFacing Facing) Combatant(int index) =>
            index >= 0 && index < session.Combatants.Count
                ? (session.Combatants[index].X, session.Combatants[index].Y,
                   Facing(session.Combatants[index].MoveDirection))
                : (-1, -1, AuraFacing.North);

        /// <inheritdoc cref="CombatAuraWorld"/>
        private static AuraFacing Facing(PathDirection direction) => direction switch
        {
            PathDirection.North => AuraFacing.North,
            PathDirection.NorthEast => AuraFacing.NorthEast,
            PathDirection.East => AuraFacing.East,
            PathDirection.SouthEast => AuraFacing.SouthEast,
            PathDirection.South => AuraFacing.South,
            PathDirection.SouthWest => AuraFacing.SouthWest,
            PathDirection.West => AuraFacing.West,
            PathDirection.NorthWest => AuraFacing.NorthWest,

            // PathDirection.None has no FACE_* counterpart. The reference initialises m_iMoveDir
            // to 0, which is FACE_NORTH, so an unmoved combatant faces north to an aura.
            _ => AuraFacing.North,
        };

        public (int Width, int Height) CombatantFootprint(int index) =>
            index >= 0 && index < session.Combatants.Count
                ? (session.Combatants[index].Icon.Width, session.Combatants[index].Icon.Height)
                : (1, 1);

        public AuraObstacle Obstacle(int x, int y) =>
            session.Map.Obstacle(x, y, 1, 1, checkOccupants: true,
                                 ignoreCombatant: CombatMap.NoDude) switch
            {
                ObstacleType.Wall => AuraObstacle.Wall,
                ObstacleType.Occupied => AuraObstacle.Occupied,
                ObstacleType.OffMap => AuraObstacle.OffMap,
                ObstacleType.LingeringSpell => AuraObstacle.LingeringSpell,
                _ => AuraObstacle.None,
            };

        public void RunAuraScript(Aura aura, string scriptName, int combatantIndex)
        {
            using var frame = host.Context.Push();

            if (combatantIndex >= 0 && combatantIndex < session.Combatants.Count)
            {
                // The reference sets the combatant context before the run so that the script can
                // ask who crossed the edge (Combatants.cpp:8785). The create hook has nobody.
                host.Context.Set(GpdlContext.Combatant, Text(combatantIndex));
            }

            // An aura's abilities are reached through $AURA_GetSA off the current aura, not through
            // the context's record lists -- so unlike the item and character walks there is nothing
            // to put on the context here.
            SpecabScripts.Run(Pairs(aura), scriptName, host.game.Scripts, host,
                              ScriptCallbacks.RunAll);
        }

        /// <summary>The aura's ability list in the order-preserving shape the runner wants.</summary>
        private static List<SpecabPair> Pairs(Aura aura) =>
            [.. aura.Abilities.Abilities.Select(a => new SpecabPair(a.Key, a.Value))];
    }

    /// <summary>One score replaced, since the scores are an immutable record.</summary>
    private static AbilityScores Written(AbilityScores scores, AbilityScore ability, int value) =>
        ability switch
        {
            AbilityScore.Strength => scores with { Strength = value },
            AbilityScore.StrengthMod => scores with { StrengthMod = value },
            AbilityScore.Intelligence => scores with { Intelligence = value },
            AbilityScore.Wisdom => scores with { Wisdom = value },
            AbilityScore.Dexterity => scores with { Dexterity = value },
            AbilityScore.Constitution => scores with { Constitution = value },
            _ => scores with { Charisma = value },
        };

    private static string Text(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
