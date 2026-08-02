using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// How a fight ended, as the design's scripts read it
/// (<c>globalData.global_asl["Combat Result"]</c>, <c>RunEvent.cpp:19742</c>).
/// </summary>
/// <remarks>
/// The strings are the interface: a design tests them by name, so they are transcribed rather than
/// derived from the enum.
/// </remarks>
public enum CombatResult
{
    /// <summary>"Win".</summary>
    Win,

    /// <summary>"Lose".</summary>
    Lose,

    /// <summary>"LoseButNeverDies" — the encounter's <c>partyNeverDies</c> flag was set.</summary>
    LoseButNeverDies,

    /// <summary>"Flee" — monsters won, but somebody got away.</summary>
    Flee,
}

/// <summary>What a finished fight yielded.</summary>
/// <param name="Result">What the design's scripts will read.</param>
/// <param name="Experience">Total experience, before it is shared out.</param>
/// <param name="Items">Everything the party may pick up.</param>
/// <param name="Money">Coins from the fallen.</param>
public readonly record struct CombatSpoils(
    CombatResult Result, int Experience,
    IReadOnlyList<ItemInstance> Items, IReadOnlyList<MoneySack> Money);

/// <summary>
/// What happens once the fighting stops (<c>DetermineVictoryExpPoints</c> and
/// <c>COMBAT_RESULTS_MENU_DATA::OnInitialEvent</c>, <c>Combatants.cpp:4315</c>,
/// <c>RunEvent.cpp:19669</c>).
/// </summary>
public static class CombatAftermath
{
    /// <summary>
    /// What the design's scripts are told (<c>combatVictorType</c> → the "Combat Result" ASL).
    /// </summary>
    /// <param name="partyNeverDies">The encounter's <c>partyNeverDies</c> flag.</param>
    /// <remarks>
    /// <b>"Fled" is derived after the fact, not decided during the fight.</b> The reference settles
    /// on <c>MonsterWins</c> and only then scans the party for anyone with status <c>Fled</c>,
    /// promoting the result to <c>PartyRanAway</c> if it finds one (<c>Combatants.cpp:4344</c>).
    /// So a loss where one character escaped is a flight, not a defeat — and the check is
    /// <i>any</i> member, not all of them.
    /// </remarks>
    public static CombatResult ResultOf(CombatOutcome outcome, IEnumerable<Combatant> combatants,
                                        bool partyNeverDies = false)
    {
        ArgumentNullException.ThrowIfNull(combatants);

        if (outcome == CombatOutcome.PartyWon)
        {
            return CombatResult.Win;
        }

        if (combatants.Any(c => c.IsFriendly && c.Status == CharacterStatus.Fled))
        {
            return CombatResult.Flee;
        }

        return partyNeverDies ? CombatResult.LoseButNeverDies : CombatResult.Lose;
    }

    /// <summary>The string a design's scripts compare against.</summary>
    public static string ResultText(CombatResult result) => result switch
    {
        CombatResult.Win => "Win",
        CombatResult.Flee => "Flee",
        CombatResult.LoseButNeverDies => "LoseButNeverDies",
        _ => "Lose",
    };

    /// <summary>
    /// The experience a won fight is worth (<c>DetermineVictoryExpPoints</c>).
    /// </summary>
    /// <param name="experienceOf">What one combatant is worth (<c>getCharExpWorth</c>).</param>
    /// <param name="monsterExperienceModifier">
    /// The design's percentage bonus (<c>GetMonsterExpMod</c>), added on top: 100 doubles it.
    /// </param>
    /// <param name="partyNoExperience">The encounter's flag; awards nothing when set.</param>
    /// <remarks>
    /// <b>Only the <i>dead</i> count.</b> A monster that fled, was turned, or is merely unconscious
    /// is worth nothing — the test is <c>GetAdjStatus() == Dead</c> and nothing else. A fight won by
    /// driving everything off the map therefore pays no experience at all.
    /// </remarks>
    public static int ExperienceFor(IEnumerable<Combatant> combatants,
                                    Func<Combatant, int> experienceOf,
                                    double monsterExperienceModifier = 0,
                                    bool partyNoExperience = false)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(experienceOf);

        if (partyNoExperience)
        {
            return 0;
        }

        int total = combatants
            .Where(c => !c.IsFriendly && c.Status == CharacterStatus.Dead)
            .Sum(experienceOf);

        if (monsterExperienceModifier != 0)
        {
            total += (int)(monsterExperienceModifier / 100.0 * total);
        }

        return Math.Max(0, total);
    }

    /// <summary>
    /// Shares experience out (<c>PARTY::distributeExpPoints</c>, <c>Party.cpp:3054</c>).
    /// </summary>
    /// <returns>How many characters received a share.</returns>
    /// <remarks>
    /// <b>Only characters with status <see cref="CharacterStatus.Okay"/> share</b> — the
    /// unconscious, the dying and the dead all get nothing, so a fight won at great cost pays the
    /// survivors more each. And <b>the whole remainder goes to the first of them</b> rather than
    /// being spread: with three survivors and 100 points the first gets 34 and the others 33.
    /// </remarks>
    public static int Distribute(IReadOnlyList<Character> party, int total)
    {
        ArgumentNullException.ThrowIfNull(party);

        if (total <= 0)
        {
            return 0;
        }

        var sharing = party.Where(c => c.Status == CharacterStatus.Okay).ToList();
        if (sharing.Count == 0)
        {
            return 0;
        }

        int share = total / sharing.Count;
        int remainder = total % sharing.Count;

        foreach (var character in sharing)
        {
            character.GiveExperience(share + remainder);
            remainder = 0;
        }

        return sharing.Count;
    }

    /// <summary>
    /// What the fallen leave behind (the treasure loop, <c>RunEvent.cpp:19800</c>).
    /// </summary>
    /// <param name="itemInfo">The item database, for the drop rule.</param>
    /// <param name="hurled">Thrown weapons lying on the map, which are picked up too.</param>
    /// <param name="noMonsterTreasure">The encounter's flag; yields nothing when set.</param>
    /// <remarks>
    /// <b>Only the dead are looted</b>, on the same <c>GetAdjStatus() == Dead</c> test the
    /// experience uses — so a monster driven off keeps its possessions.
    /// <para>
    /// <b>A monster's spell-casting items do not drop by default.</b> The filter is
    /// <c>(Wpn_Type != SpellCaster &amp;&amp; Wpn_Type != SpellLikeAbility) || CanBeTradeDropSoldDep</c>,
    /// so a wand a monster used is kept out of the treasure unless the design explicitly marks it
    /// tradeable. Dropping the filter hands the party every enemy wand in the game.
    /// </para>
    /// </remarks>
    public static (List<ItemInstance> Items, List<MoneySack> Money) Loot(
        IEnumerable<Combatant> combatants, Func<string, ItemRecord?> itemInfo,
        IEnumerable<ItemInstance>? hurled = null, bool noMonsterTreasure = false)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(itemInfo);

        var items = new List<ItemInstance>();
        var money = new List<MoneySack>();

        if (noMonsterTreasure)
        {
            return (items, money);
        }

        foreach (var fallen in combatants.Where(c => !c.IsFriendly
                                                     && c.Status == CharacterStatus.Dead))
        {
            items.AddRange(fallen.Items.Where(i => Drops(itemInfo(i.ItemId))));

            if (fallen.Money is { } purse)
            {
                money.Add(purse);
            }
        }

        items.AddRange(hurled ?? []);
        return (items, money);
    }

    private static bool Drops(ItemRecord? item) =>
        item is not null
        && ((item.Tail.WeaponType != (int)WeaponClass.SpellCaster
             && item.Tail.WeaponType != (int)WeaponClass.SpellLikeAbility)
            || item.Tail.CanBeTradeDropSoldDep != 0);

    /// <summary>
    /// Merges several fallen monsters' purses into one
    /// (<c>m_pTreasEvent-&gt;money.Add(...)</c>, <c>RunEvent.cpp:19817</c>).
    /// </summary>
    /// <remarks>
    /// Coins add per denomination; gems and jewellery are individual objects and simply pile up.
    /// A short coin list is treated as zeroes beyond its end rather than as an error, because a
    /// monster record need not carry every denomination.
    /// </remarks>
    public static MoneySack Merge(IEnumerable<MoneySack> purses)
    {
        ArgumentNullException.ThrowIfNull(purses);

        var all = purses.ToList();
        int denominations = all.Count == 0 ? 0 : all.Max(p => p.Coins.Count);

        var coins = new int[denominations];
        foreach (var purse in all)
        {
            for (int i = 0; i < purse.Coins.Count; i++)
            {
                coins[i] += purse.Coins[i];
            }
        }

        return new MoneySack(coins,
                             [.. all.SelectMany(p => p.Gems)],
                             [.. all.SelectMany(p => p.Jewelry)]);
    }

    /// <summary>
    /// Whether a pile is worth showing (<c>RunEvent.cpp:19851</c>).
    /// </summary>
    /// <remarks>
    /// <b>An empty pile is not offered at all</b> — the reference deletes the treasure event
    /// rather than pushing an empty screen, and combat exits straight to the chain.
    /// </remarks>
    public static bool IsWorthShowing(IReadOnlyList<ItemInstance> items, MoneySack money)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(money);

        return items.Count > 0
               || money.Coins.Any(c => c > 0)
               || money.Gems.Count > 0
               || money.Jewelry.Count > 0;
    }

    /// <summary>
    /// The experience an item is worth, added on top of the monsters'
    /// (<c>RunEvent.cpp:19685</c>).
    /// </summary>
    /// <remarks>
    /// Counted from the <i>treasure</i>, not from what the party already carries, and added before
    /// the total is shared out — so picking up a magic sword pays experience for finding it.
    /// </remarks>
    public static int ExperienceIn(IEnumerable<ItemInstance> items,
                                   Func<string, ItemRecord?> itemInfo)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemInfo);

        return items.Sum(i => itemInfo(i.ItemId)?.Scalars.Experience ?? 0);
    }
}
