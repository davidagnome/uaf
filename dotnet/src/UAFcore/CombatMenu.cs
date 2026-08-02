using UAF.Media;

namespace UAFcore;

/// <summary>
/// The combat menu's commands (<c>CombatMenu</c>, <c>GameMenu.cpp:1143</c>).
/// </summary>
/// <remarks>
/// <b>One-based, because the menu is.</b> <c>setItemInactive</c> takes the position in the list
/// rather than an index, and the reference's own commented-out key at
/// <c>RunEvent.cpp:19566</c> numbers them from 1. The same convention caught the treasure screen.
/// </remarks>
public enum CombatCommand
{
    Move = 1,
    Aim = 2,
    Use = 3,
    Cast = 4,
    Turn = 5,
    Guard = 6,
    Quick = 7,
    Delay = 8,
    Bandage = 9,
    View = 10,
    Speed = 11,
    Win = 12,
    Ready = 13,
    End = 14,

    /// <summary>A design-supplied action; "Sweep" is the reference's own example.</summary>
    Special = 15,
}

/// <summary>
/// What the acting combatant can currently do, which decides the menu.
/// </summary>
/// <param name="SpecialActionName">
/// The label for <see cref="CombatCommand.Special"/>, from the <c>COMBAT_MAIN_MENU</c> script
/// hook. Empty disables the entry.
/// </param>
public readonly record struct CombatOptions(
    bool CanMove = true, bool CanCast = true, bool ZoneAllowsMagic = true,
    bool CanTurnUndead = false, bool CanGuard = true, bool CanDelay = true,
    bool CanBandage = false, bool IsEditor = false, string SpecialActionName = "");

/// <summary>
/// The player's combat menu (<c>COMBAT_EVENT_DATA::OnUpdateUI</c>, <c>RunEvent.cpp:19533</c>).
/// </summary>
/// <remarks>
/// The first thing in this port that a player drives rather than watches. Everything ported so far
/// has been computer-run, and the menu is what makes the difference.
/// </remarks>
public static class CombatMenu
{
    /// <summary>The labels, in order.</summary>
    public static readonly string[] Labels =
    [
        "MOVE", "AIM", "USE", "CAST", "TURN", "GUARD", "QUICK", "DELAY",
        "BANDAGE", "VIEW", "SPEED", "WIN", "READY", "END", "SWEEP",
    ];

    /// <summary>
    /// Fills a menu with the combat commands and applies the enable rules.
    /// </summary>
    /// <param name="acting">
    /// Whether a player-run combatant is waiting for orders. When false the whole menu goes
    /// inactive — the reference does that both when there is no current combatant and when the
    /// current one is computer-run.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b><see cref="CombatCommand.Ready"/> is disabled unconditionally</b> (`:19584`), before any
    /// of the conditional rules. Not a stub — the reference simply never enables it.
    /// </para>
    /// <para>
    /// <b><see cref="CombatCommand.Win"/> is editor-only.</b> It forces a victory, so it is gated
    /// on <c>EditorMode()</c> rather than on anything about the fight.
    /// </para>
    /// <para>
    /// <b>Casting is refused for two separate reasons</b> — the caster cannot, or the <i>zone</i>
    /// forbids magic — and the reference tests them one after the other against the same entry.
    /// A design can therefore have a no-magic zone that silently disables spellcasting for
    /// everyone in it.
    /// </para>
    /// <para>
    /// <see cref="CombatCommand.Special"/> is relabelled from the script hook when it has a name,
    /// and disabled when it does not.
    /// </para>
    /// </remarks>
    public static void Build(Menu menu, CombatOptions options, bool acting = true)
    {
        ArgumentNullException.ThrowIfNull(menu);

        menu.Reset();
        menu.Orientation = MenuOrientation.Horizontal;

        for (int i = 0; i < Labels.Length; i++)
        {
            // The special entry is relabelled from the script hook when it has a name. The
            // reference calls changeMenuItem after the fact; substituting here needs no such call.
            bool isSpecial = (CombatCommand)(i + 1) == CombatCommand.Special;
            menu.AddItem(isSpecial && !string.IsNullOrEmpty(options.SpecialActionName)
                ? options.SpecialActionName
                : Labels[i]);
        }

        if (!acting)
        {
            menu.SetAllItemsEnabled(false);
            return;
        }

        menu.SetAllItemsEnabled(true);

        // Unconditional, and first.
        Enable(menu, CombatCommand.Ready, false);

        Enable(menu, CombatCommand.Move, options.CanMove);
        Enable(menu, CombatCommand.Cast, options.CanCast && options.ZoneAllowsMagic);
        Enable(menu, CombatCommand.Turn, options.CanTurnUndead);
        Enable(menu, CombatCommand.Guard, options.CanGuard);
        Enable(menu, CombatCommand.Delay, options.CanDelay);
        Enable(menu, CombatCommand.Bandage, options.CanBandage);
        Enable(menu, CombatCommand.Win, options.IsEditor);
        Enable(menu, CombatCommand.Special, !string.IsNullOrEmpty(options.SpecialActionName));
    }

    /// <summary>
    /// Enables a command, converting from the reference's numbering to the menu's.
    /// </summary>
    /// <remarks>
    /// <b><see cref="CombatCommand"/> is one-based and <see cref="Menu.SetItemEnabled"/> is
    /// zero-based.</b> The enum keeps the reference's numbering so a reader can check it against
    /// <c>RunEvent.cpp:19566</c> directly; the conversion happens here, once, rather than being
    /// silently absorbed into the enum. Passing a command straight to the menu disables its
    /// neighbour — which is exactly the trap the treasure screen's own indices set.
    /// </remarks>
    public static void Enable(Menu menu, CombatCommand command, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.SetItemEnabled((int)command - 1, enabled);
    }

    /// <summary>The command at a zero-based menu position.</summary>
    public static CombatCommand At(int menuIndex) => (CombatCommand)(menuIndex + 1);

    /// <summary>
    /// The AIM submenu (<c>AimMenu</c>, <c>GameMenu.cpp:1189</c>).
    /// </summary>
    public static readonly string[] AimLabels =
        ["NEXT", "PREV", "MANUAL", "TARGET", "CENTER", "EXIT"];

    /// <summary>
    /// The manual-aim submenu (<c>AimManualMenu</c>, <c>GameMenu.cpp:1205</c>), where the arrow
    /// keys steer the cursor and these two end it.
    /// </summary>
    public static readonly string[] AimManualLabels = ["TARGET", "EXIT"];

    /// <summary>Fills a menu with the AIM submenu.</summary>
    public static void BuildAim(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        Fill(menu, AimLabels);
    }

    /// <summary>
    /// The CAST menu's entries (<c>CastMenuData</c>, <c>RunEvent.cpp:25761</c>).
    /// </summary>
    /// <remarks>
    /// NEXT and PREV page the spell list rather than moving one entry — the reference shows a
    /// pageful at a time (<c>nextSpellPage</c>) and the arrow keys move within it.
    /// </remarks>
    public static readonly string[] CastLabels = ["CAST", "NEXT", "PREV", "EXIT"];

    /// <summary>Puts the CAST menu up.</summary>
    public static void BuildCast(Menu menu) => Fill(menu, CastLabels);

    /// <summary>
    /// The USE menu's entries, over the carried items (<c>ITEMS_MENU_DATA</c>,
    /// <c>RunEvent.cpp:15917</c>).
    /// </summary>
    /// <remarks>
    /// The reference shows the whole item screen here, with far more than four entries; this is
    /// the subset combat needs — pick one thing and invoke it.
    /// </remarks>
    public static readonly string[] UseLabels = ["USE", "NEXT", "PREV", "EXIT"];

    /// <summary>Puts the USE menu up.</summary>
    public static void BuildUse(Menu menu) => Fill(menu, UseLabels);

    /// <summary>Fills a menu with the manual-aim submenu.</summary>
    public static void BuildAimManual(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        Fill(menu, AimManualLabels);
    }

    private static void Fill(Menu menu, string[] labels)
    {
        menu.Reset();
        menu.Orientation = MenuOrientation.Horizontal;
        foreach (string label in labels)
        {
            menu.AddItem(label);
        }
        menu.SetAllItemsEnabled(true);
    }
}

/// <summary>What the player's menu is currently asking for.</summary>
/// <remarks>
/// The reference models these as separate pushed events — <c>COMBAT_AIM_MENU_DATA</c> and
/// <c>COMBAT_AIM_MANUAL_MENU_DATA</c> replace or stack on the main menu and pop when done. A mode
/// on the session is the same shape without the event stack, which this port does not have.
/// </remarks>
public enum CombatMenuMode
{
    /// <summary>The main fifteen commands.</summary>
    Command,

    /// <summary>Choosing a target: NEXT, PREV, MANUAL, TARGET, CENTER, EXIT.</summary>
    Aiming,

    /// <summary>Steering the cursor by hand: arrows move, TARGET and EXIT end it.</summary>
    AimingManual,

    /// <summary>Picking a spell from the book: CAST, NEXT, PREV, EXIT.</summary>
    ChoosingSpell,

    /// <summary>
    /// Naming a spell's targets. The same six entries as <see cref="Aiming"/> — the reference
    /// builds both from <c>AimMenuData</c> — but TARGET takes a target and carries on rather than
    /// ending the turn.
    /// </summary>
    SpellAiming,

    /// <summary>Steering the cursor by hand while naming a spell's targets.</summary>
    SpellAimingManual,

    /// <summary>Picking an item to use: USE, NEXT, PREV, EXIT.</summary>
    ChoosingItem,
}

/// <summary>The CAST submenu's entries, one-based.</summary>
public enum CastCommand
{
    Cast = 1,
    Next = 2,
    Previous = 3,
    Exit = 4,
}

/// <summary>The AIM submenu's entries, one-based like the main menu.</summary>
public enum AimCommand
{
    Next = 1,
    Previous = 2,
    Manual = 3,
    Target = 4,
    Center = 5,
    Exit = 6,
}

/// <summary>The manual-aim submenu's entries, one-based.</summary>
public enum AimManualCommand
{
    Target = 1,
    Exit = 2,
}

/// <summary>
/// The targeting cursor (<c>GetNextAim</c> / <c>GetPrevAim</c>, <c>Combatants.cpp:1363</c>).
/// </summary>
/// <remarks>
/// <para>
/// The AIM submenu is NEXT, PREV, MANUAL, TARGET, CENTER, EXIT (<c>GameMenu.cpp:1189</c>): step
/// the cursor between enemies, drive it by hand, or commit. This is the cycling half; manual
/// movement is just setting <see cref="X"/> and <see cref="Y"/>.
/// </para>
/// <para>
/// <b>Only enemies are cycled.</b> Party members and anything friendly are skipped outright, so
/// the cursor cannot be walked onto your own side even though the map has no such restriction.
/// </para>
/// </remarks>
public sealed class AimCursor
{
    /// <summary>Where the cursor sits, in terrain squares.</summary>
    public int X { get; set; }

    /// <inheritdoc cref="X"/>
    public int Y { get; set; }

    /// <summary>The combatant the cursor is on, or <see cref="CombatMap.NoDude"/>.</summary>
    public int Aim { get; private set; } = CombatMap.NoDude;

    /// <summary>
    /// Steps to the next targetable enemy (<c>GetNextAim</c>).
    /// </summary>
    /// <param name="current">
    /// The acting combatant. <b>Returned when nothing targetable is found</b> — the cursor lands
    /// back on the aimer rather than staying where it was or reporting failure.
    /// </param>
    /// <remarks>
    /// The scan advances first and tests afterwards, and an index that has just run off the end
    /// fails its own bounds test before wrapping — so a full sweep costs one extra iteration. The
    /// loop is bounded by the combatant count either way.
    /// </remarks>
    public int Next(IReadOnlyList<Combatant> combatants, Combatant current,
                    Func<Combatant, Combatant, bool>? isValidTarget = null) =>
        Step(combatants, current, isValidTarget, forward: true);

    /// <summary>Steps to the previous targetable enemy (<c>GetPrevAim</c>).</summary>
    public int Previous(IReadOnlyList<Combatant> combatants, Combatant current,
                        Func<Combatant, Combatant, bool>? isValidTarget = null) =>
        Step(combatants, current, isValidTarget, forward: false);

    private int Step(IReadOnlyList<Combatant> combatants, Combatant current,
                     Func<Combatant, Combatant, bool>? isValidTarget, bool forward)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(current);

        for (int tried = 0; tried < combatants.Count; tried++)
        {
            if (Aim < 0 || Aim >= combatants.Count)
            {
                Aim = forward ? 0 : combatants.Count - 1;
            }
            else
            {
                Aim += forward ? 1 : -1;
            }

            if (Aim < 0 || Aim >= combatants.Count)
            {
                continue;       // wraps on the next pass, as the reference does
            }

            var candidate = combatants[Aim];
            if (candidate.IsFriendly || !candidate.IsOnCombatMap(petrifiedOk: true))
            {
                continue;
            }

            if (isValidTarget?.Invoke(current, candidate) == false)
            {
                continue;
            }

            X = candidate.X;
            Y = candidate.Y;
            return Aim;
        }

        // Nothing to aim at: the cursor comes home.
        Aim = current.Index;
        X = current.X;
        Y = current.Y;
        return Aim;
    }

    /// <summary>Moves the cursor by hand, staying on the map (the MANUAL entry).</summary>
    public void MoveBy(CombatMap map, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(map);

        X = Math.Clamp(X + dx, 0, map.Width - 1);
        Y = Math.Clamp(Y + dy, 0, map.Height - 1);
        Aim = map.OccupantAt(X, Y);
    }

    /// <summary>Puts the cursor on a combatant (the CENTER entry, and the start of aiming).</summary>
    public void CenterOn(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        Aim = combatant.Index;
        X = combatant.X;
        Y = combatant.Y;
    }
}
