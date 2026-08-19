namespace UAF.Serialization;

/// <summary>One entry of a converted ability: a name, a value, and whether it is GPDL.</summary>
public sealed record SpecabDefinitionEntry(string Name, string Value, bool IsScript);

/// <summary>
/// An ability the conversion invented, to be added to the design's <c>specialAbilities.txt</c>.
/// </summary>
public sealed record SpecabDefinition(string Name, IReadOnlyList<SpecabDefinitionEntry> Entries);

/// <summary>The converted block, plus the abilities the design must gain for it to mean anything.</summary>
public sealed record SpecabUpgradeResult(SpecabBlock Block, IReadOnlyList<SpecabDefinition> Added);

/// <summary>
/// Turns a pre-0.921 special-abilities block into modern named pairs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The conversion has two outputs, which is what makes it more than a reshuffle.</b> Below
/// 0.921 an object carries its abilities inline — an activation script, a deactivation script and
/// up to twelve messages, per slot. Above it, an object carries only <i>names</i>, and the scripts
/// live in the design's shared ability database. So converting a record also invents entries for
/// that database (<c>Specab.cpp:1250</c>); dropping them would leave every converted item pointing
/// at an ability that does not exist.
/// </para>
/// <para>
/// <b>The slot index is the ability's identity.</b> Slot <i>i</i> is
/// <c>spellAbilitiesText[i]</c> — slot 1 is "Bless", slot 22 "Vorpal Attack" — and the invented
/// name is <c>type_name_slot</c>, so an item called Sword with something in slot 22 produces
/// <c>item_Sword_Vorpal Attack</c>. Two objects with the same name and the same slot collide, and
/// the reference has that too.
/// </para>
/// <para>
/// <b>The compiled binaries are dropped.</b> A slot carries both source and compiled bytecode; the
/// reference copies only the source into the new ability and lets it recompile. Carrying the
/// bytecode across would pin a design to a compiler build.
/// </para>
/// </remarks>
public static class SpecabUpgrade
{
    /// <summary>
    /// The ability slots, in order (<c>spellAbilitiesText</c>, <c>Globtext.cpp:556</c>).
    /// </summary>
    /// <remarks>
    /// Slot 0 is "None" and is a real slot: a design may have put a script in it, and the
    /// reference names the ability after it like any other.
    /// </remarks>
    public static IReadOnlyList<string> SlotNames { get; } =
    [
        "None", "Bless", "Curse", "Fear, (Undead only)", "Enlarge", "Reduce", "Charm Person",
        "Detect Magic", "Reflect Gaze Attack", "Prot from Evil", "Prot from Good", "Shield",
        "Sleep", "Fog", "Entangle", "Invisible to Animals", "Invisible to Undead",
        "Fear, (except Undead)", "Sanctuary", "Shillelagh", "Displacement", "Wizardry",
        "Vorpal Attack", "Hold Person", "Silenced", "Poisoned", "Slow Poison", "Mirror Image",
        "Invisible", "Enfeebled", "Blinded", "Diseased",
    ];

    /// <summary>
    /// The message slots (<c>SpecAbMsgText</c>, <c>Globtext.cpp:685</c>), each stored as
    /// "<c>&lt;name&gt; Msg</c>".
    /// </summary>
    public static IReadOnlyList<string> MessageNames { get; } =
    [
        "None", "Begin Casting Spell", "Cast Spell", "Flee", "Turn Undead", "Bandage", "Guard",
        "Attack", "Move", "End Turn", "Delay Turn", "Ready Item",
    ];

    /// <summary><c>Specab.cpp:57</c>.</summary>
    public const string ActivationScriptName = "Activation Script";

    /// <summary><c>Specab.cpp:58</c>.</summary>
    public const string DeactivationScriptName = "DeActivation Script";

    /// <summary>Whether this block still needs converting.</summary>
    public static bool NeedsUpgrade(SpecabBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.LegacySlots.Count > 0 || block.LegacyOrdinals.Count > 0;
    }

    /// <summary>The name the conversion gives slot <paramref name="slot"/> of an object.</summary>
    public static string AbilityName(string ownerType, string ownerName, int slot) =>
        $"{ownerType}_{ownerName}_{(slot >= 0 && slot < SlotNames.Count ? SlotNames[slot] : slot.ToString())}";

    /// <summary>
    /// Converts a block, naming the abilities after the object that carried it.
    /// </summary>
    /// <param name="ownerType">"item", "monster" or "spell", as the reference passes it.</param>
    /// <param name="ownerName">The object's id name.</param>
    /// <remarks>
    /// <para>
    /// <b>An empty slot contributes nothing at all</b> — not an ability, not a pair. The
    /// reference's test sums the lengths of every string and adds one when any message type is
    /// set, so a slot that only set <c>DisplayOnce</c> counts as empty and is dropped. That test
    /// is <see cref="LegacySpecabSlot.IsKept"/>.
    /// </para>
    /// <para>
    /// <b>The oldest shape — a bare array of ordinals, below 0.850 — is not converted here.</b>
    /// The reference does not convert it either on this path: it calls <c>EnableSpecAb</c> with
    /// empty scripts, which turns on a built-in ability rather than inventing a definition. A
    /// block still holding ordinals comes back unchanged and still cannot be written.
    /// </para>
    /// </remarks>
    public static SpecabUpgradeResult Convert(SpecabBlock block, string ownerType, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.LegacySlots.Count == 0)
        {
            return new SpecabUpgradeResult(block, []);
        }

        var pairs = new List<SpecabPair>(block.Pairs);
        var added = new List<SpecabDefinition>();

        for (int slot = 0; slot < block.LegacySlots.Count; slot++)
        {
            var legacy = block.LegacySlots[slot];
            if (!legacy.IsKept)
            {
                continue;
            }

            string name = AbilityName(ownerType, ownerName, slot);

            // The object names the ability and holds no value of its own -- the scripts have moved
            // into the design's database.
            pairs.Add(new SpecabPair(name, string.Empty));
            added.Add(new SpecabDefinition(name, [.. Entries(legacy)]));
        }

        return new SpecabUpgradeResult(
            new SpecabBlock(pairs, [], block.LegacyOrdinals), added);
    }

    /// <summary>The strings one slot becomes, skipping the empty ones as the reference does.</summary>
    private static IEnumerable<SpecabDefinitionEntry> Entries(LegacySpecabSlot legacy)
    {
        if (legacy.ActivationScript.Length != 0)
        {
            yield return new SpecabDefinitionEntry(ActivationScriptName, legacy.ActivationScript,
                                                   IsScript: true);
        }

        if (legacy.DeactivationScript.Length != 0)
        {
            yield return new SpecabDefinitionEntry(DeactivationScriptName,
                                                   legacy.DeactivationScript, IsScript: true);
        }

        // Messages are positional: the j'th message is named for the j'th action, and a design
        // with a message for "Attack" has it at that index whether or not the earlier ones are set.
        for (int j = 0; j < legacy.Messages.Count && j < MessageNames.Count; j++)
        {
            if (legacy.Messages[j].Length != 0)
            {
                yield return new SpecabDefinitionEntry($"{MessageNames[j]} Msg", legacy.Messages[j],
                                                       IsScript: false);
            }
        }
    }
}
