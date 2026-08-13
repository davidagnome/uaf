using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Turning DOS FRUA events into the engine's own event records.
/// </summary>
public class FruaEventConverterTests
{
    private static string? Heirs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    private static IReadOnlyList<FruaLevel>? Levels() =>
        Heirs() is { } design ? FruaLevel.ReadAll(design).Values.ToList() : null;

    /// <summary>
    /// FRUA's triggers map onto the engine's, and the last one is not where arithmetic puts it.
    /// </summary>
    /// <remarks>
    /// Every trigger below 136 is its FRUA value divided by eight. <b>136 is not 17.</b> The engine
    /// interleaves <c>ClassNotInParty</c> at 17, which FRUA cannot express, so <c>RaceInParty</c>
    /// is 18 — the one case a divide-by-eight shortcut gets wrong.
    /// </remarks>
    [Theory]
    [InlineData(FruaTrigger.Always, 0)]
    [InlineData(FruaTrigger.PartyHaveItem, 1)]
    [InlineData(FruaTrigger.PartyNotHaveItem, 2)]
    [InlineData(FruaTrigger.Daytime, 3)]
    [InlineData(FruaTrigger.Nighttime, 4)]
    [InlineData(FruaTrigger.RandomChance, 5)]
    [InlineData(FruaTrigger.PartySearching, 6)]
    [InlineData(FruaTrigger.PartyNotSearching, 7)]
    [InlineData(FruaTrigger.FacingDirection, 8)]
    [InlineData(FruaTrigger.QuestComplete, 9)]
    [InlineData(FruaTrigger.QuestFailed, 10)]
    [InlineData(FruaTrigger.QuestInProgress, 11)]
    [InlineData(FruaTrigger.PartyDetectingTraps, 12)]
    [InlineData(FruaTrigger.PartyNotDetectingTraps, 13)]
    [InlineData(FruaTrigger.PartySeeInvisible, 14)]
    [InlineData(FruaTrigger.PartyNotSeeInvisible, 15)]
    [InlineData(FruaTrigger.ClassInParty, 16)]
    [InlineData(FruaTrigger.RaceInParty, 18)]
    public void Each_trigger_maps_to_its_engine_ordinal(FruaTrigger trigger, int expected) =>
        Assert.Equal(expected, FruaEventControlConverter.TriggerOrdinal(trigger));

    /// <summary>The divide-by-eight shortcut is right everywhere except the last entry.</summary>
    [Fact]
    public void The_arithmetic_shortcut_holds_until_race_in_party()
    {
        foreach (FruaTrigger trigger in Enum.GetValues<FruaTrigger>())
        {
            int shortcut = (int)trigger / 8;
            int actual = FruaEventControlConverter.TriggerOrdinal(trigger);

            if (trigger == FruaTrigger.RaceInParty)
            {
                Assert.NotEqual(shortcut, actual);
                Assert.Equal(shortcut + 1, actual);
            }
            else
            {
                Assert.Equal(shortcut, actual);
            }
        }
    }

    /// <summary>The trigger's data byte reaches the field its trigger decides.</summary>
    [Fact]
    public void The_trigger_data_lands_in_the_right_field()
    {
        var chance = FruaEventControlConverter.Control(
            Synthetic() with { Trigger = FruaTrigger.RandomChance, TriggerData = 40 });
        Assert.Equal(40, chance.Chance);
        Assert.Equal(0, chance.Facing);

        var facing = FruaEventControlConverter.Control(
            Synthetic() with { Trigger = FruaTrigger.FacingDirection, TriggerData = 5 });
        Assert.Equal(5, facing.Facing);
        Assert.Equal(0, facing.Chance);

        var race = FruaEventControlConverter.Control(
            Synthetic() with { Trigger = FruaTrigger.RaceInParty, TriggerData = 2 });
        Assert.Equal("Dwarf", race.RaceId);
        Assert.Equal(string.Empty, race.ClassOrBaseclassId);

        var cls = FruaEventControlConverter.Control(
            Synthetic() with { Trigger = FruaTrigger.ClassInParty, TriggerData = 5 });
        Assert.Equal("Magic User", cls.ClassOrBaseclassId);
        Assert.Equal(string.Empty, cls.RaceId);

        // Quest indices are numbers rather than names, and start past the keys and items.
        var quest = FruaEventControlConverter.Control(
            Synthetic() with { Trigger = FruaTrigger.QuestComplete, TriggerData = 25 });
        Assert.Equal(5, quest.Quest);
    }

    /// <summary>
    /// FRUA class 1 has no engine equivalent and yields nothing rather than a stale value.
    /// </summary>
    [Fact]
    public void The_missing_class_case_yields_empty() =>
        Assert.Equal(string.Empty, FruaEventControlConverter.TriggerClassName(1));

    /// <summary>
    /// The chain trigger decides which of the engine's two chain slots the target lands in.
    /// </summary>
    [Fact]
    public void The_chain_target_follows_the_chain_trigger()
    {
        var always = FruaEventControlConverter.Base(
            Synthetic() with { ChainTrigger = FruaChainTrigger.Always, ChainEvent = 7 }, 0, 1);
        Assert.Equal(7, always.ChainEventHappen);
        Assert.Equal(7, always.ChainEventNotHappen);

        var happened = FruaEventControlConverter.Base(
            Synthetic() with { ChainTrigger = FruaChainTrigger.IfEventHappened, ChainEvent = 7 },
            0, 1);
        Assert.Equal(7, happened.ChainEventHappen);
        Assert.Equal(0, happened.ChainEventNotHappen);

        var not = FruaEventControlConverter.Base(
            Synthetic() with
            {
                ChainTrigger = FruaChainTrigger.IfEventDidNotHappen,
                ChainEvent = 7,
            },
            0, 1);
        Assert.Equal(0, not.ChainEventHappen);
        Assert.Equal(7, not.ChainEventNotHappen);
    }

    /// <summary>Once-only survives as the engine's flag.</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Once_only_survives(bool onceOnly, int expected) =>
        Assert.Equal(expected,
                     FruaEventControlConverter.Control(
                         Synthetic() with { OnceOnly = onceOnly }).OnceOnly);

    /// <summary>
    /// <c>Converts</c> and <c>Convert</c> agree — neither can drift from the other.
    /// </summary>
    /// <remarks>
    /// A type claimed as converted that returns null, or one converted without being claimed,
    /// would make coverage reporting a lie. This is what keeps the two honest.
    /// </remarks>
    [Fact]
    public void The_claimed_coverage_matches_what_converts()
    {
        foreach (FruaEventType type in Enum.GetValues<FruaEventType>())
        {
            if (type == FruaEventType.None)
            {
                continue;
            }

            var converted = FruaEventConverter.Convert(Synthetic() with { Type = type }, 1);

            Assert.Equal(FruaEventConverter.Converts.Contains(type), converted is not null);
        }
    }

    /// <summary>Each converted type carries the engine's own event-type ordinal.</summary>
    [Theory]
    [InlineData(FruaEventType.TextStatement, EventType.TextStatement)]
    [InlineData(FruaEventType.GiveTreasure, EventType.GiveTreasure)]
    [InlineData(FruaEventType.CombatTreasure, EventType.CombatTreasure)]
    [InlineData(FruaEventType.Damage, EventType.Damage)]
    [InlineData(FruaEventType.Sounds, EventType.Sounds)]
    [InlineData(FruaEventType.QuestStage, EventType.QuestStage)]
    [InlineData(FruaEventType.GainExperience, EventType.GainExperience)]
    [InlineData(FruaEventType.QuestionYesNo, EventType.QuestionYesNo)]
    [InlineData(FruaEventType.Vault, EventType.Vault)]
    [InlineData(FruaEventType.PassTime, EventType.PassTime)]
    [InlineData(FruaEventType.ChainEvent, EventType.ChainEventType)]
    [InlineData(FruaEventType.Camp, EventType.Camp)]
    [InlineData(FruaEventType.Stairs, EventType.Stairs)]
    [InlineData(FruaEventType.Teleporter, EventType.Teleporter)]
    [InlineData(FruaEventType.TransferModule, EventType.TransferModule)]
    public void The_engine_event_type_is_carried(FruaEventType from, EventType to)
    {
        var converted = FruaEventConverter.Convert(Synthetic() with { Type = from }, 1);

        Assert.NotNull(converted);
        Assert.Equal((int)to, BaseOf(converted).EventType);
    }

    /// <summary>
    /// The three transfer types share one payload and differ only in ordinal.
    /// </summary>
    [Fact]
    public void The_three_transfer_types_share_a_payload()
    {
        var types = new[]
        {
            FruaEventType.Stairs, FruaEventType.Teleporter, FruaEventType.TransferModule,
        };

        var destinations = types
            .Select(t => Assert.IsType<TransferEvent>(
                FruaEventConverter.Convert(Synthetic() with { Type = t }, 1)).Destination)
            .ToList();

        Assert.All(destinations, d => Assert.Equal(destinations[0], d));
    }

    /// <summary>Real text events keep their text.</summary>
    [Fact]
    public void A_real_text_event_keeps_its_text()
    {
        if (Levels() is not { } levels)
        {
            return;
        }

        int withText = 0;

        foreach (var level in levels)
        {
            foreach (var source in level.Events.Where(e => e.Type == FruaEventType.TextStatement))
            {
                var converted = FruaEventConverter.Convert(source, 1, level.Strings);
                var text = Assert.IsType<TextEvent>(converted);

                if (!string.IsNullOrEmpty(text.Base.Text))
                {
                    withText++;
                }
            }
        }

        Assert.True(withText > 100, $"only {withText} converted text events carry text");
    }

    /// <summary>
    /// Converting without a string table produces the same structure, only without text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table is the one input a caller may not have; this pins that its absence costs the
    /// strings and nothing else.
    /// </para>
    /// <para>
    /// <b>The highlight markers survive a missing table</b>, because they come from the flags byte
    /// rather than from the text — a highlighted slot that resolves to nothing still produces
    /// <c>/h/h</c>. That is the reference's behaviour too: it appends the opening marker before it
    /// looks at the chunk. So what an absent table leaves is markup with nothing between.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_missing_string_table_costs_text_and_nothing_else()
    {
        if (Levels() is not { } levels)
        {
            return;
        }

        int compared = 0;

        foreach (var level in levels)
        {
            foreach (var source in level.Events.Where(e => e.Type == FruaEventType.TextStatement))
            {
                var with = Assert.IsType<TextEvent>(
                    FruaEventConverter.Convert(source, 1, level.Strings));
                var without = Assert.IsType<TextEvent>(FruaEventConverter.Convert(source, 1));

                Assert.Equal(
                    string.Empty,
                    without.Base.Text.Replace(FruaTextEvent.HighlightMarker, string.Empty,
                                              StringComparison.Ordinal));

                Assert.Equal(with.WaitForReturn, without.WaitForReturn);
                Assert.Equal(with.ForceBackup, without.ForceBackup);
                Assert.Equal(with.Base.Control, without.Base.Control);
                compared++;
            }
        }

        Assert.True(compared > 0, "no text events to compare");
    }

    /// <summary>
    /// Every event in the design either converts or is a type not yet claimed.
    /// </summary>
    /// <remarks>
    /// <b>This is the coverage measurement, not a pass/fail on completeness.</b> What it asserts is
    /// that nothing converts unexpectedly and nothing claimed fails — the share converted is
    /// reported through the failure message when the claim and the outcome disagree.
    /// </remarks>
    [Fact]
    public void Every_shipped_event_converts_or_is_unclaimed()
    {
        if (Levels() is not { } levels)
        {
            return;
        }

        int converted = 0;
        int unclaimed = 0;

        foreach (var level in levels)
        {
            foreach (var source in level.Events)
            {
                if (source.Type == FruaEventType.None)
                {
                    continue;
                }

                var result = FruaEventConverter.Convert(source, 1, level.Strings);

                if (result is null)
                {
                    Assert.DoesNotContain(source.Type, FruaEventConverter.Converts);
                    unclaimed++;
                }
                else
                {
                    Assert.Contains(source.Type, FruaEventConverter.Converts);
                    converted++;
                }
            }
        }

        // 745 of the design's 1,040 events, measured. The floor guards against a regression
        // silently dropping a whole type; raise it as more types are mapped.
        Assert.True(converted >= 745,
                    $"coverage fell: {converted} converted, {unclaimed} unclaimed");
        Assert.True(converted + unclaimed > 500,
                    $"only {converted + unclaimed} events seen across the design");
    }

    private static GameEventBase BaseOf(IGameEvent e) => e switch
    {
        TextEvent t => t.Base,
        TreasureEvent t => t.Base,
        DamageEvent d => d.Base,
        SoundEvent s => s.Base,
        QuestEvent q => q.Base,
        GainExperienceEvent g => g.Base,
        YesNoEvent y => y.Base,
        VaultEvent v => v.Base,
        PassTimeEvent p => p.Base,
        ChainEvent c => c.Base,
        CampEvent c => c.Base,
        TransferEvent t => t.Base,
        _ => throw new InvalidOperationException($"no base known for {e.GetType().Name}"),
    };

    /// <summary>An event with a zeroed payload, for the fields no shipped event exercises.</summary>
    private static FruaEvent Synthetic() =>
        new(Type: FruaEventType.TextStatement,
            RawType: 2,
            OnceOnly: false,
            ChainTrigger: FruaChainTrigger.Always,
            Trigger: FruaTrigger.Always,
            TriggerData: 0,
            ChainEvent: 0,
            Data: new byte[16]);
}
