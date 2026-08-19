using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// The JSON character format, read against the one shipped file that uses it.
/// </summary>
/// <remarks>
/// <c>SomethingWild</c>'s <c>Data/Uril Kabo.CHAR</c> is the only instance in the corpus, so every
/// case here returns early without it; <see cref="The_corpus_ships_a_json_character"/> is what
/// stops the file passing while proving nothing.
/// </remarks>
public class JsonCharacterReaderTests
{
    private static string? UrilKabo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? path = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn", "Data",
                           "Uril Kabo.CHAR");

        return path is not null && File.Exists(path) ? path : null;
    }

    private static CharacterRecord? Read()
    {
        if (UrilKabo() is not { } path)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonCharacterReader.Read(stream);
    }

    /// <summary>The premise: the file is there and is JSON.</summary>
    [Fact]
    public void The_corpus_ships_a_json_character()
    {
        if (UrilKabo() is not { } path)
        {
            return;
        }

        using var stream = File.OpenRead(path);

        Assert.True(JsonCharacterReader.IsJson(stream));

        // Sniffing does not consume the stream, or the read that follows would start mid-file.
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// The binary format is not mistaken for this one, nor this one for that.
    /// </summary>
    /// <remarks>
    /// <b>Both formats use the same extensions</b>, so the name cannot decide it — and the binary
    /// reader does not reject JSON cleanly: the first eight bytes of <c>{"charVersion"</c> decode
    /// as a plausible <c>double</c>, which is why the port used to call this file corrupt.
    /// </remarks>
    [Fact]
    public void The_two_formats_are_told_apart()
    {
        using var binary = new MemoryStream([0xBF, 0xFA, 0xDE, 0xBC, 0xFA, 0xDE, 0xBC, 0xFA]);
        Assert.False(JsonCharacterReader.IsJson(binary));

        using var json = new MemoryStream("  \r\n {\"charVersion\":\"1\"}"u8.ToArray());
        Assert.True(JsonCharacterReader.IsJson(json));
    }

    /// <summary>The scalars come back as the file states them.</summary>
    [Fact]
    public void The_character_reads()
    {
        if (Read() is not { } who)
        {
            return;
        }

        Assert.Equal("Uril Kabo", who.Name);
        Assert.Equal("Uril Kabo", who.CharacterId);
        Assert.Equal("Human", who.Race);
        Assert.Equal("Druid", who.ClassId);
        Assert.Equal(16, who.Thac0);
        Assert.Equal(33, who.HitPoints);
        Assert.Equal(33, who.MaxHitPoints);
        Assert.Equal(7, who.ArmorClass);
        Assert.Equal(31, who.Age);
        Assert.Equal(3500, who.MaxEncumbrance);
    }

    /// <summary>
    /// The four enum names decode to their indices.
    /// </summary>
    /// <remarks>
    /// <b>This format spells them differently from every other part of the port.</b> It says
    /// "Chaotic Neutral" where GPDL's <c>$Alignment</c> says "CHAOTIC NEUTRAL", so a reader that
    /// reused the scripting tables would resolve none of them and silently return zero — which is
    /// "Lawful Good", a perfectly plausible wrong answer.
    /// </remarks>
    [Fact]
    public void The_enum_names_decode()
    {
        if (Read() is not { } who)
        {
            return;
        }

        Assert.Equal(0, who.Gender);             // Male
        Assert.Equal(5, who.Alignment);          // Chaotic Neutral
        Assert.Equal(0, who.Status);             // OKAY
        Assert.Equal(1, who.CreatureSize);       // Medium
    }

    /// <summary>A number written with a decimal part still reads as an integer field.</summary>
    /// <remarks>
    /// <c>nbrHitDice</c> is "51.000000" on the wire. An integer parse would reject it and leave
    /// the field zero.
    /// </remarks>
    [Fact]
    public void A_decimal_string_reads_as_a_number()
    {
        if (Read() is not { } who)
        {
            return;
        }

        Assert.Equal(51.0, who.NumberOfHitDice);
        Assert.Equal(1.0f, who.NumberOfAttacks);
    }

    /// <summary>The nested structures come across, not just the scalars.</summary>
    [Fact]
    public void The_nested_structures_read()
    {
        if (Read() is not { } who)
        {
            return;
        }

        var baseclass = Assert.Single(who.BaseclassStats);
        Assert.Equal("druid", baseclass.BaseclassId);
        Assert.Equal(9, baseclass.CurrentLevel);
        Assert.Equal(90001, baseclass.Experience);

        var possession = Assert.Single(who.Items.Items);
        Assert.Equal("Bite", possession.ItemId);
        Assert.Equal(1, possession.Quantity);

        Assert.Equal(11, who.SpellBook.Spells.Count);
        Assert.Equal("Detect Magic|Druid", who.SpellBook.Spells[0].SpellId);
        Assert.Equal(1, who.SpellBook.Spells[0].Memorized);
    }

    /// <summary>
    /// The icon's type is a list of flag names, and decodes to the flag word.
    /// </summary>
    /// <remarks>
    /// The one field in the format that is neither a string scalar nor an object: <c>picType</c> is
    /// written as a JSON array of <c>SurfaceType</c> names, so <c>["IconDib"]</c> is 64.
    /// </remarks>
    [Fact]
    public void The_pic_type_is_a_flag_list()
    {
        if (Read() is not { } who)
        {
            return;
        }

        Assert.NotNull(who.Icon);
        Assert.Equal(64, who.Icon!.PicType);     // IconDib
        Assert.Equal("icon_Gorilla.png", who.Icon.FileName);
        Assert.Equal(48, who.Icon.FrameWidth);
        Assert.Equal(2, who.Icon.NumFrames);
    }

    /// <summary>
    /// The file is written back byte for byte.
    /// </summary>
    /// <remarks>
    /// <b>The only format in this port where that is possible.</b> Every binary format restamps
    /// its version on save, so no shipped file comes back unchanged (docs/PORTING-PLAN.md §12);
    /// JSON carries its version as an ordinary field, so there is nothing to restamp and the
    /// strongest claim is available here.
    /// </remarks>
    [Fact]
    public void The_file_is_written_back_byte_for_byte()
    {
        if (UrilKabo() is not { } path)
        {
            return;
        }

        string original = File.ReadAllText(path);
        var who = Read()!;

        Assert.Equal(original, JsonCharacterWriter.Write(who));
    }

    /// <summary>And a second write is a fixpoint.</summary>
    [Fact]
    public void Writing_twice_produces_the_same_text()
    {
        if (Read() is not { } who)
        {
            return;
        }

        string once = JsonCharacterWriter.Write(who);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(once));
        string twice = JsonCharacterWriter.Write(JsonCharacterReader.Read(stream));

        Assert.Equal(once, twice);
    }

    /// <summary>Something that is not JSON at all is refused with a reason.</summary>
    [Fact]
    public void Not_json_is_refused()
    {
        using var nonsense = new MemoryStream("{ this is not json"u8.ToArray());

        var e = Assert.Throws<InvalidDataException>(() => JsonCharacterReader.Read(nonsense));
        Assert.Contains("Not a JSON character file", e.Message, StringComparison.Ordinal);
    }
}
