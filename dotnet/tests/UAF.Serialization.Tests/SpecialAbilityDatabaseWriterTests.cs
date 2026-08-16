using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>specialAbilities.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// <para>
/// This is where the GPDL the <c>$RUN_*_SCRIPTS</c> family executes actually lives — a spell, item
/// or character carries only ability <i>names</i>, and the source those names stand for is here. So
/// a writer that lost an entry would not corrupt a file; it would quietly delete the design's
/// scripts.
/// </para>
/// <para>
/// <b>Not <see cref="SpecabWriterTests"/>.</b> That covers the block embedded inside a record;
/// this is the whole-file database, which has its own <c>"SpecAbVer01"</c> stamp and its own
/// framing.
/// </para>
/// </remarks>
public class SpecialAbilityDatabaseWriterTests
{
    /// <summary>Every design in the corpus that ships the binary database.</summary>
    /// <remarks>
    /// <c>DefaultDesign</c> is absent from this list on purpose: it ships
    /// <c>specialAbilities.txt</c> and no <c>.dat</c>, which the premise case pins.
    /// </remarks>
    public static TheoryData<string> Designs =>
    [
        "reference/SomethingWild.dsn",
        "reference/Case.dsn",
        "reference/ci-tier3",
    ];

    private static List<SpecialAbilityDefinition>? Read(string design)
    {
        if (DatabaseWriterCorpus.File(design, "specialAbilities.dat") is not { } path
            || DatabaseWriterCorpus.Version(design) is not { } version)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return SpecialAbilityDatabaseReader.Read(stream, version);
    }

    private static byte[] Write(IReadOnlyList<SpecialAbilityDefinition> abilities)
    {
        var stream = new MemoryStream();
        SpecialAbilityDatabaseWriter.WriteFile(stream, abilities);
        return stream.ToArray();
    }

    private static List<SpecialAbilityDefinition> ReadBack(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return SpecialAbilityDatabaseReader.Read(stream,
                                                 SpecialAbilityDatabaseWriter.WrittenVersion);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_special_ability_round_trips(string design)
    {
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        Assert.NotEmpty(abilities);

        var read = ReadBack(Write(abilities));
        Assert.Equal(abilities.Count, read.Count);

        for (int i = 0; i < abilities.Count; i++)
        {
            Assert.Equal(abilities[i].Name, read[i].Name);

            // AslEntry holds only strings and a byte, so record equality really is value equality
            // -- and the order matters: the reference walks a BTree and the port a list, so the
            // two agree only if the list is written in the order it was read.
            Assert.Equal(abilities[i].Strings, read[i].Strings);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_definition_in_a_shipped_design_is_writable(string design)
    {
        // Without this the round trip could pass by having nothing to do.
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        Assert.NotEmpty(abilities);
        Assert.All(abilities,
                   a => Assert.True(SpecialAbilityDatabaseWriter.CanWrite(a, out string reason),
                                    reason));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Saving_the_same_database_twice_produces_the_same_bytes(string design)
    {
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        byte[] first = Write(abilities);
        byte[] second = Write(ReadBack(first));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The premise: the corpus really carries special abilities, and they carry GPDL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The always-true half is unlike the other four files': <c>DefaultDesign</c> has no binary
    /// database at all, only <c>specialAbilities.txt</c>, so what a checkout with no
    /// <c>reference/</c> can assert is that the committed design is the text-only one and that a
    /// database built by hand still round-trips. The rest, when the corpus is present, pins that
    /// the scripts are there — a database that read and wrote cleanly but held no source would be
    /// exactly as useless as no database.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_corpus_really_carries_special_abilities_with_scripts_in_them()
    {
        Assert.NotNull(DatabaseWriterCorpus.RepoRoot());

        // The committed design ships the text form only, which is why it is not in Designs.
        Assert.Null(DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign,
                                              "specialAbilities.dat"));
        Assert.NotNull(DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign,
                                                 "specialAbilities.txt"));

        // So the non-vacuous claim available everywhere is that the format round-trips at all.
        var built = new SpecialAbilityDefinition(
            "monster_GiantSlug",
            [new AslEntry("DoesSpellAttackSucceed", SpecialAbilityDatabaseReader.ScriptFlag,
                          "{ $SET_RESULT(1); }")]);

        var everywhere = ReadBack(Write([built]));
        Assert.Equal(built.Name, everywhere[0].Name);
        Assert.Equal(built.Strings, everywhere[0].Strings);

        var abilities = Read("reference/Case.dsn");
        if (abilities is null)
        {
            return;
        }

        Assert.True(abilities.Count > 100, $"only {abilities.Count} abilities");

        var scripts = abilities
            .SelectMany(a => a.Strings)
            .Where(s => s.Flags == SpecialAbilityDatabaseReader.ScriptFlag)
            .ToList();

        Assert.True(scripts.Count > 800, $"only {scripts.Count} script entries");
        Assert.Contains(scripts, s => s.Value.Contains('$', StringComparison.Ordinal));
    }

    /// <summary>
    /// A shipped <c>specialAbilities.dat</c> comes back byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stronger than the round trip and the fixpoint, which compare this port against itself. It is
    /// the claim that matters most for this file, because two of its reads are <b>one-way</b>:
    /// <see cref="SpecialAbilityDatabaseReader.RepairName"/> and the compressed ASL's key fix-up
    /// both map characters below <c>0x20</c> upwards, so a database containing one would round-trip
    /// through this port as a fixpoint and still not be the file the reference wrote. Byte identity
    /// says the corpus has none — over 110KB of it in <c>SomethingWild</c> alone.
    /// </para>
    /// <para>
    /// It also pins the walk order. The reference stores these in a BTree and writes them in key
    /// order; the port keeps a list in file order. The two agree only because the file was written
    /// from that BTree, and this is what would notice if it ever were not.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Designs))]
    public void A_shipped_database_comes_back_byte_for_byte(string design)
    {
        if (DatabaseWriterCorpus.File(design, "specialAbilities.dat") is not { } path)
        {
            return;
        }

        Assert.Equal(File.ReadAllBytes(path), Write(Read(design)!));
    }

    /// <summary>
    /// The stamp is written uncompressed, ahead of everything else.
    /// </summary>
    /// <remarks>
    /// It is the only thing identifying this file — there is no magic sentinel — so compressing it
    /// with the rest would produce a file the reader rejects as not being this database. Reading
    /// the stamp with the plain archive alone is what proves it is outside the LZW stream.
    /// </remarks>
    [Fact]
    public void The_format_stamp_sits_outside_the_compressed_stream()
    {
        var stream = new MemoryStream(Write([]));
        Assert.Equal(SpecialAbilityDatabaseReader.Version,
                     new MfcArchiveReader(stream).ReadString());

        // And what follows it is the compression-type byte, in the clear.
        Assert.Equal(CarArchiveWriter.CompressType, (byte)stream.ReadByte());
    }

    /// <summary>
    /// A name the reader would silently rewrite is refused rather than written.
    /// </summary>
    /// <remarks>
    /// <c>RepairName</c> adds <c>0x20</c> to every character below it and the compressed ASL reader
    /// does the same to keys. Both are one-way, so such a name cannot be written such that it comes
    /// back unchanged — and the ability would end up under a name nothing else in the design
    /// matches.
    /// </remarks>
    [Fact]
    public void A_name_the_reader_would_repair_is_refused_with_a_reason()
    {
        var mangled = new SpecialAbilityDefinition("oison", []);

        Assert.False(SpecialAbilityDatabaseWriter.CanWrite(mangled, out string reason));
        Assert.Contains("0x20", reason, StringComparison.Ordinal);

        var keyed = new SpecialAbilityDefinition(
            "Poison", [new AslEntry("cript", SpecialAbilityDatabaseReader.ScriptFlag, "{}")]);

        Assert.False(SpecialAbilityDatabaseWriter.CanWrite(keyed, out string keyReason));
        Assert.Contains("key", keyReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every flag value survives, not only the script one.
    /// </summary>
    /// <remarks>
    /// The flags are values rather than bits — <c>SPECAB_BINARYCODE</c> is 5, which would match a
    /// mask test against <c>SPECAB_SCRIPT</c> (1). A writer that filtered by flag, as the savegame
    /// ASL path does, would drop the constants and the cached bytecode; the design path writes
    /// every entry whatever its flags.
    /// </remarks>
    [Fact]
    public void Entries_of_every_flag_are_written_not_only_the_scripts()
    {
        var ability = new SpecialAbilityDefinition("Poison",
        [
            new AslEntry("OnHit", SpecialAbilityDatabaseReader.ScriptFlag, "{ $x; }"),
            new AslEntry("Strength", SpecialAbilityDatabaseReader.ConstantFlag, "12"),
            new AslEntry("Compiled", SpecialAbilityDatabaseReader.BinaryCodeFlag, ""),
            new AslEntry("Broken", SpecialAbilityDatabaseReader.ScriptErrorFlag, "syntax error"),
        ]);

        var read = ReadBack(Write([ability]))[0];

        Assert.Equal(4, read.Strings.Count);
        Assert.Equal(ability.Strings, read.Strings);
    }
}
