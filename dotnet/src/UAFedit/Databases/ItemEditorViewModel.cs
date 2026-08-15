using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;

namespace UAFedit.Databases;

/// <summary>
/// The detail form for one <c>ITEM_DATA</c> record — the Avalonia replacement for
/// <c>CItemDBDlg</c> (<c>UAFWinEd/ItemDB.cpp</c>, dialog <c>IDD_ITEMDB</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It covers more than the MFC dialog did.</b> <c>IDD_ITEMDB</c> pushed six fields behind a
/// "Magical Props" modal and left several others unreachable, so a design's attack bonus, spell id
/// and protection bonus could only be edited two dialogs deep or not at all. Everything
/// <see cref="ItemRecord"/> carries as a scalar or a string is here, in one form.
/// </para>
/// <para>
/// <b>What is shown but not edited</b>: <see cref="PreSpellNameKey"/> and
/// <see cref="LegacyUsableByClass"/> are migration residue that only pre-0.998101 designs carry —
/// writing either would corrupt a modern record's provenance rather than change anything. The
/// special-abilities block and the attribute list pass through untouched, because both are keyed
/// collections whose editors are separate screens in the original too.
/// </para>
/// </remarks>
public sealed partial class ItemEditorViewModel : RecordEditorViewModel<ItemRecord>
{
    /// <summary>What an unnamed record is called in the master list.</summary>
    public const string Unnamed = "(unnamed item)";

    /// <summary>
    /// The reference's cap on an item's attack message (<c>MAX_ITEM_ATTACK_MSG_LEN</c>).
    /// </summary>
    /// <remarks>
    /// The dialog says so in a static beside the box. It is a display convention rather than a
    /// serialization limit — the message is a length-prefixed string and a longer one round-trips
    /// fine — so this is enforced as a warning on <see cref="HasOverlongAttackMessage"/> rather
    /// than by truncating what the user typed.
    /// </remarks>
    public const int MaxAttackMessage = 20;

    public ItemEditorViewModel(ItemRecord record, IEnumerable<string>? knownBaseclasses = null)
        : base(record, nameof(UsableByAnyBaseclass), nameof(IsHalveJoinEffective),
               nameof(HasOverlongAttackMessage), nameof(OtherUsageFlags))
    {
        ArgumentNullException.ThrowIfNull(record);

        WeaponTypeChoices = Choices.WithCurrent(Choices.WeaponTypes, record.Tail.WeaponType);
        HandChoices = Choices.WithCurrent(Choices.HandCounts, record.Combat.HandsToUse);
        RechargeChoices = Choices.WithCurrent(Choices.RechargeRates, record.Tail.RechargeRate);
        SlotChoices = Choices.WithCurrent(Choices.Slots,
                                          Choices.NormaliseSlot(record.Combat.LocationReadied));

        Baseclasses = BuildBaseclassList(record.Tail.UsableByBaseclass, knownBaseclasses);
        foreach (var entry in Baseclasses)
        {
            entry.PropertyChanged += OnBaseclassChanged;
        }

        Load(record);
    }

    /// <summary>The design's baseclasses, ticked where this item names them.</summary>
    /// <remarks>
    /// The group box round the original's button was captioned "Can 'Ready'" — this is readying
    /// permission (<c>IsUsableByBaseclass</c>), not a class restriction on owning the item.
    /// </remarks>
    public IReadOnlyList<SelectableId> Baseclasses { get; }

    /// <summary>
    /// <b>True when <i>nothing</i> is ticked, which means every baseclass may ready it.</b>
    /// </summary>
    /// <remarks>
    /// An empty <c>usableByBaseclass</c> is the encoding for "any" — the text format writes the
    /// literal <c>"any"</c> for an empty list and reads <c>"any"</c> or <c>""</c> back as
    /// <c>RemoveAll()</c> (<c>ItemDB.cpp:1069</c>, <c>:1095</c>), and <c>ITEM_DATA::Clear</c> leaves
    /// it empty (<c>Items.cpp:3299</c>). So an untouched tick list is the most permissive state and
    /// not, as it looks, the most restrictive. Worse, the original's Add command pre-ticked every
    /// baseclass (<c>ItemEditor.cpp:94</c>), so designs carry both encodings of the same meaning
    /// and only one of them survives a baseclass being renamed.
    /// </remarks>
    public bool UsableByAnyBaseclass => !Baseclasses.Any(b => b.IsSelected);

    public IReadOnlyList<IntChoice> WeaponTypeChoices { get; }

    public IReadOnlyList<IntChoice> HandChoices { get; }

    public IReadOnlyList<SlotChoice> SlotChoices { get; }

    // ---- Identity -----------------------------------------------------------------------------

    /// <summary>
    /// The item's id (<c>m_uniqueName</c>, <c>Items.h:701</c>) — what an event names, not a label.
    /// </summary>
    /// <remarks>
    /// Renaming this breaks every reference to the item. The MFC editor checked for duplicates on
    /// add and not on rename, which is how designs end up with two records answering to the same
    /// id and every lookup silently resolving to the first; see
    /// <see cref="ItemDatabaseViewModel.DuplicateNames"/>.
    /// </remarks>
    [ObservableProperty]
    private string uniqueName = string.Empty;

    /// <summary>The fuller name the inventory screen prints. Not an id, and free to collide.</summary>
    [ObservableProperty]
    private string idName = string.Empty;

    /// <summary>
    /// The spell this item casts when USEd — the whole of the item-invocation path.
    /// </summary>
    /// <remarks>
    /// Only designs at or above 0.999647 carry it; below that the field is not on the wire at all,
    /// so a value typed here would be dropped by a writer targeting an older version.
    /// </remarks>
    [ObservableProperty]
    private string spellId = string.Empty;

    [ObservableProperty]
    private string hitSound = string.Empty;

    [ObservableProperty]
    private string missSound = string.Empty;

    [ObservableProperty]
    private string launchSound = string.Empty;

    /// <summary>The pre-0.998101 spell-name migration key. Shown so it is not mistaken for lost.</summary>
    public int PreSpellNameKey => Original.Names.PreSpellNameKey;

    // ---- Economy ------------------------------------------------------------------------------

    [ObservableProperty]
    private int cost;

    [ObservableProperty]
    private int encumbrance;

    /// <summary>Experience awarded for acquiring the item.</summary>
    [ObservableProperty]
    private int experience;

    /// <summary>How many the item comes in — arrows per bundle, and what HALVE and JOIN split.</summary>
    [ObservableProperty]
    private int bundleQty;

    [ObservableProperty]
    private int numCharges;

    /// <summary>
    /// <c>Recharge_Rate</c> — an <b>enum, not a rate</b> (<c>itemRechargeRate</c>,
    /// <c>Items.h:44</c>).
    /// </summary>
    /// <remarks>
    /// 0 is <c>irrNever</c> and 1 is <c>irrDaily</c>; the dialog presented it as a radio pair
    /// captioned "Total" and "Daily", which is why the field reads as a number that ought to be a
    /// count of charges per day. It is not. A text box here would invite exactly that mistake.
    /// </remarks>
    public IntChoice? RechargeChoice
    {
        get => RechargeChoices.FirstOrDefault(c => c.Value == rechargeRate);
        set => SetChoice(ref rechargeRate, value?.Value, nameof(RechargeChoice));
    }

    public IReadOnlyList<IntChoice> RechargeChoices { get; }

    private int rechargeRate;

    // ---- Combat -------------------------------------------------------------------------------

    /// <summary>
    /// The number of <b>sides</b> on each damage die versus a small target.
    /// </summary>
    /// <remarks>
    /// <b><c>Dmg_Dice</c> is the sides and <c>Nbr_Dice</c> is the count</b>, which is the opposite
    /// of what both names suggest (<c>ItemDB.cpp:637</c>, where the text importer's
    /// <c>CONFIG_DECODE_dice(…, dice, sides, …)</c> assigns <c>Nbr_Dice_Sm = dice</c> and
    /// <c>Dmg_Dice_Sm = sides</c>). Swapping them turns 1d6 into 6d1 with no error anywhere.
    /// </remarks>
    [ObservableProperty]
    private int dmgDiceSm;

    /// <summary>How many dice are rolled versus a small target.</summary>
    [ObservableProperty]
    private int nbrDiceSm;

    [ObservableProperty]
    private int dmgBonusSm;

    /// <inheritdoc cref="DmgDiceSm"/>
    [ObservableProperty]
    private int dmgDiceLg;

    [ObservableProperty]
    private int nbrDiceLg;

    [ObservableProperty]
    private int dmgBonusLg;

    /// <summary>
    /// Rate of fire per round — a <c>double</c> where every neighbour is a <c>long</c>.
    /// </summary>
    /// <remarks>
    /// Fractional values are the point: 0.5 is one attack every other round, which is why this is
    /// not the integer field the dialog's narrow edit box makes it look like.
    /// </remarks>
    [ObservableProperty]
    private double rofPerRound;

    /// <summary>Subtracted from 10 by the armour rules; the dialog's own caption says "added".</summary>
    [ObservableProperty]
    private int protectionBase;

    [ObservableProperty]
    private int protectionBonus;

    [ObservableProperty]
    private int attackBonus;

    [ObservableProperty]
    private int rangeMax;

    /// <summary>
    /// The ammunition family, matched between a launcher and its ammunition.
    /// </summary>
    /// <remarks>
    /// <b>Empty and <c>"None"</c> are the same value.</b> The reader normalises <c>"None"</c> to
    /// empty on load (<c>Items.cpp:2807</c>), so typing it back produces a record that compares
    /// unequal to one that means the same thing. Left as typed rather than re-normalised here,
    /// because that normalisation belongs to the reader and would hide a design that really does
    /// have an ammo type called None.
    /// </remarks>
    [ObservableProperty]
    private string ammoType = string.Empty;

    /// <summary>Up to 20 characters, interpolated as "&lt;attacker&gt; &lt;message&gt; &lt;target&gt;".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlongAttackMessage))]
    private string attackMessage = string.Empty;

    public bool HasOverlongAttackMessage => AttackMessage.Length > MaxAttackMessage;

    // ---- Events -------------------------------------------------------------------------------

    /// <summary>The event run when the item is USEd, or 0.</summary>
    [ObservableProperty]
    private uint useEvent;

    [ObservableProperty]
    private uint examineEvent;

    [ObservableProperty]
    private string examineLabel = string.Empty;

    // ---- Art ----------------------------------------------------------------------------------

    /// <summary>
    /// The in-flight missile sprite's file, or null when the record carries no art.
    /// </summary>
    /// <remarks>
    /// Only the file name is editable. The rest of a <c>PIC_DATA</c> — frame size, timing, alpha —
    /// is the art picker's business, and inventing a whole record for an item that has none would
    /// put twelve made-up fields on the wire. So a null stays null.
    /// </remarks>
    public string? MissileArtFileName
    {
        get => missileArt?.FileName;
        set => SetArt(ref missileArt, value, nameof(MissileArtFileName));
    }

    /// <summary>The hit sprite from the record's leading art block.</summary>
    public string? HitArtFileName
    {
        get => hitArt?.FileName;
        set => SetArt(ref hitArt, value, nameof(HitArtFileName));
    }

    /// <summary>
    /// The hit sprite from the record's <i>tail</i> — a second, independent copy.
    /// </summary>
    /// <remarks>
    /// <b>Not a duplicate of <see cref="HitArtFileName"/>.</b> <c>ITEM_DATA</c> really does put
    /// <c>HitArt</c> on the wire twice, once with <c>MissileArt</c> near the front and once alone
    /// after <c>IsNonLethal</c> (<c>Items.cpp:2884</c>). The two are separate storage and shipped
    /// designs do not always agree on them, so collapsing them into one field would pick a winner
    /// the reference never picks.
    /// </remarks>
    public string? TailHitArtFileName
    {
        get => tailHitArt?.FileName;
        set => SetArt(ref tailHitArt, value, nameof(TailHitArtFileName));
    }

    private PicRecord? hitArt;
    private PicRecord? missileArt;
    private PicRecord? tailHitArt;

    // ---- Flags --------------------------------------------------------------------------------

    /// <summary>A cursed item, once readied, cannot be taken off.</summary>
    public bool IsCursed
    {
        get => cursed != 0;
        set => SetFlag(ref cursed, value, nameof(IsCursed));
    }

    public bool IsNonLethal
    {
        get => isNonLethal != 0;
        set => SetFlag(ref isNonLethal, value, nameof(IsNonLethal));
    }

    /// <summary>Whether HALVE and JOIN apply. Defaults to <b>true</b> in designs below 0.881.</summary>
    public bool CanBeHalvedJoined
    {
        get => canBeHalvedJoined != 0;
        set => SetFlag(ref canBeHalvedJoined, value, nameof(CanBeHalvedJoined));
    }

    /// <summary>
    /// False when <see cref="CanBeHalvedJoined"/> is set but <see cref="BundleQty"/> makes it inert.
    /// </summary>
    /// <remarks>
    /// <c>itemCanBeJoined</c> and <c>itemCanBeHalved</c> both require <c>Bundle_Qty &gt; 1</c> as
    /// well as the flag (<c>Items.cpp:469</c>, <c>:488</c>). The MFC dialog resolved the
    /// contradiction by <b>writing FALSE onto the item</b> whenever the quantity dropped below two
    /// (<c>ItemDBDlg.cpp:85</c>, and again on the save path at <c>:413</c>, after the DDX had
    /// already stored the user's tick). This form does not do that — an editor that silently
    /// rewrites a field the user did not touch is how designs quietly lose data — so the
    /// contradiction is reported instead.
    /// </remarks>
    public bool IsHalveJoinEffective => !CanBeHalvedJoined || BundleQty > 1;

    /// <summary>
    /// Whether the item can leave its owner, and whether a monster drops it at end of combat.
    /// </summary>
    /// <remarks>
    /// It is the second half that surprises: a <c>SpellCaster</c> item is normally destroyed with
    /// its monster, and this flag is what lets the party loot it
    /// (<c>CombatAftermath</c>, and the dialog's own caption). Defaults to true below 0.904.
    /// </remarks>
    public bool CanBeTradeDropSoldDep
    {
        get => canBeTradeDropSoldDep != 0;
        set => SetFlag(ref canBeTradeDropSoldDep, value, nameof(CanBeTradeDropSoldDep));
    }

    public bool IsUsable
    {
        get => (usageFlags & Choices.UsageUsable) != 0;
        set => SetUsage(Choices.UsageUsable, value, nameof(IsUsable));
    }

    /// <summary>A scroll a caster can copy into a spell book.</summary>
    public bool IsScribable
    {
        get => (usageFlags & Choices.UsageScribable) != 0;
        set => SetUsage(Choices.UsageScribable, value, nameof(IsScribable));
    }

    /// <summary>Note the inverted sense — the stored bit means "not magical".</summary>
    public bool IsNotMagical
    {
        get => (usageFlags & Choices.UsageNotMagical) != 0;
        set => SetUsage(Choices.UsageNotMagical, value, nameof(IsNotMagical));
    }

    /// <summary>The bits of <c>m_usageFlags</c> nothing above claims, kept so they survive.</summary>
    public int OtherUsageFlags =>
        usageFlags & ~(Choices.UsageUsable | Choices.UsageScribable | Choices.UsageNotMagical);

    private int cursed;
    private int isNonLethal;
    private int canBeHalvedJoined;
    private int canBeTradeDropSoldDep;
    private int usageFlags;

    /// <summary>The pre-0.998101 seven-class usability bitmask. Read-only; superseded.</summary>
    public int LegacyUsableByClass => Original.Tail.LegacyUsableByClass;

    // ---- Choice-backed fields -----------------------------------------------------------------

    /// <summary>
    /// The item's <c>weaponClassType</c>, which decides whether it can attack at all and how.
    /// </summary>
    public IntChoice? WeaponTypeChoice
    {
        get => WeaponTypeChoices.FirstOrDefault(c => c.Value == weaponType);
        set => SetChoice(ref weaponType, value?.Value, nameof(WeaponTypeChoice));
    }

    public IntChoice? HandsChoice
    {
        get => HandChoices.FirstOrDefault(c => c.Value == handsToUse);
        set => SetChoice(ref handsToUse, value?.Value, nameof(HandsChoice));
    }

    /// <summary>
    /// Where the item is worn, as the base-38 word the field actually holds.
    /// </summary>
    /// <remarks>
    /// <b>The stored value is normalised on load.</b> A design below the conversion gate wrote a
    /// small ordinal (0 = weapon hand, …) which <c>Items.cpp:2820</c> rewrites into a packed name.
    /// Doing that here means an old design's item lands on a real entry in the list rather than on
    /// nothing, and it is the <i>database</i> table that is applied — the carried-item table
    /// disagrees at ordinal 3 and would turn gauntlets into a quiver.
    /// </remarks>
    public SlotChoice? SlotChoiceValue
    {
        get => SlotChoices.FirstOrDefault(c => c.Value == locationReadied);
        set
        {
            if (value is null || value.Value == locationReadied)
            {
                return;
            }

            OnPropertyChanging(nameof(SlotChoiceValue));
            locationReadied = value.Value;
            OnPropertyChanged(nameof(SlotChoiceValue));
        }
    }

    private int weaponType;
    private int handsToUse;
    private uint locationReadied;

    // ---- Preserved blocks ---------------------------------------------------------------------

    /// <summary>The special-ability pairs the record carries, as read-only text.</summary>
    public IReadOnlyList<string> SpecialAbilityLines =>
    [
        .. Original.Tail.SpecialAbilities.Pairs.Select(p => $"{p.Key} = {p.Value}"),
        .. Original.Tail.SpecialAbilities.LegacySlots.Count > 0
            ? new[] { $"({Original.Tail.SpecialAbilities.LegacySlots.Count} legacy slots)" }
            : [],
    ];

    /// <summary>The <c>ITEM_DATA_ATTRIBUTES</c> list, as read-only text.</summary>
    public IReadOnlyList<string> AttributeLines =>
        [.. Original.Tail.Attributes.Select(a => $"{a.Key} = {a.Value}")];

    // ---- Base contract ------------------------------------------------------------------------

    public override string Title => UniqueName.Length > 0 ? UniqueName : Unnamed;

    public override string Subtitle =>
        IdName.Length > 0 && !string.Equals(IdName, UniqueName, StringComparison.Ordinal)
            ? IdName
            : string.Empty;

    protected override void Load(ItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        UniqueName = record.Names.UniqueName;
        IdName = record.Names.IdName;
        SpellId = record.Names.SpellId;
        HitSound = record.Names.HitSound;
        MissSound = record.Names.MissSound;
        LaunchSound = record.Names.LaunchSound;

        AmmoType = record.Scalars.AmmoType;
        Experience = record.Scalars.Experience;
        Cost = record.Scalars.Cost;
        Encumbrance = record.Scalars.Encumbrance;
        AttackBonus = record.Scalars.AttackBonus;
        cursed = record.Scalars.Cursed;
        BundleQty = record.Scalars.BundleQty;
        NumCharges = record.Scalars.NumCharges;

        locationReadied = Choices.NormaliseSlot(record.Combat.LocationReadied);
        handsToUse = record.Combat.HandsToUse;
        DmgDiceSm = record.Combat.DmgDiceSm;
        NbrDiceSm = record.Combat.NbrDiceSm;
        DmgBonusSm = record.Combat.DmgBonusSm;
        DmgDiceLg = record.Combat.DmgDiceLg;
        NbrDiceLg = record.Combat.NbrDiceLg;
        DmgBonusLg = record.Combat.DmgBonusLg;
        RofPerRound = record.Combat.RofPerRound;
        ProtectionBase = record.Combat.ProtectionBase;
        ProtectionBonus = record.Combat.ProtectionBonus;

        weaponType = record.Tail.WeaponType;
        usageFlags = record.Tail.UsageFlags;
        RangeMax = record.Tail.RangeMax;
        UseEvent = record.Tail.UseEvent;
        ExamineEvent = record.Tail.ExamineEvent;
        ExamineLabel = record.Tail.ExamineLabel;
        AttackMessage = record.Tail.AttackMessage;
        rechargeRate = record.Tail.RechargeRate;
        isNonLethal = record.Tail.IsNonLethal;
        canBeHalvedJoined = record.Tail.CanBeHalvedJoined;
        canBeTradeDropSoldDep = record.Tail.CanBeTradeDropSoldDep;

        hitArt = record.HitArt;
        missileArt = record.MissileArt;
        tailHitArt = record.Tail.HitArt;

        var named = record.Tail.UsableByBaseclass.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Baseclasses)
        {
            entry.IsSelected = named.Contains(entry.Id);
        }

        // The fields set through a backing variable above raise nothing on their own.
        OnPropertyChanged(nameof(IsCursed));
        OnPropertyChanged(nameof(IsNonLethal));
        OnPropertyChanged(nameof(CanBeHalvedJoined));
        OnPropertyChanged(nameof(CanBeTradeDropSoldDep));
        OnPropertyChanged(nameof(IsUsable));
        OnPropertyChanged(nameof(IsScribable));
        OnPropertyChanged(nameof(IsNotMagical));
        OnPropertyChanged(nameof(WeaponTypeChoice));
        OnPropertyChanged(nameof(HandsChoice));
        OnPropertyChanged(nameof(RechargeChoice));
        OnPropertyChanged(nameof(SlotChoiceValue));
        OnPropertyChanged(nameof(HitArtFileName));
        OnPropertyChanged(nameof(MissileArtFileName));
        OnPropertyChanged(nameof(TailHitArtFileName));
    }

    protected override ItemRecord Build()
    {
        var names = new ItemNames(Original.Names.PreSpellNameKey, SpellId, UniqueName, IdName,
                                  HitSound, MissSound, LaunchSound);

        var scalars = new ItemScalars(AmmoType, Experience, Cost, Encumbrance, AttackBonus,
                                      cursed, BundleQty, NumCharges);

        var combat = new ItemCombat(locationReadied, handsToUse,
                                    DmgDiceSm, NbrDiceSm, DmgBonusSm,
                                    DmgDiceLg, NbrDiceLg, DmgBonusLg,
                                    RofPerRound, ProtectionBase, ProtectionBonus);

        var usable = Canonical<string>(
            [.. Baseclasses.Where(b => b.IsSelected).Select(b => b.Id)],
            Original.Tail.UsableByBaseclass);

        var tail = new ItemTail(weaponType, usageFlags, Original.Tail.LegacyUsableByClass,
                                usable, RangeMax, UseEvent, ExamineEvent, ExamineLabel,
                                AttackMessage, rechargeRate, isNonLethal, tailHitArt,
                                canBeHalvedJoined, canBeTradeDropSoldDep,
                                Original.Tail.SpecialAbilities, Original.Tail.Attributes);

        return new ItemRecord(names, hitArt, missileArt, scalars, combat, tail);
    }

    /// <summary>
    /// A record for a brand-new item, with the defaults the reference applies when a field is
    /// absent from the file rather than the zeros a blank record would carry.
    /// </summary>
    /// <remarks>
    /// <c>CanBeHalvedJoined</c> and <c>CanBeTradeDropSoldDep</c> both default to <b>true</b> in a
    /// design that predates them (<c>Items.cpp:2884</c>, <c>:2889</c>), and the attack message
    /// defaults to <c>"attacks"</c> (<c>:2872</c>). A new item built from zeros would be one that
    /// cannot be dropped and attacks with an empty verb — neither of which any design contains.
    /// </remarks>
    public static ItemRecord NewRecord(string name)
    {
        const string defaultAttackMessage = "attacks";     // Items.cpp:3311
        const string defaultExamineLabel = "EXAMINE";      // Items.cpp:3309, and DeleteItemEvents

        return new ItemRecord(
            new ItemNames(-1, string.Empty, name, name, string.Empty, string.Empty, string.Empty),
            HitArt: null, MissileArt: null,
            // Bundle_Qty 1, everything else zero.
            new ItemScalars(string.Empty, 0, 0, 0, 0, 0, 1, 0),
            // WeaponHand, one hand, 1d6 against both sizes, one attack per round.
            new ItemCombat(ReadiedLocation.WeaponHand, 1, 6, 1, 0, 6, 1, 0, 1.0, 0, 0),
            // The tick list is left EMPTY on purpose -- see UsableByAnyBaseclass. The original's
            // Add command pre-ticked every baseclass instead, which says the same thing in a form
            // that stops meaning it as soon as the design gains a baseclass.
            new ItemTail(0, 0, 0, [], 0, 0, 0, defaultExamineLabel,
                         defaultAttackMessage, 0, 0, null, 1, 1,
                         new SpecabBlock([], [], []), []));
    }

    // ---- Setters that write through a shared backing field ------------------------------------

    private void SetFlag(ref int field, bool on, string property)
    {
        if (on == (field != 0))
        {
            return;
        }

        OnPropertyChanging(property);
        field = Flag(field, on);
        OnPropertyChanged(property);
    }

    private void SetUsage(int mask, bool on, string property)
    {
        if (on == ((usageFlags & mask) != 0))
        {
            return;
        }

        OnPropertyChanging(property);
        usageFlags = on ? usageFlags | mask : usageFlags & ~mask;
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(OtherUsageFlags));
    }

    private void SetChoice(ref int field, int? chosen, string property)
    {
        if (chosen is not { } value || value == field)
        {
            return;
        }

        OnPropertyChanging(property);
        field = value;
        OnPropertyChanged(property);
    }

    private void SetArt(ref PicRecord? field, string? fileName, string property)
    {
        if (field is null || fileName is null || string.Equals(field.FileName, fileName,
                                                               StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanging(property);
        field = field with { FileName = fileName };
        OnPropertyChanged(property);
    }

    private void OnBaseclassChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    /// <summary>
    /// The tick list: every baseclass the design defines, plus any this item names that it does not.
    /// </summary>
    private static IReadOnlyList<SelectableId> BuildBaseclassList(
        IReadOnlyList<string> named, IEnumerable<string>? known)
    {
        var namedSet = named.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownList = known?.ToList() ?? [];
        var knownSet = knownList.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. knownList.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                        .Select(id => new SelectableId(id, namedSet.Contains(id), isKnown: true)),
            .. named.Where(id => !knownSet.Contains(id))
                    .Select(id => new SelectableId(id, isSelected: true, isKnown: false)),
        ];
    }
}
