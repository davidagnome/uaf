using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;

namespace UAFedit.Databases;

/// <summary>
/// The detail form for one <c>MONSTER_DATA</c> record — the Avalonia replacement for
/// <c>CMonsterDBDlg</c> (<c>UAFWinEd/MonsterDBDlg.cpp</c>, dialog <c>IDD_MONSTERDB</c>), with the
/// attack list inlined rather than two modals deep.
/// </summary>
/// <remarks>
/// <b>Nothing here rewrites a field the user did not touch.</b> The original silently zeroed
/// <c>Hit_Dice_Bonus</c> on both the load and the save path whenever <c>UseHitDice</c> was false
/// (<c>MonsterDBDlg.cpp:90</c>, <c>:210</c>), so merely opening a hit-point monster and pressing OK
/// destroyed its bonus. That coupling is reported through <see cref="IsHitDiceBonusEffective"/>
/// instead.
/// </remarks>
public sealed partial class MonsterEditorViewModel : RecordEditorViewModel<MonsterRecord>
{
    /// <summary>The ASL key a monster's race hides behind (<c>Monster.cpp:657</c>).</summary>
    /// <remarks>
    /// <b>The race is not a serialized member of <c>MONSTER_DATA</c>.</b> The struct declares
    /// <c>RACE_ID raceID;</c> with the comment "Serialized via ASL" (<c>Monster.h:389</c>) and the
    /// store path pushes it into <c>mon_asl</c> under this key just before <c>Morale</c>. So the
    /// port's <see cref="MonsterRecord"/> has no <c>Race</c> property and is not missing one —
    /// the value lives in <see cref="MonsterRecord.Attributes"/>, which is where this reads and
    /// writes it.
    /// </remarks>
    public const string RaceAttributeKey = "$SYS$Race";

    /// <summary>What a monster with no race attribute is (<c>Monster.cpp:842</c>).</summary>
    public const string DefaultRace = "Human";

    /// <summary>
    /// <c>ASLF_EDITOR</c> — the flags the reference stamps on the race entry it writes.
    /// </summary>
    public const byte EditorAttributeFlags = (byte)(AslFlags.ReadOnly | AslFlags.Design);

    public const string Unnamed = "(unnamed monster)";

    public MonsterEditorViewModel(MonsterRecord record, IEnumerable<string>? knownClasses = null)
        : base(record, nameof(IsHitDiceBonusEffective), nameof(HitDiceCaption),
               nameof(HasAttacks), nameof(MoneySummary), nameof(ItemsSummary),
               nameof(HasIcon), nameof(CarriedThroughText))
    {
        ArgumentNullException.ThrowIfNull(record);

        SizeChoices = Choices.WithCurrent(Choices.CreatureSizes, record.Size);
        ClassChoices = Choices.WithCurrent(
            [.. (knownClasses ?? []).OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                                    .Select(c => new TextChoice(c, c))],
            record.ClassId);

        FormFlags = BuildFlags(
        [
            ("Mammal", 1, "Vulnerable to the druid spell Charm Person or Mammal."),
            ("Animal", 2, "Susceptible to Invisibility to Animals."),
            ("Snake", 4, "Vulnerable to Snake Charm."),
            ("True giant", 8, "Takes extra damage from a Long Sword vs Giants."),
            ("Large", 16, "Damage comes from the LARGE column even if the icon is 1x1."),
        ], () => formType, v => Set(ref formType, v, nameof(FormType)));

        PenaltyFlags = BuildFlags(
        [
            ("Dwarf AC", 1, "A dwarf attacked by this monster gets +4 AC."),
            ("Gnome AC", 2, "A gnome attacked by this monster gets +4 AC."),
            ("Dwarf THAC0", 4, "A dwarf attacking this monster gets +1 THAC0."),
            ("Gnome THAC0", 8, "A gnome attacking this monster gets +1 THAC0."),
            ("Ranger damage", 16, "A ranger hitting this monster adds its ranger levels to damage."),
        ], () => penaltyType, v => Set(ref penaltyType, v, nameof(PenaltyType)));

        ImmunityFlags = BuildFlags(
        [
            ("Poison", 1, "Immune to poison, including stinking cloud."),
            ("Death magic", 2, "Immune to all death magic, including cloudkill."),
            ("Confusion", 4, "Confusion wands, spells and gazes have no effect."),
            ("Vorpal weapon", 8, "Cannot be decapitated by a vorpal blade."),
        ], () => immunityType, v => Set(ref immunityType, v, nameof(ImmunityType)));

        MiscFlags = BuildFlags(
        [
            ("Can be held or charmed", 1, "Vulnerable to hold and charm spells."),
            ("Affected by dispel evil", 2, "Summoned from another plane; dispel evil applies."),
        ], () => miscOptionsType, v => Set(ref miscOptionsType, v, nameof(MiscOptionsType)));

        Attacks.CollectionChanged += OnAttacksChanged;
        Load(record);
    }

    // ---- Identity -----------------------------------------------------------------------------

    /// <summary>
    /// The monster's id. In editor builds <c>MONSTER_ID</c> <b>is</b> the name
    /// (<c>Monster.h:369</c>), so renaming breaks every encounter that summons it.
    /// </summary>
    [ObservableProperty]
    private string name = string.Empty;

    /// <summary>
    /// The monster's race, read from and written to the <c>$SYS$Race</c> attribute.
    /// </summary>
    /// <remarks>
    /// Free text rather than a list, because a race the design no longer defines still has to
    /// round-trip. The original's combo was <c>CBS_DROPDOWN</c> and read only <c>GetCurSel()</c>,
    /// so anything typed that was not an exact list entry silently became the <b>empty</b> race
    /// (<c>MonsterDBDlg.cpp:233</c>).
    /// </remarks>
    [ObservableProperty]
    private string race = DefaultRace;

    /// <summary>
    /// The turning category, as free text.
    /// </summary>
    /// <remarks>
    /// A combo once, now a plain edit box — the <c>UndeadTypeText</c> version is commented out of
    /// the dialog (<c>MonsterDBDlg.cpp:132</c>). Designs above 0.998115 store the name itself, so
    /// the set is genuinely open. <b>Empty means "not undead"</b>; there is no "Not Undead" value.
    /// <see cref="UndeadTypeSuggestions"/> lists the thirteen standard names.
    /// </remarks>
    [ObservableProperty]
    private string undeadType = string.Empty;

    /// <summary>The standard turning categories, for the hint under the box.</summary>
    public static string UndeadTypeSuggestions { get; } =
        "Standard categories: "
        + string.Join(", ", Choices.UndeadTypes.Skip(1).Select(c => c.Value));

    [ObservableProperty]
    private string hitSound = string.Empty;

    [ObservableProperty]
    private string missSound = string.Empty;

    [ObservableProperty]
    private string moveSound = string.Empty;

    [ObservableProperty]
    private string deathSound = string.Empty;

    /// <summary>The icon's art file, or null when the record carries no <c>PIC_DATA</c>.</summary>
    public string? IconFileName
    {
        get => icon?.FileName;
        set
        {
            if (icon is null || value is null
                || string.Equals(icon.FileName, value, StringComparison.Ordinal))
            {
                return;
            }

            OnPropertyChanging(nameof(IconFileName));
            icon = icon with { FileName = value };
            OnPropertyChanged(nameof(IconFileName));
        }
    }

    /// <summary>The bare filename designs below 0.640 stored instead of a <c>PIC_DATA</c>.</summary>
    public string LegacyIconFile => Original.LegacyIconFile;

    public int PreSpellNameKey => Original.PreSpellNameKey;

    private PicRecord? icon;

    // ---- Statistics ---------------------------------------------------------------------------

    [ObservableProperty]
    private int intelligence;

    /// <summary>Lower is better, and negative is legal — the original's box allowed a minus sign
    /// where every neighbouring box did not.</summary>
    [ObservableProperty]
    private int armorClass;

    [ObservableProperty]
    private int movement;

    [ObservableProperty]
    private int thac0;

    [ObservableProperty]
    private int magicResistance;

    [ObservableProperty]
    private int morale;

    [ObservableProperty]
    private int experienceValue;

    /// <summary>
    /// Hit dice — <b>or hit points, when <see cref="UsesHitDice"/> is false</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>float</c> field with two meanings. With <c>UseHitDice</c> clear the engine reads the
    /// same number as a hit-point total and converts it,
    /// <c>hd = (hd / FIGHTER_HIT_DIE) + 1</c> clamped to [1, 40] (<c>Char.cpp:3757</c>, and
    /// repeated at four more sites). So a "12" here is twelve dice or twelve hit points depending
    /// on a checkbox, and the two differ by a factor of four.
    /// </para>
    /// <para>
    /// It is a <c>float</c> because half-dice monsters exist, which is also why it is the one
    /// non-integer among <c>MONSTER_DATA</c>'s <c>long</c>s (<c>Monster.h:410</c>).
    /// </para>
    /// </remarks>
    [ObservableProperty]
    private float hitDice;

    [ObservableProperty]
    private int hitDiceBonus;

    /// <summary>True when <see cref="HitDice"/> means dice; false when it means hit points.</summary>
    /// <remarks>
    /// Stored the right way round here. The original's radio index was the <i>inverse</i> of the
    /// <c>BOOL</c> — <c>m_UseHitDice = (UseHitDice ? 0 : 1)</c> (<c>MonsterDBDlg.cpp:89</c>) — so
    /// reading the dialog's variable name gives the opposite of the truth.
    /// </remarks>
    public bool UsesHitDice
    {
        get => useHitDice != 0;
        set
        {
            if (value == (useHitDice != 0))
            {
                return;
            }

            OnPropertyChanging(nameof(UsesHitDice));
            useHitDice = Flag(useHitDice, value);
            OnPropertyChanged(nameof(UsesHitDice));
        }
    }

    /// <summary>What the <see cref="HitDice"/> box is currently a count of.</summary>
    public string HitDiceCaption => UsesHitDice ? "Hit dice" : "Hit points";

    /// <summary>False when a bonus is set that the hit-point path will never read.</summary>
    public bool IsHitDiceBonusEffective => UsesHitDice || HitDiceBonus == 0;

    private int useHitDice;

    // ---- Choice-backed fields -----------------------------------------------------------------

    public IReadOnlyList<IntChoice> SizeChoices { get; }

    public IReadOnlyList<TextChoice> ClassChoices { get; }

    public IntChoice? SizeChoice
    {
        get => SizeChoices.FirstOrDefault(c => c.Value == size);
        set
        {
            if (value is null || value.Value == size)
            {
                return;
            }

            OnPropertyChanging(nameof(SizeChoice));
            size = value.Value;
            OnPropertyChanged(nameof(SizeChoice));
        }
    }

    /// <summary>
    /// The monster's class. Defaults to <c>"Fighter"</c> in designs below 0.998101, where the field
    /// is not on the wire at all (<c>Monster.cpp:781</c>).
    /// </summary>
    public TextChoice? ClassChoice
    {
        get => ClassChoices.FirstOrDefault(
            c => string.Equals(c.Value, classId, StringComparison.Ordinal));
        set
        {
            if (value is null || string.Equals(value.Value, classId, StringComparison.Ordinal))
            {
                return;
            }

            OnPropertyChanging(nameof(ClassChoice));
            classId = value.Value;
            OnPropertyChanged(nameof(ClassChoice));
        }
    }

    private int size;
    private string classId = string.Empty;

    // ---- Flags --------------------------------------------------------------------------------

    public IReadOnlyList<FlagBit> FormFlags { get; }

    public IReadOnlyList<FlagBit> PenaltyFlags { get; }

    public IReadOnlyList<FlagBit> ImmunityFlags { get; }

    public IReadOnlyList<FlagBit> MiscFlags { get; }

    public uint FormType => formType;

    public uint PenaltyType => penaltyType;

    public uint ImmunityType => immunityType;

    public uint MiscOptionsType => miscOptionsType;

    private uint formType;
    private uint penaltyType;
    private uint immunityType;
    private uint miscOptionsType;

    // ---- Attacks ------------------------------------------------------------------------------

    /// <summary>
    /// The monster's attacks. Never empty in a record that came off disk.
    /// </summary>
    /// <remarks>
    /// The reader gives a monster with none a single 1d6 "attacks", at <b>every</b> version and not
    /// only on the legacy path (<c>Monster.cpp:764</c>) — one of dc-default's 171 monsters really
    /// does ship with an empty list. So an empty list here is something the user just did, and
    /// <see cref="HasAttacks"/> is what says so before a save turns it back into 1d6.
    /// </remarks>
    public ObservableCollection<MonsterAttackViewModel> Attacks { get; } = [];

    public bool HasAttacks => Attacks.Count > 0;

    [RelayCommand]
    private void AddAttack()
    {
        var attack = new MonsterAttackViewModel(MonsterAttackViewModel.NewAttack());
        attack.PropertyChanged += OnAttackChanged;
        Attacks.Add(attack);
    }

    [RelayCommand]
    private void RemoveAttack(MonsterAttackViewModel? attack)
    {
        if (attack is null)
        {
            return;
        }

        attack.PropertyChanged -= OnAttackChanged;
        Attacks.Remove(attack);
    }

    // ---- Carried, and preserved ---------------------------------------------------------------

    /// <summary>
    /// The monster's default inventory, shown but not edited.
    /// </summary>
    /// <remarks>
    /// An <c>ITEM_LIST</c> plus twelve ready slots, edited through <c>CItemDlg</c> in the original.
    /// It is carried through untouched, which is what keeps the record byte-identical for a monster
    /// nobody edited the inventory of. <b>Both this and <see cref="MoneySummary"/> sit after the
    /// attribute list on the wire</b> (<c>Monster.cpp:851</c>), unlike <c>ITEM_DATA</c>, which ends
    /// at its ASL.
    /// </remarks>
    public string ItemsSummary => Original.Items is { } list
        ? $"{list.Items.Count} item(s), {list.Ready.Slots.Count(s => s != 0)} readied"
        : "(none — design predates 0.694)";

    public string MoneySummary => Original.Money is { } money
        ? $"{money.Coins.Sum()} coins, {money.Gems.Count} gems, {money.Jewelry.Count} jewellery"
        : "(none — design predates 0.906)";

    public IReadOnlyList<string> SpecialAbilityLines =>
    [
        .. Original.SpecialAbilities.Pairs.Select(p => $"{p.Key} = {p.Value}"),
        .. Original.SpecialAbilities.LegacySlots.Count > 0
            ? new[] { $"({Original.SpecialAbilities.LegacySlots.Count} legacy slots)" }
            : [],
    ];

    /// <summary>The two carried-through blocks as one block of text.</summary>
    public string CarriedThroughText =>
        SpecialAbilityLines.Count + AttributeLines.Count == 0
            ? "No special abilities, no attributes."
            : string.Join(Environment.NewLine, SpecialAbilityLines.Concat(AttributeLines));

    public bool HasIcon => icon is not null;

    public bool HasLegacyIconFile => Original.LegacyIconFile.Length > 0;

    public bool HasLegacySpellKey => Original.PreSpellNameKey >= 0;

    /// <summary>
    /// The <c>MONSTER_DATA_ATTRIBUTES</c> list, as read-only text.
    /// </summary>
    /// <remarks>
    /// <see cref="RaceAttributeKey"/> appears here too, and editing <see cref="Race"/> changes it.
    /// No editor in the original touched <c>mon_asl</c> at all.
    /// </remarks>
    public IReadOnlyList<string> AttributeLines =>
        [.. Original.Attributes.Select(a => $"{a.Key} = {a.Value}")];

    // ---- Base contract ------------------------------------------------------------------------

    public override string Title => Name.Length > 0 ? Name : Unnamed;

    public override string Subtitle =>
        $"{(UsesHitDice ? $"{HitDice:0.##} HD" : $"{HitDice:0.##} hp")}, AC {ArmorClass}";

    protected override void Load(MonsterRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Name = record.Name;
        icon = record.Icon;
        HitSound = record.HitSound;
        MissSound = record.MissSound;
        MoveSound = record.MoveSound;
        DeathSound = record.DeathSound;

        Intelligence = record.Intelligence;
        ArmorClass = record.ArmorClass;
        Movement = record.Movement;
        HitDice = record.HitDice;
        useHitDice = record.UseHitDice;
        HitDiceBonus = record.HitDiceBonus;
        Thac0 = record.Thac0;
        MagicResistance = record.MagicResistance;
        Morale = record.Morale;
        ExperienceValue = record.ExperienceValue;

        size = record.Size;
        classId = record.ClassId;
        UndeadType = record.UndeadType;
        Race = RaceOf(record.Attributes);

        formType = record.FormType;
        penaltyType = record.PenaltyType;
        immunityType = record.ImmunityType;
        miscOptionsType = record.MiscOptionsType;

        foreach (var attack in Attacks)
        {
            attack.PropertyChanged -= OnAttackChanged;
        }

        Attacks.Clear();
        foreach (var attack in record.Attacks)
        {
            var row = new MonsterAttackViewModel(attack);
            row.PropertyChanged += OnAttackChanged;
            Attacks.Add(row);
        }

        foreach (var flag in FormFlags.Concat(PenaltyFlags)
                                      .Concat(ImmunityFlags).Concat(MiscFlags))
        {
            flag.Refresh();
        }

        OnPropertyChanged(nameof(UsesHitDice));
        OnPropertyChanged(nameof(SizeChoice));
        OnPropertyChanged(nameof(ClassChoice));
        OnPropertyChanged(nameof(IconFileName));
    }

    protected override MonsterRecord Build()
    {
        var attacks = Canonical<AttackDetails>([.. Attacks.Select(a => a.Attack)],
                                               Original.Attacks);

        var attributes = Canonical(WithRace(Original.Attributes, Race), Original.Attributes);

        return new MonsterRecord(
            Original.PreSpellNameKey, Name, icon, Original.LegacyIconFile,
            HitSound, MissSound, MoveSound, DeathSound,
            Intelligence, ArmorClass, Movement, HitDice, useHitDice, HitDiceBonus, Thac0,
            attacks, MagicResistance, size, classId, Morale, ExperienceValue,
            formType, penaltyType, immunityType, miscOptionsType, UndeadType,
            Original.SpecialAbilities, attributes, Original.Items, Original.Money);
    }

    /// <summary>
    /// A record for a brand-new monster, with <c>MONSTER_DATA</c>'s constructor defaults.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the dialog's defaults.</b> <c>CMonsterDBDlg</c>'s <c>AFX_DATA_INIT</c> block sets
    /// AC 10, morale 50 and so on (<c>MonsterDBDlg.cpp:48</c>) and every one of them is
    /// unconditionally overwritten before the dialog is shown. The values that actually reach a new
    /// record come from <c>MONSTER_DATA::MONSTER_DATA()</c> (<c>Monster.cpp:149</c>): zeros except
    /// <b>THAC0 = 20</b>, <c>Size = Medium</c> and <c>UseHitDice = TRUE</c>.
    /// </para>
    /// <para>
    /// <b>The race is set here and the constructor does not set it.</b> Only <c>Clear()</c> assigns
    /// <c>"Human"</c> (<c>Monster.cpp:272</c>), so the original's Add produced a monster with an
    /// <i>empty</i> race while every deserialized monster got "Human". The reader's own default is
    /// followed instead of that accident.
    /// </para>
    /// </remarks>
    public static MonsterRecord NewRecord(string name) =>
        new(PreSpellNameKey: -1, name, Icon: null, LegacyIconFile: string.Empty,
            HitSound: string.Empty, MissSound: string.Empty,
            MoveSound: string.Empty, DeathSound: string.Empty,
            Intelligence: 0, ArmorClass: 0, Movement: 0, HitDice: 0, UseHitDice: 1,
            HitDiceBonus: 0, Thac0: 20,
            [MonsterAttackViewModel.NewAttack()],
            MagicResistance: 0, Size: 1, ClassId: "Fighter", Morale: 0, ExperienceValue: 0,
            FormType: 0, PenaltyType: 0, ImmunityType: 0, MiscOptionsType: 0,
            UndeadType: string.Empty,
            new SpecabBlock([], [], []),
            [new AslEntry(RaceAttributeKey, EditorAttributeFlags, DefaultRace)],
            Items: null, Money: null);

    /// <summary>The <c>$SYS$Race</c> attribute's value, or <c>"Human"</c> when there is none.</summary>
    public static string RaceOf(IReadOnlyList<AslEntry> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        foreach (var entry in attributes)
        {
            if (string.Equals(entry.Key, RaceAttributeKey, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value.Length > 0 ? entry.Value : DefaultRace;
            }
        }

        return DefaultRace;
    }

    /// <summary>
    /// The attribute list with the race entry set, or the list itself when nothing changes.
    /// </summary>
    /// <remarks>
    /// A new entry is <b>appended</b>. The reference's <c>StoreStringAsASL</c> inserts into a keyed
    /// list, so a design whose attributes are stored in key order will come back with this one out
    /// of place — harmless to every reader, but it is the one thing here that is not a byte-exact
    /// round trip.
    /// </remarks>
    private static IReadOnlyList<AslEntry> WithRace(IReadOnlyList<AslEntry> attributes, string race)
    {
        if (string.Equals(RaceOf(attributes), race, StringComparison.Ordinal))
        {
            return attributes;
        }

        var updated = new List<AslEntry>(attributes.Count + 1);
        bool replaced = false;

        foreach (var entry in attributes)
        {
            if (string.Equals(entry.Key, RaceAttributeKey, StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(entry with { Value = race });
                replaced = true;
            }
            else
            {
                updated.Add(entry);
            }
        }

        if (!replaced)
        {
            updated.Add(new AslEntry(RaceAttributeKey, EditorAttributeFlags, race));
        }

        return updated;
    }

    private static IReadOnlyList<FlagBit> BuildFlags(
        (string Label, uint Mask, string Description)[] bits,
        Func<uint> read, Action<uint> write) =>
        [.. bits.Select(b => new FlagBit(b.Label, b.Mask, b.Description, read, write))];

    private void Set(ref uint field, uint value, string property)
    {
        OnPropertyChanging(property);
        field = value;
        OnPropertyChanged(property);
    }

    private void OnAttackChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    private void OnAttacksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAttacks));
        RaiseDerived();
    }
}
