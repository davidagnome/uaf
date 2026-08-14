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
