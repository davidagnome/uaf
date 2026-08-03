namespace UAF.Serialization;

/// <summary>
/// Writes the town services and their neighbours — the tail of the event layer that a real level
/// still reaches.
/// </summary>
/// <remarks>
/// <para>
/// Ten types between them, and only 26 events across every shipped level: a tavern and a shop
/// appear <b>once each</b> in the whole corpus. That is what makes them the awkward end of the
/// tail — there is no second example to check a guess against, so each was transcribed from its
/// storing branch and the round trip is the only evidence.
/// </para>
/// <para>
/// Every storing branch here is flat, and three of them make the point sharply: a shop writes the
/// fields its reader admits at 0.696, 0.740 and 0.910 unconditionally, a tavern writes
/// <c>EachTaleOnceOnly</c> where the reader gates it at 0.910, and a training hall writes its
/// baseclass list where the reader gates it above 0.9984.
/// </para>
/// </remarks>
public static class TownEventWriters
{
    /// <summary>
    /// How many tales a <c>TAVERN</c> writes — <b>always</b>, whatever the record holds.
    /// </summary>
    /// <remarks>
    /// <b>The count on the wire is the constant, not the list length.</b> The reference writes
    /// <c>ar &lt;&lt; MAX_TALES</c> and then loops to <c>MAX_TALES</c> (<c>GameEvent.cpp:9668</c>),
    /// so a tavern with three tales still emits 255 of them and 252 blank sentinels. Writing the
    /// list's own count instead produces a file whose tale count and tale bodies disagree — and the
    /// reference's own loading branch <c>ASSERT</c>s the count is between 10 and 255, so a small
    /// one is a shape it believes impossible.
    /// </remarks>
    public const int MaxTales = 255;

    /// <summary>Writes a <c>SOUND_EVENT</c>: a count then that many names.</summary>
    /// <remarks>
    /// Structurally identical to <c>BACKGROUND_SOUNDS</c> and a separate class — they must not
    /// share a writer, because either could gain a field.
    /// </remarks>
    public static void WriteSound(IArchiveWriteCursor ar, SoundEvent sound)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(sound);

        GameEventWriter.Write(ar, sound.Base);

        ar.WriteInt32(sound.Sounds.Count);
        foreach (string name in sound.Sounds)
        {
            GameEventWriter.WriteDas(ar, name);
        }
    }

    /// <summary>Writes a <c>GAIN_EXP_DATA</c>.</summary>
    public static void WriteGainExperience(IArchiveWriteCursor ar, GainExperienceEvent gain)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(gain);

        GameEventWriter.Write(ar, gain.Base);

        ar.WriteInt32(gain.Experience);
        GameEventWriter.WriteDas(ar, gain.Sound);
        ar.WriteInt32(gain.Chance);
        ar.WriteInt32(gain.Who);
    }

    /// <summary>Writes a <c>CAMP_EVENT_DATA</c> — the base plus one flag.</summary>
    public static void WriteCamp(IArchiveWriteCursor ar, CampEvent camp)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(camp);

        GameEventWriter.Write(ar, camp.Base);
        ar.WriteInt32(camp.ForceExit);
    }

    /// <summary>Writes a <c>REMOVE_NPC_DATA</c>.</summary>
    /// <remarks>
    /// The character reference has the pre-0.998101 numeric-key form under the same condition the
    /// control block's ids do, so <see cref="GameEventWriter.CanWrite"/> already refuses it.
    /// </remarks>
    public static void WriteRemoveNpc(IArchiveWriteCursor ar, RemoveNpcEvent remove)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(remove);

        GameEventWriter.Write(ar, remove.Base);

        ar.WriteInt32(remove.Distance);
        ar.WriteString(remove.CharacterId);          // verbatim: a CHARACTER_ID
    }

    /// <summary>Writes an <c>NPC_SAYS_DATA</c>.</summary>
    public static void WriteNpcSays(IArchiveWriteCursor ar, NpcSaysEvent says)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(says);

        GameEventWriter.Write(ar, says.Base);

        ar.WriteString(says.CharacterId);            // verbatim: a CHARACTER_ID
        ar.WriteInt32(says.Distance);
        GameEventWriter.WriteDas(ar, says.Sound);
        ar.WriteInt32(says.MustHitReturn);
        ar.WriteInt32(says.Highlight);
    }

    /// <summary>Writes a <c>TRAININGHALL</c> (<c>GameEvent.cpp:10386</c>).</summary>
    /// <remarks>
    /// <b><c>Cost</c> sits after the baseclass list and outside its version gate</b> — the
    /// "read past the closing brace" shape again, and missing it desynchronised every level with a
    /// training hall in it when the reader was written.
    /// </remarks>
    public static void WriteTrainingHall(IArchiveWriteCursor ar, TrainingHallEvent hall)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(hall);

        GameEventWriter.Write(ar, hall.Base);

        ar.WriteInt32(hall.ForceExit);

        ar.WriteInt32(hall.Trainable.Count);
        foreach (var trainable in hall.Trainable)
        {
            ar.WriteString(trainable.BaseclassId);   // verbatim: a BASECLASS_ID
            ar.WriteInt32(trainable.MinLevel);
            ar.WriteInt32(trainable.MaxLevel);
            ar.WriteString(trainable.Notes);
        }

        ar.WriteInt32(hall.Cost);
    }

    /// <summary>Writes a <c>TEMPLE</c> (<c>GameEvent.cpp:9948</c>).</summary>
    /// <remarks>
    /// <c>templeSpells</c> is declared <c>spellBookType&amp;</c> — a reference member, unusual here
    /// — but it serializes inline like any other, and <c>totalDonation</c> follows it.
    /// </remarks>
    public static void WriteTemple(IArchiveWriteCursor ar, TempleEvent temple)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(temple);

        GameEventWriter.Write(ar, temple.Base);

        ar.WriteInt32(temple.ForceExit);
        ar.WriteInt32(temple.AllowDonations);
        ar.WriteInt32(temple.CostFactor);
        ar.WriteInt32(temple.MaxLevel);
        ar.WriteInt32(temple.DonationTrigger);
        ar.WriteUInt32(temple.DonationChain);

        CharacterLeafWriters.WriteSpellBook(ar, temple.TempleSpells);

        ar.WriteInt32(temple.TotalDonation);
    }

    /// <summary>Writes a <c>SHOP</c> (<c>GameEvent.cpp:10586</c>).</summary>
    /// <remarks>
    /// Three version gates on the loading side and none on this one, and the item inventory sits
    /// outside the branch so it is written at every version.
    /// </remarks>
    public static void WriteShop(IArchiveWriteCursor ar, ShopEvent shop)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(shop);

        GameEventWriter.Write(ar, shop.Base);

        ar.WriteInt32(shop.ForceExit);
        ar.WriteInt32(shop.CostFactor);
        ar.WriteInt32(shop.CostToIdentify);
        ar.WriteInt32(shop.BuybackPercentage);
        ar.WriteInt32(shop.CanIdentify);
        ar.WriteInt32(shop.CanAppraiseGems);
        ar.WriteInt32(shop.CanAppraiseJewels);
        ar.WriteInt32(shop.BuyItemsSoldOnly);

        MonsterLeafWriters.WriteItemList(ar, shop.ItemsAvailable);
    }

    /// <summary>Writes a <c>TAVERN</c> (<c>GameEvent.cpp:9632</c>).</summary>
    /// <remarks>
    /// <b>Always <see cref="MaxTales"/> tales and <see cref="MoreEventReaders.MaxDrinks"/>
    /// drinks</b> — see <see cref="MaxTales"/> for why the tale count is a constant rather than the
    /// list length. The drinks loop sits outside the storing branch.
    /// </remarks>
    public static void WriteTavern(IArchiveWriteCursor ar, TavernEvent tavern)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(tavern);

        if (tavern.Tales.Count > MaxTales)
        {
            throw new ArgumentException(
                $"a TAVERN writes {MaxTales} tales; this one holds {tavern.Tales.Count}, which " +
                "have nowhere to go.", nameof(tavern));
        }

        if (tavern.Drinks.Count != MoreEventReaders.MaxDrinks)
        {
            throw new ArgumentException(
                $"a TAVERN writes exactly {MoreEventReaders.MaxDrinks} drinks, not " +
                $"{tavern.Drinks.Count}. The count is compile-time in the reference and never " +
                "written, so a short list truncates the event.", nameof(tavern));
        }

        GameEventWriter.Write(ar, tavern.Base);

        ar.WriteInt32(tavern.ForceExit);
        ar.WriteInt32(tavern.Inflation);
        ar.WriteInt32(tavern.Barkeep);
        ar.WriteInt32(tavern.AllowFights);
        ar.WriteInt32(tavern.AllowDrinks);
        ar.WriteUInt32(tavern.FightChain);
        ar.WriteUInt32(tavern.DrinkChain);
        ar.WriteInt32(tavern.DrinkPointTrigger);
        ar.WriteInt32(tavern.TaleOrder);
        ar.WriteInt32(tavern.EachTaleOnceOnly);

        // The constant, not tavern.Tales.Count. Unused slots go out as blank sentinels, which is
        // what the reference's default-constructed TALEs write.
        ar.WriteInt32(MaxTales);
        for (int i = 0; i < MaxTales; i++)
        {
            var tale = i < tavern.Tales.Count ? tavern.Tales[i] : new Tale(string.Empty, 0);
            GameEventWriter.WriteDas(ar, tale.Text);
            ar.WriteInt32(tale.Count);
        }

        foreach (var drink in tavern.Drinks)
        {
            GameEventWriter.WriteDas(ar, drink.Name);
            ar.WriteInt32(drink.Points);
        }
    }

    /// <summary>Writes a <c>WHO_PAYS_EVENT_DATA</c>.</summary>
    /// <remarks>
    /// The two <c>TRANSFER_DATA</c> blocks are outside the storing branch, so they are written at
    /// every version even though <c>moneyType</c> above them is gated at 0.912 on the way in.
    /// </remarks>
    public static void WriteWhoPays(IArchiveWriteCursor ar, WhoPaysEvent pays)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(pays);

        GameEventWriter.Write(ar, pays.Base);

        ar.WriteInt32(pays.Impossible);
        ar.WriteInt32(pays.Gems);
        ar.WriteInt32(pays.Jewels);
        ar.WriteInt32(pays.Platinum);
        ar.WriteUInt32(pays.SuccessChain);
        ar.WriteInt32(pays.SuccessAction);
        ar.WriteInt32(pays.FailAction);
        ar.WriteUInt32(pays.FailChain);
        ar.WriteInt32(pays.MoneyType);

        SimpleEventWriters.WriteTransferData(ar, pays.SuccessTransfer);
        SimpleEventWriters.WriteTransferData(ar, pays.FailTransfer);
    }
}
