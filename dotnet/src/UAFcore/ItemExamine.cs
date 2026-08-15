using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The inventory's EXAMINE entry, which is not one command but whatever a design makes of it
/// (<c>CAN_EXAMINE_OR_WHATEVER</c> and <c>EXAMINE_OR_WHATEVER</c>, <c>RunEvent.cpp:8640</c>,
/// <c>:8239</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>"EXAMINE" is a default label, not the command's name.</b> The entry is renamed per item from
/// the item's own <c>ExamineLabel</c>, and a script may rename it again — the reference's own
/// comments call it "EXAMINE (or whatever)" throughout. So a design can put READ, DRINK or LIGHT
/// there and this is the machinery behind all of them.
/// </para>
/// <para>
/// <b>An item with no <c>ExamineLabel</c> has no entry at all.</b> That is the gate: the label's
/// emptiness, checked before any script runs, is what greys the entry out for most things a
/// character carries.
/// </para>
/// </remarks>
public static class ItemExamine
{
    /// <summary>The hook asked whether the entry should show, and what it should say.</summary>
    public const string CanExamineHook = "CAN_EXAMINE_OR_WHATEVER";

    /// <summary>The hook run when the entry is chosen.</summary>
    public const string ExamineHook = "EXAMINE_OR_WHATEVER";

    /// <summary>
    /// The hook slot carrying the entry's label, in and out
    /// (<c>hookParameters[5]</c>).
    /// </summary>
    /// <remarks>
    /// <b>Seeded with the item's label and read back afterwards</b>, so a script changes the menu
    /// text by writing to it. The slot is the whole channel — there is no return value for this.
    /// </remarks>
    public const int LabelSlot = 5;

    /// <summary>The slot carrying the item's row on the character (<c>hookParameters[4]</c>).</summary>
    public const int RowSlot = 4;

    /// <summary>
    /// The slot a script writes a keyboard shortcut into (<c>hookParameters[6]</c>).
    /// </summary>
    /// <remarks>
    /// <b>Left empty means "choose one for me"</b>: the reference substitutes <c>-1</c> and sets a
    /// flag to derive a shortcut later, once every item has been through this.
    /// </remarks>
    public const int ShortcutSlot = 6;

    /// <summary>What a design made of the EXAMINE entry for one item.</summary>
    /// <param name="Label">What the entry should say. Empty means there is no entry.</param>
    /// <param name="Enabled">Whether it may be chosen.</param>
    /// <param name="Shortcut">The key that picks it, or −1 for "derive one".</param>
    public readonly record struct ExamineEntry(string Label, bool Enabled, int Shortcut)
    {
        /// <summary>No entry — the item has no label, so nothing is offered.</summary>
        public static readonly ExamineEntry None = new(string.Empty, false, -1);
    }

    /// <summary>
    /// What the EXAMINE entry should look like for one carried item.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both the character's scripts and the item's run, and the two ANSWERS CHAIN.</b> The
    /// reference does not read the character's return value into a variable
    /// (<c>RunEvent.cpp:8653</c>) — but it does not need to: every run seeds its result from hook
    /// parameter 0 and writes back to it, and slot 0 <i>is</i> the return value. So a character
    /// script's answer carries into the item's run and stands whenever the item has nothing of its
    /// own to say. A character can disable the entry after all.
    /// </para>
    /// <para>
    /// <b>Only the first character of the item's answer is looked at, and only 'Y' enables.</b> An
    /// empty answer leaves the entry as it was — enabled — so a design that does not answer gets
    /// the entry, and one that answers anything but Y loses it.
    /// </para>
    /// </remarks>
    public static ExamineEntry EntryFor(CharacterRecord who, ItemRecord item, int row,
                                        GlobalScripts scripts, GpdlUnhostedEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(host);

        if (string.IsNullOrEmpty(item.Tail.ExamineLabel))
        {
            return ExamineEntry.None;
        }

        host.HookParameters[RowSlot] = row.ToString(System.Globalization.CultureInfo.InvariantCulture);
        host.HookParameters[LabelSlot] = item.Tail.ExamineLabel;
        host.HookParameters[ShortcutSlot] = string.Empty;

        // The character's answer is thrown away; it runs so its scripts can rewrite the label.
        SpecabScripts.Run(who.SpecialAbilities, CanExamineHook, scripts, host,
                          ScriptCallbacks.RunAll);

        string answer = SpecabScripts.Run(item.Tail.SpecialAbilities, CanExamineHook, scripts,
                                          host, ScriptCallbacks.RunAll);

        string label = host.HookParameters[LabelSlot];
        string shortcut = host.HookParameters[ShortcutSlot];

        // Empty answers YES: the entry stays as it was.
        bool enabled = answer.Length == 0 || answer[0] == 'Y';

        return new ExamineEntry(label, enabled,
                                shortcut.Length == 0 ? -1 : MfcString.Atoi(shortcut));
    }

    /// <summary>
    /// Runs the entry (<c>RunEvent.cpp:8239</c>).
    /// </summary>
    /// <returns>
    /// What the item's scripts answered — <c>"CastSpell"</c> and its siblings, which the caller
    /// acts on. Empty when nothing was said.
    /// </returns>
    /// <remarks>
    /// <b>The character's scripts run first and their answer chains into the item's</b>, through
    /// hook parameter 0 — see <see cref="EntryFor"/>. So an item with no scripts of its own returns
    /// whatever the character said, which is why the reference does not need to capture it.
    /// </remarks>
    public static string Choose(CharacterRecord who, ItemRecord item,
                                GlobalScripts scripts, GpdlUnhostedEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(host);

        SpecabScripts.Run(who.SpecialAbilities, ExamineHook, scripts, host,
                          ScriptCallbacks.RunAll);

        return SpecabScripts.Run(item.Tail.SpecialAbilities, ExamineHook, scripts, host,
                                 ScriptCallbacks.RunAll);
    }
}
