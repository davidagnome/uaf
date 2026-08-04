using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The parts of <c>generateNewCharacter</c> (<c>Char.cpp:4278</c>) that a new character needs
/// beyond its rolled abilities.
/// </summary>
/// <remarks>
/// <para>
/// <b>One of its two paths is dead code that would crash.</b> The function handles
/// <c>START_EXP_VALUE</c> and then calls <c>die("Not Needed?")</c> for the "start experience is a
/// minimum level" case — so a design configured that way takes the reference down. Only the value
/// path is ported, because only the value path runs.
/// </para>
/// <para>
/// Age, weight and height all come from a race's <c>DICEPLUS</c>, which the reference compiles
/// through the GPDL toolchain. <see cref="DiceFormula"/> evaluates the subset every shipped
/// design actually uses and refuses the rest by name — see its remarks.
/// </para>
/// </remarks>
public static class NewCharacter
{
    /// <summary>How many days a birthday is rolled across (<c>RollDice(365,1)</c>).</summary>
    public const int DaysInYear = 365;

    /// <summary>
    /// A new character's purse (<c>getNewCharStartingMoney</c>, <c>Char.cpp:5636</c>).
    /// </summary>
    /// <remarks>
    /// <b>The field is called <c>StartPlatinum</c> and the coins are not platinum.</b> The amount
    /// goes in at <c>money.GetDefaultType()</c> — the design's own base denomination — so a design
    /// whose currency is copper starts its characters with that many copper pieces. The name is a
    /// leftover from when the denominations were fixed.
    /// </remarks>
    public static Purse StartingMoney(int coins, int gems, int jewelry, MoneyRules money)
    {
        ArgumentNullException.ThrowIfNull(money);

        var purse = new Purse(money);
        purse.Add(money.BaseType, coins);

        for (int g = 0; g < gems; g++)
        {
            purse.AddGem(new GemType(0, 0));
        }

        for (int j = 0; j < jewelry; j++)
        {
            purse.AddJewelry(new GemType(0, 0));
        }

        return purse;
    }

    /// <summary>
    /// A new character's kit (<c>getNewCharStartingEquip</c>, <c>Char.cpp:5652</c>).
    /// </summary>
    /// <remarks>
    /// <b>Copied off the class, wholesale.</b> There is no per-baseclass contribution and no
    /// merging — a multi-class character gets its class record's list and nothing from the
    /// baseclasses underneath it.
    /// </remarks>
    public static List<ItemInstance> StartingEquipment(ClassRecord? record) =>
        record is null ? [] : [.. record.StartingEquipment.Items];

    /// <summary>
    /// The baseclass rows a new character starts with (<c>Char.cpp:4310</c>).
    /// </summary>
    /// <remarks>
    /// <b>One row per baseclass of the class, all at level 1 with no experience</b> — the starting
    /// experience is given afterwards and the levelling walks up from there, so a character who
    /// begins above first level does so by being awarded experience and then trained, not by being
    /// created at that level.
    /// </remarks>
    public static List<BaseclassStats> BaseclassRows(ClassRecord? record) =>
        record is null
            ? []
            : [.. record.Baseclasses.Select(
                id => new BaseclassStats(id, CurrentLevel: 1, PreviousLevel: 0,
                                         PreDrainLevel: 0, Experience: 0))];

    /// <summary>
    /// A record with nothing in it, for the generator to build on.
    /// </summary>
    /// <remarks>
    /// <b>The only place this port constructs a <c>CharacterRecord</c> positionally.</b> Sixty-odd
    /// fields in an order that means nothing to a reader, so it happens once, here, and everything
    /// else uses <c>with</c> — the same shape <see cref="SaveGameProjection"/> uses to write a
    /// live character back over the record it came from.
    /// </remarks>
    public static CharacterRecord Blank { get; } = new(
        CharacterVersion: unchecked((int)CharacterRecordWriter.CharacterVersion),
        PreSpellNamesKey: 0,
        Type: 0, Race: "", Gender: 0, ClassId: "", Alignment: 0,
        AllowInCombat: 1, Status: 0, UndeadType: "", CreatureSize: 0,
        Name: "", CharacterId: "",
        Thac0: 20, Morale: 50, Encumbrance: 0, MaxEncumbrance: 0, ArmorClass: 10,
        HitPoints: 1, MaxHitPoints: 1, NumberOfHitDice: 1.0,
        Age: 0, MaxAge: 0, Birthday: 0, MaxCureDisease: 0,
        UnarmedDieSmall: 0, UnarmedNumberDieSmall: 0, UnarmedBonus: 0,
        UnarmedDieLarge: 0, UnarmedNumberDieLarge: 0,
        MaxMovement: 0, ReadyToTrain: 0, CanTradeItems: 1,
        Abilities: new AbilityScores(0, 0, 0, 0, 0, 0, 0),
        OpenDoors: 0, OpenMagicDoors: 0, BendBarsLiftGates: 0,
        HitBonus: 0, DamageBonus: 0, MagicResistance: 0,
        BaseclassStats: [], SkillAdjustments: [], SpellAdjustments: [],
        IsPreGenerated: 0, CanBeSaved: 1, HasLayedOnHandsToday: 0,
        Money: null, NumberOfAttacks: 1.0f,
        Icon: null, IconIndex: 0, OriginalIndex: 0, UniquePartyId: 0,
        DisableTalkIfDead: 0, TalkEvent: 0, TalkLabel: "",
        ExamineEvent: 0, ExamineLabel: "",
        SpellBook: new SpellBook(0, []), DetectingInvisible: 0, DetectingTraps: 0,
        SpellEffects: [], Blockages: [],
        SmallPic: new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        Items: new ItemList([], ReadyItems.Empty),
        SpecialAbilities: new SpecabBlock([], [], []),
        Attributes: []);

    /// <summary>
    /// Everything the generator collected, as the record a <c>.chr</c> is written from.
    /// </summary>
    /// <remarks>
    /// <b><c>CanBeSaved</c> and <c>IsPreGenerated</c> are what make this a player's character.</b>
    /// A pre-generated NPC is offered on the roster and refuses to serialize —
    /// <c>serializeCharacter</c> returns FALSE when <c>CanBeSaved</c> is clear — so a generated
    /// character has to declare itself the opposite of one.
    /// </remarks>
    public static CharacterRecord Assemble(CharacterCreation made, AbilityScores abilities,
                                           int maxHitPoints,
                                           IEnumerable<BaseclassStats> baseclasses,
                                           Purse money, IEnumerable<ItemInstance> equipment,
                                           int age, int maxAge, int birthday)
    {
        ArgumentNullException.ThrowIfNull(made);
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(baseclasses);
        ArgumentNullException.ThrowIfNull(money);
        ArgumentNullException.ThrowIfNull(equipment);

        return Blank with
        {
            Race = made.RaceId ?? "",
            Gender = (int)made.Gender,
            ClassId = made.ClassId ?? "",
            Alignment = made.Alignment,
            Name = made.CharacterName ?? "",
            Abilities = abilities,
            HitPoints = maxHitPoints,
            MaxHitPoints = maxHitPoints,
            BaseclassStats = [.. baseclasses],
            Money = money.ToRecord(),
            Items = new ItemList([.. equipment], ReadyItems.Empty),
            Age = age,
            MaxAge = maxAge,
            Birthday = birthday,

            // The icon and the portrait are file names, not art: the record holds a PIC_DATA
            // whose only field the generator fills is its filename.
            Icon = made.Icon is null
                ? null
                : new PicRecord(0, made.Icon, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            SmallPic = new PicRecord(0, made.SmallPicture ?? "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),

            // A player's character, not one the design shipped.
            IsPreGenerated = 0,
            CanBeSaved = 1,
        };
    }

    /// <summary>
    /// Rolls a race's dice field — its age, maximum age, weight, height or movement.
    /// </summary>
    /// <param name="male">
    /// What the expression's one identifier resolves to. Weight and height are the fields that
    /// use it, to add a gender bonus.
    /// </param>
    /// <returns>The rolled value, or null when the expression is empty or unsupported.</returns>
    /// <remarks>
    /// <b>Null is the reference's own "did not roll".</b> <c>RACE_DATA::GetStartAge</c> returns 0
    /// when <c>Roll</c> answers false, so an empty field and a refused one are the same answer
    /// there; keeping them distinct here is what lets a caller say which happened.
    /// </remarks>
    public static int? Roll(DicePlus? dice, Func<int, int, int> roll, bool male,
                            out string? unsupported)
    {
        unsupported = null;
        if (dice is null)
        {
            return null;
        }

        return DiceFormula.TryEvaluate(dice.Text, roll,
                                       n => n == DiceFormula.MaleSymbol ? (male ? 1 : 0) : null,
                                       out int value, out unsupported)
            ? value
            : null;
    }

    /// <summary>Rolls a birthday — a day of the year, 1 to 365.</summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    public static int Birthday(Func<int, int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(roll);
        return roll(1, DaysInYear);
    }

    /// <summary>
    /// A starting age, floored at the design's minimum (<c>determineCharStartAge</c>,
    /// <c>GameRules.cpp:2076</c>).
    /// </summary>
    /// <param name="rolledAge">
    /// What the race's age dice gave. Passed in because <c>DICEPLUS::Roll</c> is not ported — see
    /// the remarks on this class.
    /// </param>
    /// <remarks>
    /// <b>The floor only applies when it is positive.</b> <c>START_AGE</c> is design
    /// configuration and a zero or negative one leaves the race's roll alone rather than clamping
    /// everything to it.
    /// </remarks>
    public static int StartAge(int rolledAge, int minimumAge) =>
        minimumAge > 0 && rolledAge < minimumAge ? minimumAge : rolledAge;

    /// <summary>
    /// Caps an age at the character's maximum (<c>age = min(maxAge, age)</c>, <c>Char.cpp:4413</c>).
    /// </summary>
    /// <remarks>
    /// Applied after the floor, so a race whose maximum age is below the design's minimum
    /// starting age produces a character born at its own limit — the two clamps are not checked
    /// against each other.
    /// </remarks>
    public static int CapAge(int age, int maxAge) => Math.Min(age, maxAge);
}
