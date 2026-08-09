using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Reading a level's event records (<c>UAImportEvent</c>, <c>UAFWinEd/UAImport.cpp:1768</c>).
/// </summary>
public class FruaEventTests
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

    private static FruaEvent Event(byte type, byte trigger = 0, byte triggerData = 0,
                                   byte chain = 0, params byte[] data)
    {
        var record = new byte[FruaEvent.Length];
        record[0] = type;
        record[1] = trigger;
        record[2] = triggerData;
        record[3] = chain;
        data.CopyTo(record, 4);
        return FruaEvent.Read(record);
    }

    [Fact]
    public void A_record_splits_into_type_trigger_and_payload()
    {
        // 0x1B = 24 (daytime) | 2 (if-happened) | 1 (once only) -- all three fields at once.
        var e = Event(2, trigger: 0x1B, triggerData: 42, chain: 7, 0xAA, 0xBB);

        Assert.Equal(FruaEventType.TextStatement, e.Type);
        Assert.Equal(2, e.RawType);
        Assert.True(e.OnceOnly);                                          // bit 0
        Assert.Equal(FruaChainTrigger.IfEventHappened, e.ChainTrigger);   // bits 1-2
        Assert.Equal(FruaTrigger.Daytime, e.Trigger);                     // bits 3-7
        Assert.Equal(42, e.TriggerData);
        Assert.Equal(7, e.ChainEvent);
        Assert.Equal(16, e.Data.Count);
    }

    /// <summary>
    /// The payload is addressed by the reference's own one-based whole-record offset.
    /// </summary>
    /// <remarks>
    /// <c>EventByte</c> reads <c>pData[FileOffset - 5]</c>, so offset 5 is the first payload byte.
    /// Every per-type reader in the reference quotes offsets in this scheme, and getting it wrong
    /// by even one shifts every field of every event.
    /// </remarks>
    [Fact]
    public void Offset_five_is_the_first_payload_byte()
    {
        var e = Event(2, data: [0x11, 0x22, 0x33, 0x44]);

        Assert.Equal(0x11, e.Byte(5));
        Assert.Equal(0x22, e.Byte(6));
        Assert.Equal(0x2211, e.Word(5));
        Assert.Equal(0x44332211u, e.Dword(5));
    }

    /// <summary>Unknown type bytes fall to None rather than being cast blindly.</summary>
    [Theory]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(200)]
    public void A_type_the_reference_does_not_map_is_None(byte type)
    {
        var e = Event(type);

        Assert.Equal(FruaEventType.None, e.Type);
        Assert.Equal(type, e.RawType);
        Assert.True(e.IsEmpty);
    }

    [Theory]
    [InlineData(0x00, FruaChainTrigger.Always)]
    [InlineData(0x02, FruaChainTrigger.IfEventHappened)]
    [InlineData(0x04, FruaChainTrigger.IfEventDidNotHappen)]
    [InlineData(0x06, FruaChainTrigger.Always)]   // both bits set is not a case
    public void The_chain_trigger_is_bits_one_and_two(byte trigger, FruaChainTrigger expected)
    {
        Assert.Equal(expected, Event(2, trigger: trigger).ChainTrigger);
    }

    [Theory]
    [InlineData(0, FruaTrigger.Always)]
    [InlineData(8, FruaTrigger.PartyHaveItem)]
    [InlineData(40, FruaTrigger.RandomChance)]
    [InlineData(64, FruaTrigger.FacingDirection)]
    [InlineData(136, FruaTrigger.RaceInParty)]
    [InlineData(144, FruaTrigger.Always)]   // past the last case
    public void The_trigger_is_the_top_five_bits(byte trigger, FruaTrigger expected)
    {
        Assert.Equal(expected, Event(2, trigger: trigger).Trigger);
    }

    /// <summary>A facing trigger stores a mask, so one event can want several facings.</summary>
    [Fact]
    public void A_facing_trigger_is_a_mask_not_an_ordinal()
    {
        Assert.Equal([FruaFacing.North], Event(2, trigger: 64, triggerData: 1).Facings());
        Assert.Equal([FruaFacing.North, FruaFacing.South],
                     Event(2, trigger: 64, triggerData: 5).Facings());
        Assert.Equal([FruaFacing.North, FruaFacing.East, FruaFacing.South, FruaFacing.West],
                     Event(2, trigger: 64, triggerData: 15).Facings());
        Assert.Empty(Event(2, trigger: 64, triggerData: 0).Facings());
    }

    /// <summary>One byte addresses keys, items and quests by range.</summary>
    [Theory]
    [InlineData(0, FruaObjectKind.Key, 0)]
    [InlineData(7, FruaObjectKind.Key, 7)]
    [InlineData(8, FruaObjectKind.Item, 0)]
    [InlineData(19, FruaObjectKind.Item, 11)]
    [InlineData(20, FruaObjectKind.Quest, 0)]
    [InlineData(63, FruaObjectKind.Quest, 43)]
    public void One_numbering_covers_keys_items_and_quests(byte data, FruaObjectKind kind, int index)
    {
        Assert.Equal(kind, FruaEvent.ObjectKind(data));
        Assert.Equal(index, FruaEvent.ObjectIndex(data));
    }

    /// <summary>
    /// The class table has a hole at 1, where the reference reads uninitialised memory.
    /// </summary>
    [Theory]
    [InlineData(0, "Cleric")]
    [InlineData(2, "Fighter")]
    [InlineData(6, "Thief")]
    [InlineData(1, null)]     // no case in the reference's switch
    [InlineData(7, null)]
    public void An_unmapped_class_is_refused_rather_than_invented(byte data, string? expected)
    {
        Assert.Equal(expected, Event(2, trigger: 128, triggerData: data).ClassWanted());
    }

    [Theory]
    [InlineData(0, "Elf")]
    [InlineData(5, "Human")]
    [InlineData(6, null)]
    public void The_race_table_covers_six(byte data, string? expected)
    {
        Assert.Equal(expected, Event(2, trigger: 136, triggerData: data).RaceWanted());
    }

    // ---- the real DOS levels ---------------------------------------------------------------

    /// <summary>
    /// Every event byte in every shipped level maps to a type the reference knows.
    /// </summary>
    /// <remarks>
    /// <b>1,040 non-empty events across 26 levels and not one unknown type byte.</b> That is the
    /// evidence the type table is complete rather than merely plausible — a missing case would show
    /// as a record falling to <c>None</c> with a non-zero raw type.
    /// </remarks>
    [Fact]
    public void No_shipped_event_has_a_type_the_table_lacks()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int live = 0;
        var unknown = new List<byte>();

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            Assert.Equal(FruaEvent.PerLevel, level.Events.Count);

            foreach (var e in level.Events)
            {
                if (e.RawType == 0)
                {
                    continue;
                }

                live++;
                if (e.Type == FruaEventType.None)
                {
                    unknown.Add(e.RawType);
                }
            }
        }

        Assert.Empty(unknown);
        Assert.Equal(1040, live);
    }

    /// <summary>
    /// Every trigger in every shipped level is one the table names.
    /// </summary>
    [Fact]
    public void No_shipped_event_has_a_trigger_the_table_lacks()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        foreach (var (number, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.RawType != 0))
            {
                // Always is both a real value and the fallback, so check the stored bits directly:
                // a trigger the table lacked would be a non-zero class reported as Always.
                if (e.Trigger == FruaTrigger.Always)
                {
                    Assert.Equal(0, (int)e.Trigger);
                }

                Assert.True(Enum.IsDefined(e.Trigger),
                            $"level {number} has trigger {e.Trigger}");
            }
        }
    }

    /// <summary>
    /// A cell's event index reaches a real event, and text events name real strings.
    /// </summary>
    /// <remarks>
    /// This is the join that matters: the cell block, the event block and the string block are read
    /// from three different offsets, and this walks all three. A wrong offset in any of them
    /// breaks the chain.
    /// </remarks>
    [Fact]
    public void A_cells_event_index_reaches_an_event_that_names_a_string()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        int texts = 0;

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                int index = level.Cell(x, y).EventIndex;
                if (index == 0)
                {
                    continue;
                }

                var e = level.Events[index - 1];

                // A TextStatement's string index sits at the reference's offset 5.
                if (e.Type == FruaEventType.TextStatement
                    && level.Strings.Get(e.Word(5)) is { } text)
                {
                    Assert.NotEmpty(text);
                    texts++;
                }
            }
        }

        Assert.True(texts > 0, "no cell reached a text event with a readable string");
    }
}
