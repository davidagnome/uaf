using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Applies a <c>SPECIAL_ITEM_KEY_EVENT_DATA</c>'s give/take list
/// (<c>SPECIAL_ITEM_KEY_EVENT_DATA::OnKeypress</c>, <c>RunEvent.cpp:12800</c>).
/// </summary>
/// <remarks>
/// <para>
/// Special items and keys are the design's inventory of plot tokens: quest objects, gate keys,
/// anything an <see cref="EventTrigger"/> can test for. They are <b>global</b>, not carried by a
/// character — which is why they live on <see cref="WorldState"/> and not on a party member.
/// </para>
/// <para>
/// The event presents itself first and applies on Return, so the list is applied by
/// <see cref="EventRunner"/> through a host callback rather than here.
/// </para>
/// </remarks>
public static class SpecialItems
{
    /// <summary><c>ITEM_FLAG</c> (<c>Externs.h:890</c>) — a special item rather than a key.</summary>
    public const byte ItemFlag = 0x01;

    /// <summary><c>KEY_FLAG</c> (<c>Externs.h:891</c>).</summary>
    public const byte KeyFlag = 0x02;

    /// <summary><c>SPECIAL_OBJECT_TAKE</c> (<c>GameEvent.h:54</c>).</summary>
    public const byte Take = 0x01;

    /// <summary><c>SPECIAL_OBJECT_GIVE</c> (<c>GameEvent.h:55</c>).</summary>
    public const byte Give = 0x02;

    /// <summary>
    /// Gives and takes everything the event lists, in list order.
    /// </summary>
    /// <returns>How many entries actually changed something.</returns>
    /// <remarks>
    /// <para>
    /// <b>Possession is a stage, and the stage doubles as the flag.</b> Giving sets stage 1 and
    /// taking sets stage 0 — there is no separate "held" bit, so
    /// <see cref="WorldState.HasSpecialItem"/> asks whether the stage is above zero. Giving an item
    /// the party already holds therefore <i>resets it to stage 1</i> in the reference — except that
    /// it guards with <c>if (!hasSpecialItem(...))</c> first, so it does not. Both halves of that
    /// are reproduced: the guard is what stops a re-give from rewinding an item's progress.
    /// </para>
    /// <para>
    /// <b>An id the design does not define is skipped, not created.</b> The reference logs "Bogus
    /// special item index" and returns. An event left pointing at a deleted item is therefore
    /// silent rather than resurrecting it.
    /// </para>
    /// <para>
    /// Any <c>ItemType</c> that is neither <see cref="ItemFlag"/> nor <see cref="KeyFlag"/>, and
    /// any operation that is neither give nor take, falls through both switches and does nothing —
    /// as the reference's do.
    /// </para>
    /// </remarks>
    public static int Apply(SpecialItemEvent special, WorldState world)
    {
        ArgumentNullException.ThrowIfNull(special);
        ArgumentNullException.ThrowIfNull(world);

        int changed = 0;

        foreach (var entry in special.Items)
        {
            changed += entry.Operation switch
            {
                Give => GiveOne(entry, world),
                Take => TakeOne(entry, world),
                _ => 0,
            };
        }

        return changed;
    }

    private static int GiveOne(SpecialObjectEvent entry, WorldState world)
    {
        switch (entry.ItemType)
        {
            case ItemFlag when world.DefinesSpecialItem(entry.Index) &&
                               !world.HasSpecialItem(entry.Index):
                world.SetSpecialItemStage(entry.Index, 1);
                return 1;

            case KeyFlag when world.DefinesKey(entry.Index) && !world.HasKey(entry.Index):
                world.SetKeyStage(entry.Index, 1);
                return 1;

            default:
                return 0;
        }
    }

    private static int TakeOne(SpecialObjectEvent entry, WorldState world)
    {
        switch (entry.ItemType)
        {
            case ItemFlag when world.HasSpecialItem(entry.Index):
                world.SetSpecialItemStage(entry.Index, 0);
                return 1;

            case KeyFlag when world.HasKey(entry.Index):
                world.SetKeyStage(entry.Index, 0);
                return 1;

            default:
                return 0;
        }
    }
}
