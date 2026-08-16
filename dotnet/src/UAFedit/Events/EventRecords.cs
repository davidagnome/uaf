using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>
/// Operations over an <see cref="IGameEvent"/> that its interface cannot express.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IGameEvent"/> exposes <c>Base</c> as a get-only property, and every implementation is
/// a <c>sealed record</c>. So an editor can read the shared header through the interface and has no
/// way at all to write it back: <c>with { Base = … }</c> needs the concrete type at the call site.
/// </para>
/// <para>
/// <b>Hence the switch.</b> It is 37 lines of the same expression and it is deliberate — the
/// alternatives are reflection over the compiler-generated <c>&lt;Clone&gt;$</c>, which fails
/// silently on a renamed member, or an <c>IGameEvent.WithBase</c> that would mean editing
/// <c>UAF.Serialization</c>.
/// </para>
/// <para>
/// <b>Its failure mode is silence, and it has already bitten once.</b> An unlisted type falls to
/// the default and comes back unchanged, so an edit disappears rather than throwing —
/// <see cref="YesNoEvent"/> was missing from the first draft and every header edit to one of
/// Case.dsn's 102 yes/no events was quietly discarded. Nothing in the compiler catches that: the
/// switch is exhaustive over a set only a human knows. The corpus test
/// <c>Every_corpus_record_accepts_a_new_header</c> is what does, and it is the reason the omission
/// lasted an hour rather than shipping.
/// </para>
/// </remarks>
public static class EventRecords
{
    /// <summary>The same event with a different shared header.</summary>
    public static IGameEvent WithBase(IGameEvent body, GameEventBase header)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(header);

        return body switch
        {
            AddNpcEvent e => e with { Base = header },
            CampEvent e => e with { Base = header },
            ChainEvent e => e with { Base = header },
            CombatEvent e => e with { Base = header },
            DamageEvent e => e with { Base = header },
            EncounterEvent e => e with { Base = header },
            FlowControlEvent e => e with { Base = header },
            GainExperienceEvent e => e with { Base = header },
            GuidedTour e => e with { Base = header },
            HealPartyEvent e => e with { Base = header },
            JournalEvent e => e with { Base = header },
            LogicBlockEvent e => e with { Base = header },
            NpcSaysEvent e => e with { Base = header },
            PasswordEvent e => e with { Base = header },
            PassTimeEvent e => e with { Base = header },
            PlayMovieEvent e => e with { Base = header },
            QuestEvent e => e with { Base = header },
            QuestionEvent e => e with { Base = header },
            RandomEvent e => e with { Base = header },
            RemoveNpcEvent e => e with { Base = header },
            ShopEvent e => e with { Base = header },
            SmallTownEvent e => e with { Base = header },
            SoundEvent e => e with { Base = header },
            SpecialItemEvent e => e with { Base = header },
            TakePartyItemsEvent e => e with { Base = header },
            TavernEvent e => e with { Base = header },
            TavernTalesEvent e => e with { Base = header },
            TempleEvent e => e with { Base = header },
            TextEvent e => e with { Base = header },
            TrainingHallEvent e => e with { Base = header },
            TransferEvent e => e with { Base = header },
            TreasureEvent e => e with { Base = header },
            UtilitiesEvent e => e with { Base = header },
            VaultEvent e => e with { Base = header },
            WhoPaysEvent e => e with { Base = header },
            WhoTriesEvent e => e with { Base = header },
            YesNoEvent e => e with { Base = header },
            _ => body,
        };
    }

    /// <summary>
    /// The event's declared type.
    /// </summary>
    /// <remarks>
    /// <b>Not the ordinal the level's event list stored.</b> The tag appears twice on the wire —
    /// once to choose the reader and once inside <c>GameEvent::Serialize</c>
    /// (<c>EventDispatch</c>) — and only the second survives on the record. They agree in every
    /// design that loads, but this is the one the record can answer for itself, so a list built
    /// from bodies alone stays honest.
    /// </remarks>
    public static EventType TypeOf(IGameEvent body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return (EventType)body.Base.EventType;
    }
}
