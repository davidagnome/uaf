using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// The <c>GameEvent</c> base and its <c>EVENT_CONTROL</c> — the preamble shared by all ~68
/// <c>*_EVENT_DATA</c> classes.
/// </summary>
/// <remarks>
/// Events live inside level files and are reached through <c>LEVEL</c>/<c>ZONE</c>, which are not
/// ported yet, so there is no end-to-end walk here. What can be checked without one: the field
/// widths against synthetic bytes, and the structural signature the base leaves in real <c>.lvl</c>
/// files.
/// </remarks>
public class GameEventTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!;
    }

    private static byte[] Str(string s)
    {
        byte[] body = System.Text.Encoding.Latin1.GetBytes(s);
        return [(byte)body.Length, .. body];
    }

    private static byte[] I32(int v) => BitConverter.GetBytes(v);

    /// <summary>An ASL block with no entries: the map name then a WORD count of zero.</summary>
    private static byte[] EmptyAsl(string mapName) => [.. Str(mapName), 0, 0];

    [Fact]
    public void Control_block_reads_with_the_widths_the_writer_used()
    {
        // A modern design: names rather than numeric keys, and every version-gated block present.
        var version = new DesignVersion(5.28);

        byte[] data =
        [
            .. I32(0), .. I32(0), .. I32(1),          // status, result, onceOnly
            .. I32(2), .. I32(3),                     // chainTrigger, eventTrigger
            .. Str("Longsword"),                      // itemID -- a STRING at this version
            .. I32(7), .. I32(50), .. I32(1),         // quest, chance, facing
            .. Str("Human"),                          // raceID
            .. Str("fighter"),                        // classID or baseclassID -- one string either way
            .. Str("Aramil"),                         // characterID, >= 0.820
            .. EmptyAsl(AslMaps.EventControl),        // >= 0.566
            .. Str("$SomeScript"), .. I32(0),         // gpdlData + gpdlIsBinary, >= 0.880
            .. I32(4), .. I32(5),                     // partyX, partyY, >= 0.911
            .. Str("Bless"), .. I32(1), .. I32(2),    // memorized spell id/class/level
        ];

        var ms = new MemoryStream(data);
        var control = GameEventReader.ReadControl(
            ArchiveCursor.For(new MfcArchiveReader(ms)), version, ArchiveRole.Editor);

        Assert.Equal(1, control.OnceOnly);
        Assert.Equal(3, control.EventTrigger);
        Assert.Equal("Longsword", control.ItemId);
        Assert.Equal(50, control.Chance);
        Assert.Equal("Human", control.RaceId);
        Assert.Equal("fighter", control.ClassOrBaseclassId);
        Assert.Equal("Aramil", control.CharacterId);
        Assert.Equal("$SomeScript", control.GpdlData);
        Assert.Equal(5, control.PartyY);
        Assert.Equal("Bless", control.MemorizedSpellId);

        // Consumed exactly -- the assertion that actually pins the widths.
        Assert.Equal(data.Length, ms.Position);
    }

    [Fact]
    public void Legacy_designs_store_numeric_keys_where_modern_ones_store_names()
    {
        // Below VersionSpellNames the editor reads an int database key instead of a name, so the
        // same field is 4 bytes rather than a counted string. Four such fields in this block, and
        // the ASL/gpdl/party blocks are all absent at 0.565.
        var version = new DesignVersion(0.565);

        byte[] data =
        [
            .. I32(0), .. I32(0), .. I32(0), .. I32(0), .. I32(0),
            .. I32(12),                               // itemID as a key
            .. I32(0), .. I32(0), .. I32(0),          // quest, chance, facing
            .. I32(3),                                // raceID as a key
            .. I32(4),                                // class/baseclass as a key
        ];

        var ms = new MemoryStream(data);
        var control = GameEventReader.ReadControl(
            ArchiveCursor.For(new MfcArchiveReader(ms)), version, ArchiveRole.Editor);

        Assert.Equal("12", control.ItemId);
        Assert.Equal("3", control.RaceId);
        Assert.Empty(control.CharacterId);            // absent below 0.820
        Assert.Empty(control.Attributes);             // absent below 0.566
        Assert.Empty(control.GpdlData);               // absent below 0.880
        Assert.Equal(data.Length, ms.Position);
    }

    [Fact]
    public void Zero_or_negative_legacy_keys_mean_no_reference()
    {
        // GameEvent.cpp:1586 -- a key of 0 or less clears the id rather than resolving it.
        var version = new DesignVersion(0.565);
        byte[] data =
        [
            .. I32(0), .. I32(0), .. I32(0), .. I32(0), .. I32(0),
            .. I32(0),                                // itemID key of 0
            .. I32(0), .. I32(0), .. I32(0),
            .. I32(0), .. I32(0),
        ];

        var control = GameEventReader.ReadControl(
            ArchiveCursor.For(new MfcArchiveReader(new MemoryStream(data))),
            version, ArchiveRole.Editor);

        Assert.Empty(control.ItemId);
    }

    [Fact]
    public void Asl_names_do_not_all_end_in_ATTRIBUTES()
    {
        // The list was first built from a "_ATTRIBUTES" grep, which silently found a convincing
        // subset. Six names do not match that pattern, and two have no suffix at all -- so a
        // reader built from that grep would fail on every event.
        Assert.Equal("EVENT_DATA_ATTR", AslMaps.EventData);
        Assert.Equal("EVENTCONT_ATTR", AslMaps.EventControl);
        Assert.Equal("TALE", AslMaps.Tale);
        Assert.Equal("TAVTALE", AslMaps.TavernTale);

        string[] withoutSuffix = [AslMaps.Tale, AslMaps.TavernTale];
        Assert.All(withoutSuffix, n => Assert.DoesNotContain("_ATTR", n));
    }

    public static TheoryData<string, int> LevelFiles => new()
    {
        { "src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl", 2 },
        { "reference/Case.dsn/Data/Level001.lvl", 575 },
    };

    [Theory]
    [MemberData(nameof(LevelFiles))]
    public void Every_event_in_a_real_level_carries_exactly_one_control_block(
        string rel, int expectedEvents)
    {
        string path = Path.Combine(RepoRoot().FullName, rel);
        if (!File.Exists(path)) return;

        byte[] data = File.ReadAllBytes(path);

        // Structural evidence for the base's shape without a full level walk: GameEvent::Serialize
        // reads one EVENT_CONTROL (with its own ASL) and then one event ASL, so the two markers
        // must appear the same number of times. They do, in both an uncompressed 2-event level and
        // a compressed 575-event one.
        int events = Count(data, AslMaps.EventData);
        int controls = Count(data, AslMaps.EventControl);

        Assert.Equal(expectedEvents, events);
        Assert.Equal(events, controls);

        // 16 zones per level is fixed, not design data.
        Assert.Equal(16, Count(data, AslMaps.Zone));
        Assert.Equal(1, Count(data, AslMaps.Level));
    }

    private static int Count(byte[] haystack, string needleText)
    {
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(needleText);
        int count = 0;
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) count++;
        }
        return count;
    }
}

/// <summary>
/// The event-type ordinals and the dispatch table that maps them to field layouts.
/// </summary>
public class EventDispatchTests
{
    [Fact]
    public void Ordinals_are_positional_and_must_match_the_C_enum_order()
    {
        // GameEvent.h:145 assigns no explicit values below 1000, so an ordinal means nothing
        // except "index into this exact sequence". Pinning the ends and a few interior members
        // catches an accidental insertion, which would otherwise renumber every event in every
        // existing level with no error anywhere.
        Assert.Equal(0, (int)EventType.NoEvent);
        Assert.Equal(1, (int)EventType.AddNpc);
        Assert.Equal(4, (int)EventType.Combat);
        Assert.Equal(44, (int)EventType.FlowControl);   // last design-data ordinal
        Assert.Equal(40, (int)EventType.GPDLEvent);

        // The control screens start at a deliberate gap.
        Assert.Equal(1000, (int)EventType.ControlSplash);
    }

    [Fact]
    public void NoEvent_consumes_only_its_ordinal()
    {
        // CreateNewEvent returns null, and the caller then skips Serialize entirely
        // (GameEvent.cpp:3634) -- so the entry is four bytes and nothing more. Treating every
        // counted entry as a full event desynchronises on the first one of these.
        Assert.True(EventDispatch.ReadsNothing(EventType.NoEvent));
        Assert.DoesNotContain(EventType.NoEvent, EventDispatch.ClassNames.Keys);
    }

    [Fact]
    public void Several_ordinals_share_one_field_layout()
    {
        // Three transfer flavours are one class, so a reader keyed on the ordinal rather than the
        // class would need three identical branches -- or, worse, might assume three layouts.
        Assert.Equal("TRANSFER_EVENT_DATA", EventDispatch.ClassNames[EventType.Stairs]);
        Assert.Equal("TRANSFER_EVENT_DATA", EventDispatch.ClassNames[EventType.Teleporter]);
        Assert.Equal("TRANSFER_EVENT_DATA", EventDispatch.ClassNames[EventType.TransferModule]);

        // PickOneCombat is obsolete but still readable, as a COMBAT_EVENT_DATA.
        Assert.Equal("COMBAT_EVENT_DATA", EventDispatch.ClassNames[EventType.PickOneCombat]);
        Assert.Equal("COMBAT_EVENT_DATA", EventDispatch.ClassNames[EventType.Combat]);

        // 43 ordinals map to fewer distinct layouts.
        Assert.True(EventDispatch.ClassNames.Values.Distinct().Count()
                    < EventDispatch.ClassNames.Count);
    }

    [Fact]
    public void Obsolete_ordinals_still_have_layouts_because_old_levels_contain_them()
    {
        // InnEvent is the one obsolete ordinal with NO class -- it reads nothing, like NoEvent.
        // The others were superseded but must still be readable.
        Assert.True(EventDispatch.ReadsNothing(EventType.InnEvent));
        Assert.False(EventDispatch.ReadsNothing(EventType.TavernTales));
        Assert.False(EventDispatch.ReadsNothing(EventType.PickOneCombat));
    }
}
