using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the event bodies that carry a design's content — text, quests, tours, treasure and the
/// logic blocks that gate them.
/// </summary>
/// <remarks>
/// <para>
/// Grouped by what they are rather than split one file per type, as
/// <see cref="SimpleEventWriters"/> is: each is the shared <see cref="GameEventWriter"/> preamble
/// plus a short run of fields. Between them they cover the overwhelming majority of a real level —
/// <c>TextStatement</c> alone is <b>3,451 of the 4,705 events</b> in the two designs that ship
/// levels.
/// </para>
/// <para>
/// Every storing branch here is flat, with the version tests in the loading half — including the
/// two that look most like exceptions. <c>TEXT_EVENT_DATA</c> writes <c>distance</c>
/// unconditionally where the reader gates it at 0.908, and
/// <c>SPECIAL_ITEM_KEY_EVENT_DATA</c> writes its two flags where the reader gates them at 0.830.
/// Mirroring either gate would emit an old shape into a file stamped new.
/// </para>
/// </remarks>
public static class ContentEventWriters
{
    /// <summary>Writes a <c>TEXT_EVENT_DATA</c> (<c>GameEvent.cpp:8516</c>).</summary>
    /// <remarks>
    /// <b>The sound's directory is stripped on the way out</b>, as an item's three sounds and a
    /// character's five are. The text the event displays is not here at all — it lives in the
    /// base's three <c>Text</c> fields.
    /// </remarks>
    public static void WriteText(IArchiveWriteCursor ar, TextEvent text)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(text);

        GameEventWriter.Write(ar, text.Base);

        ar.WriteInt32(text.WaitForReturn);
        ar.WriteInt32(text.ForceBackup);
        ar.WriteInt32(text.HighlightText);
        ar.WriteInt32(text.Distance);
        GameEventWriter.WriteDas(ar, PicDataWriter.StripFilenamePath(text.Sound));
    }

    /// <summary>Writes a <c>QUEST_EVENT_DATA</c> (<c>GameEvent.cpp:9458</c>).</summary>
    /// <remarks>
    /// <b><c>stage</c> is a <c>WORD</c></b> between an <c>int</c> and two <c>DWORD</c>s — 26 bytes,
    /// not 28. Its declaration is separated from its neighbours by a block of inline accessors,
    /// which is what makes the class look like it ends earlier than it does.
    /// </remarks>
    public static void WriteQuest(IArchiveWriteCursor ar, QuestEvent quest)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(quest);

        GameEventWriter.Write(ar, quest.Base);

        ar.WriteInt32(quest.Operation);
        ar.WriteInt32(quest.CompleteOnAccept);
        ar.WriteInt32(quest.FailOnRejection);
        ar.WriteInt32(quest.Quest);                  // packed: type in the top nibble, id below
        ar.WriteUInt16(quest.Stage);                 // WORD
        ar.WriteUInt32(quest.AcceptChain);
        ar.WriteUInt32(quest.RejectChain);
    }

    /// <summary>Writes a <c>GUIDED_TOUR</c> (<c>GameEvent.cpp:7125</c>).</summary>
    /// <remarks>
    /// <b>All <see cref="GuidedTourReader.MaxSteps"/> steps are always on the wire</b>, outside the
    /// storing branch and with no count in front of them; unused ones carry the blank sentinel.
    /// Writing only the steps a tour uses would leave the reader consuming the next event.
    /// </remarks>
    public static void WriteGuidedTour(IArchiveWriteCursor ar, GuidedTour tour)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(tour);

        if (tour.Steps.Count != GuidedTourReader.MaxSteps)
        {
            throw new ArgumentException(
                $"a GUIDED_TOUR writes exactly {GuidedTourReader.MaxSteps} steps, not " +
                $"{tour.Steps.Count}. The count is compile-time in the reference and never " +
                "written, so a short list truncates the event.", nameof(tour));
        }

        GameEventWriter.Write(ar, tour.Base);

        ar.WriteInt32(tour.TourX);
        ar.WriteInt32(tour.TourY);
        ar.WriteInt32(tour.Facing);
        ar.WriteInt32(tour.UseStartLocation);
        ar.WriteInt32(tour.ExecuteEvent);

        foreach (var step in tour.Steps)
        {
            GameEventWriter.WriteDas(ar, step.Text);
            ar.WriteInt32(step.Step);
        }
    }

    /// <summary>Writes a <c>SPECIAL_ITEM_KEY_EVENT_DATA</c> (<c>GameEvent.cpp:9243</c>).</summary>
    /// <remarks>
    /// The item list here is a <c>SPECIAL_OBJECT_EVENT_LIST</c> — ten bytes an entry — and
    /// <b>not</b> the <c>ITEM_LIST</c> that other classes call by the same member name. It sits
    /// outside the storing branch, between the base and the flags.
    /// </remarks>
    public static void WriteSpecialItem(IArchiveWriteCursor ar, SpecialItemEvent special)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(special);

        GameEventWriter.Write(ar, special.Base);

        WriteSpecialObjectList(ar, special.Items);

        ar.WriteInt32(special.ForceExit);
        ar.WriteInt32(special.WaitForReturn);
    }

    /// <summary>Writes a <c>UTILITIES_EVENT_DATA</c> (<c>GameEvent.cpp:10712</c>).</summary>
    /// <remarks>
    /// The densest mix of widths in any event: two <c>BYTE</c>s and a <c>WORD</c> between
    /// <c>int</c>s, giving 24 bytes where a uniform reading would write 32.
    /// </remarks>
    public static void WriteUtilities(IArchiveWriteCursor ar, UtilitiesEvent utilities)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(utilities);

        GameEventWriter.Write(ar, utilities.Base);

        ar.WriteInt32(utilities.EndPlay);
        ar.WriteInt32(utilities.Operation);
        ar.WriteInt32(utilities.ItemCheck);
        ar.WriteByte(utilities.MathItemType);        // BYTE
        ar.WriteByte(utilities.ResultItemType);      // BYTE
        ar.WriteUInt16(utilities.MathAmount);        // WORD
        ar.WriteInt32(utilities.MathItemIndex);
        ar.WriteInt32(utilities.ResultItemIndex);

        WriteSpecialObjectList(ar, utilities.Items);
    }

    /// <summary>
    /// Whether a treasure event can be written, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// A <c>GIVE_TREASURE_DATA</c> below 0.740 carries three loose coin counts that the reference
    /// folds into a <c>MONEY_SACK</c> as it loads (<c>money.Add</c>, <c>money.AddGem</c>); this
    /// port discards them and leaves the sack empty, so writing it would take the treasure. It is
    /// the same shape of loss as a pre-0.661 character's loose coins — and the empty sack, with no
    /// coin slots at all rather than ten zeroed ones, is what identifies it.
    /// </remarks>
    public static bool CanWriteTreasure(TreasureEvent treasure, out string reason)
    {
        ArgumentNullException.ThrowIfNull(treasure);

        if (treasure.Money.Coins.Count != MonsterLeafReaders.MaxCoinTypes)
        {
            reason = $"Treasure event {treasure.Base.Id} was read from a design below 0.740, " +
                     "where the money is three loose counts rather than a MONEY_SACK. The " +
                     "reference folds them in as it loads; this port drops them, so writing the " +
                     "empty sack would take the treasure.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes a <c>GIVE_TREASURE_DATA</c> (<c>GameEvent.cpp:7768</c>).</summary>
    /// <remarks>
    /// Money, then the flag, then the item list — and the list is outside the storing branch, so it
    /// is present at every version. <c>COMBAT_TREASURE</c> is the same event without the flag; the
    /// two are easy to conflate and differ by four bytes in the middle.
    /// </remarks>
    public static void WriteGiveTreasure(IArchiveWriteCursor ar, TreasureEvent treasure)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(treasure);

        if (!CanWriteTreasure(treasure, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        GameEventWriter.Write(ar, treasure.Base);

        MonsterLeafWriters.WriteMoneySack(ar, treasure.Money);
        ar.WriteInt32(treasure.SilentGiveToActiveChar);
        MonsterLeafWriters.WriteItemList(ar, treasure.Items);
    }

    /// <summary>Writes a <c>COMBAT_TREASURE</c> (<c>GameEvent.cpp:7813</c>) — no flag, no legacy form.</summary>
    public static void WriteCombatTreasure(IArchiveWriteCursor ar, TreasureEvent treasure)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(treasure);

        if (!CanWriteTreasure(treasure, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        GameEventWriter.Write(ar, treasure.Base);

        MonsterLeafWriters.WriteMoneySack(ar, treasure.Money);
        MonsterLeafWriters.WriteItemList(ar, treasure.Items);
    }

    /// <summary>Writes a <c>LOGIC_BLOCK_DATA</c> (<c>GameEvent.cpp:14103</c>).</summary>
    /// <remarks>
    /// <para>
    /// The largest event subclass, and the densest byte run in the format: two <c>DWORD</c>s, seven
    /// strings, <b>twenty-six consecutive <c>BYTE</c>s</b>, then one more string. Every one of those
    /// bytes has a name that reads like an enum — <c>m_GateTypeC</c>, <c>m_ActionType1</c>,
    /// <c>m_Flags</c> — so widening even one costs three bytes and desynchronises the rest.
    /// </para>
    /// <para>
    /// <b>The strings are raw, not <c>DAS</c>.</b> An empty terminal parameter stays empty rather
    /// than becoming the blank sentinel.
    /// </para>
    /// </remarks>
    public static void WriteLogicBlock(IArchiveWriteCursor ar, LogicBlockEvent logic)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(logic);

        Expect(logic.Inputs.Count, LogicBlockEventReader.InputTerminals.Length, "inputs");
        Expect(logic.ActionParams.Count, 2, "action parameters");
        Expect(logic.GateTypes.Count, LogicBlockEventReader.GateTerminals.Length, "gate types");
        Expect(logic.InputTypes.Count, LogicBlockEventReader.InputTerminals.Length, "input types");
        Expect(logic.ActionTypes.Count, 2, "action types");
        Expect(logic.Negations.Count, LogicBlockEventReader.NegatedTerminals.Length, "negations");
        Expect(logic.IfTrue.Count, 2, "if-true flags");

        GameEventWriter.Write(ar, logic.Base);

        ar.WriteUInt32(logic.FalseChain);
        ar.WriteUInt32(logic.TrueChain);

        foreach (string input in logic.Inputs) ar.WriteString(input);        // A, B, D, F, G
        foreach (string param in logic.ActionParams) ar.WriteString(param);

        foreach (byte gate in logic.GateTypes) ar.WriteByte(gate);           // C, E, H, I, J, K, L
        foreach (byte input in logic.InputTypes) ar.WriteByte(input);
        foreach (byte action in logic.ActionTypes) ar.WriteByte(action);

        ar.WriteByte(logic.ChainIfFalse);
        ar.WriteByte(logic.ChainIfTrue);
        ar.WriteByte(logic.NoChain);

        foreach (byte negation in logic.Negations) ar.WriteByte(negation);   // C..K -- not L
        foreach (byte ifTrue in logic.IfTrue) ar.WriteByte(ifTrue);

        ar.WriteByte(logic.Flags);
        ar.WriteString(logic.Misc);
    }

    /// <summary>Writes a <c>SPECIAL_OBJECT_EVENT_LIST</c> (<c>GameEvent.cpp:496</c>).</summary>
    /// <remarks>
    /// Ten bytes an entry, not sixteen: <c>ItemType</c> and <c>operation</c> are <c>BYTE</c>s and
    /// only <c>index</c> and <c>id</c> are <c>int</c>s.
    /// </remarks>
    public static void WriteSpecialObjectList(IArchiveWriteCursor ar,
                                              IReadOnlyList<SpecialObjectEvent> items)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(items);

        ar.WriteInt32(items.Count);
        foreach (var item in items)
        {
            ar.WriteByte(item.ItemType);             // BYTE
            ar.WriteByte(item.Operation);            // BYTE
            ar.WriteInt32(item.Index);
            ar.WriteInt32(item.Id);
        }
    }

    private static void Expect(int actual, int expected, string what)
    {
        if (actual != expected)
        {
            throw new ArgumentException(
                $"a LOGIC_BLOCK_DATA writes exactly {expected} {what}, not {actual}. The counts " +
                "are compile-time in the reference and never written, so a short list shifts " +
                "every byte after it.");
        }
    }
}
