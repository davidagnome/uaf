using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;

namespace UAFedit.Databases;

/// <summary>
/// One row of a monster's attack list — the Avalonia replacement for
/// <c>CMonsterAttackDetails</c> (<c>UAFWinEd/MonsterAttackDetails.cpp</c>, dialog
/// <c>IDD_MONSTERATTACKDETAILS</c>), inlined into the monster form rather than two modals deep.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="SpellClass"/> and <see cref="SpellLevel"/> are carried, never edited.</b> The
/// original zeroed both whenever a spell was picked (<c>MonsterAttackDetails.cpp:149</c>) and the
/// code that would have repopulated them is commented out — but they are still on the wire, so a
/// port that dropped them would change every attack that has them set. They are pass-through
/// fields here for exactly that reason.
/// </para>
/// <para>
/// <b><see cref="Bonus"/> may be negative.</b> The original's edit box carried <c>ES_NUMBER</c>,
/// which rejects a minus sign, three lines below a comment saying the bonus can be positive or
/// negative (<c>MonsterAttackDetails.cpp:90</c>, <c>UAFWinEd.rc:3475</c>). Negative bonuses
/// therefore exist only in imported data and could never be typed. They can be here.
/// </para>
/// </remarks>
public sealed partial class MonsterAttackViewModel : ObservableObject
{
    /// <summary>
    /// <c>MAX_MONSTER_ATTACK_MSG_LEN</c> (<c>Monster.h:127</c>), which the original really did
    /// enforce — <c>Left(20)</c>, unlike the name fields' broken <c>SetAt(n,'\0')</c> idiom.
    /// </summary>
    public const int MaxMessage = 20;

    private readonly AttackDetails original;

    public MonsterAttackViewModel(AttackDetails attack)
    {
        ArgumentNullException.ThrowIfNull(attack);

        original = attack;
        sides = attack.Sides;
        nbr = attack.Nbr;
        bonus = attack.Bonus;
        attackMessage = attack.AttackMessage;
        spellId = attack.SpellId;
    }

    /// <summary>Sides per die. Zero is legal only when <see cref="SpellId"/> names a spell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int sides;

    /// <summary>How many dice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int nbr;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int bonus;

    /// <summary>Interpolated as "&lt;monster&gt; &lt;message&gt; &lt;target&gt;".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string attackMessage = string.Empty;

    /// <summary>The spell this attack casts instead of, or as well as, rolling damage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(IsDamageless))]
    private string spellId = string.Empty;

    /// <summary>
    /// True when the attack rolls no damage and names no spell, which is an attack that does
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The original clamped sides and count up to 1 unless a spell was set
    /// (<c>MonsterAttackDetails.cpp:83</c>) — the same condition, resolved by rewriting the data
    /// instead of reporting it.
    /// </remarks>
    public bool IsDamageless => (Sides == 0 || Nbr == 0) && SpellId.Length == 0;

    public string Summary =>
        $"{Nbr}d{Sides}{(Bonus == 0 ? string.Empty : Bonus > 0 ? $"+{Bonus}" : Bonus.ToString())}"
        + (AttackMessage.Length > 0 ? $" — {AttackMessage}" : string.Empty)
        + (SpellId.Length > 0 ? $" [{SpellId}]" : string.Empty);

    /// <summary>The attack as the record stores it, with the untouched fields carried over.</summary>
    public AttackDetails Attack => new(Sides, Nbr, Bonus, AttackMessage, SpellId,
                                       original.LegacySpellId, original.SpellClass,
                                       original.SpellLevel);

    /// <summary>
    /// A new attack, seeded as <c>CMonsterAttacksDlg</c> seeded one.
    /// </summary>
    /// <remarks>
    /// <c>sides = 6</c> and the message <c>"attacks"</c> (<c>MonsterAttacksDlg.cpp:74</c>), with
    /// the die count left at zero and then bumped to 1 for display by the detail dialog. Seeded at
    /// 1 here so the row is not born in the state <see cref="IsDamageless"/> warns about.
    /// </remarks>
    public static AttackDetails NewAttack() =>
        new(6, 1, 0, MonsterRecordReader.DefaultAttackMessage, string.Empty, -1, 0, 0);
}
