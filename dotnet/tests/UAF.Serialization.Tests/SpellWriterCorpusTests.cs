using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>spells.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// The third record type the port can write, and the largest. Nothing follows the records — unlike
/// <c>items.dat</c>, whose ammo-type list sits after them.
/// </remarks>
public class SpellWriterCorpusTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    private static string? PathTo(string relativeDataDir)
    {
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, Path.Combine(relativeDataDir.Split('/')), "spells.dat");
        return path is not null && File.Exists(path) ? path : null;
    }

    private static List<SpellRecord>? Read(string relativeDataDir)
    {
        if (PathTo(relativeDataDir) is not { } path)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        stream.Seek(header.PayloadOffset, SeekOrigin.Begin);

        // Editor, as SpellWalkTests reads them: it is the role that takes the legacy class-mask
        // branch, and every design here is past it anyway.
        return header.Tier == ArchiveTier.CompressedCar
            ? SpellRecordReader.ReadDatabase(CarArchiveReader.Open(stream), header.Version,
                                             ArchiveRole.Editor)
            : SpellRecordReader.ReadDatabase(new MfcArchiveReader(stream), header.Version,
                                             ArchiveRole.Editor);
    }

    private static byte[] WriteCompressed(IReadOnlyList<SpellRecord> spells)
    {
        var stream = new MemoryStream();
        using (var car = CarArchiveWriter.Open(stream))
        {
            SpellRecordWriter.WriteDatabase(ArchiveWriteCursor.For(car), spells);
        }

        return stream.ToArray();
    }

    private static byte[]? CompressedPayload(string relativeDataDir)
    {
        if (PathTo(relativeDataDir) is not { } path)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        if (header.Tier != ArchiveTier.CompressedCar)
        {
            return null;
        }

        stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var payload = new MemoryStream();
        stream.CopyTo(payload);
        return payload.ToArray();
    }

    public static TheoryData<string> Designs =>
    [
        "reference/SomethingWild.dsn/Data",
        "reference/Case.dsn/Data",
        "reference/ci-tier3/Data",
        "reference/dc-default/data-files",
    ];

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_spell_round_trips(string dataDir)
    {
        var spells = Read(dataDir);
        if (spells is null)
        {
            return;
        }

        Assert.NotEmpty(spells);

        var stream = new MemoryStream(WriteCompressed(spells));
        var read = SpellRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                  SpellRecordWriter.WrittenVersion,
                                                  ArchiveRole.Editor);

        Assert.Equal(spells.Count, read.Count);
        for (int i = 0; i < spells.Count; i++)
        {
            AssertSameSpell(spells[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_record_in_a_modern_design_is_writable(string dataDir)
    {
        // Without this the round trip could pass by having nothing to do.
        var spells = Read(dataDir);
        if (spells is null)
        {
            return;
        }

        Assert.All(spells,
                   s => Assert.True(SpellRecordWriter.CanWrite(s, out string reason), reason));
    }

    [Fact]
    public void A_design_at_the_written_version_comes_back_byte_for_byte()
    {
        // The same claim as for monsters and items, and the same condition: ci-tier3 is 5.29, so
        // nothing is added on the way out.
        byte[]? shipped = CompressedPayload("reference/ci-tier3/Data");
        var spells = Read("reference/ci-tier3/Data");
        if (shipped is null || spells is null)
        {
            return;
        }

        Assert.Equal(shipped, WriteCompressed(spells));
    }

    [Fact]
    public void The_compiled_script_binaries_really_are_empty_in_shipped_designs()
    {
        // SpellScript keeps the binary so the writer has something to put back, and this is what
        // makes that a distinction without a difference in practice: the reference empties every
        // one of them as it loads, so a file it wrote holds fourteen blanks per record. If this
        // ever fails, keeping the field is doing real work rather than merely being tidy.
        var spells = Read("reference/dc-default/data-files");
        if (spells is null)
        {
            return;
        }

        Assert.All(spells.SelectMany(s => s.Scripts), s => Assert.Equal(string.Empty, s.Binary));
    }

    [Fact]
    public void A_spell_carries_sources_worth_writing_back()
    {
        // The guard beside the assertion above: all-empty binaries would also be produced by a
        // reader that had lost its place, so something in the pairs has to be non-empty.
        var spells = Read("reference/dc-default/data-files");
        if (spells is null)
        {
            return;
        }

        Assert.Contains(spells.SelectMany(s => s.Scripts), s => s.Source.Length > 0);
    }

    [Fact]
    public void The_slots_a_design_predates_are_written_as_blanks_not_dropped()
    {
        // DefaultDesign is 0.915: past the 0.904 that brought SpellBegin/SpellEnd, but below both
        // the 1.0303 saving-throw group and the 2.6 initiation pair. So five of its seven slots are
        // empty on the way in and have to go out as blanks -- which is what the reference's own
        // default-constructed members write.
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data",
                           "spells.dat");
        if (path is null || !File.Exists(path))
        {
            return;
        }

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        var spells = SpellRecordReader.ReadDatabase(new MfcArchiveReader(fs), header.Version,
                                                    ArchiveRole.Editor);

        Assert.True(header.Version < new DesignVersion(1.0303));
        Assert.All(spells, s => Assert.Equal(SpellRecordReader.SpellScriptCount, s.Scripts.Count));

        // As it stands the whole design is refused -- but for the specab shape and nothing else,
        // which is the same reason DefaultDesign's 44 monsters are 0 of 44 writable. Give each
        // record a modern block and the rest of it goes out, which is the point worth pinning:
        // the pre-0.998101 class masks are NOT a refusal, because the reader already expanded them
        // into a school name and baseclass names. An old item is unwritable for the mirror-image
        // reason -- its Usable_by_Class bitmask needs baseclass.dat to convert.
        Assert.All(spells, s => Assert.False(SpellRecordWriter.CanWrite(s, out _)));

        var modern = spells
            .Select(s => s with { SpecialAbilities = new SpecabBlock([], [], []) })
            .ToList();
        Assert.All(modern,
                   s => Assert.True(SpellRecordWriter.CanWrite(s, out string reason), reason));

        var written = new MemoryStream(WriteCompressed(modern));
        var read = SpellRecordReader.ReadDatabase(CarArchiveReader.Open(written),
                                                  SpellRecordWriter.WrittenVersion,
                                                  ArchiveRole.Editor);

        Assert.Equal(modern.Count, read.Count);
        for (int i = 0; i < modern.Count; i++)
        {
            AssertSameSpell(modern[i], read[i]);
        }
    }

    private static void AssertSameSpell(SpellRecord expected, SpellRecord actual)
    {
        // Field by field: the record holds lists, which compare by reference.
        Assert.Equal(expected.PreSpellNameKey, actual.PreSpellNameKey);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.CastSound, actual.CastSound);
        Assert.Equal(expected.SchoolId, actual.SchoolId);
        Assert.Equal(expected.AllowedBaseclasses, actual.AllowedBaseclasses);
        Assert.Equal(expected.Level, actual.Level);
        Assert.Equal(expected.CastingTime, actual.CastingTime);
        Assert.Equal(expected.CastingTimeType, actual.CastingTimeType);
        Assert.Equal(expected.CanTargetFriend, actual.CanTargetFriend);
        Assert.Equal(expected.CanTargetEnemy, actual.CanTargetEnemy);
        Assert.Equal(expected.IsCumulative, actual.IsCumulative);
        Assert.Equal(expected.Restrictions, actual.Restrictions);
        Assert.Equal(expected.CanBeDispelled, actual.CanBeDispelled);
        Assert.Equal(expected.CanMemorize, actual.CanMemorize);
        Assert.Equal(expected.AllowScribe, actual.AllowScribe);
        Assert.Equal(expected.AutoScribe, actual.AutoScribe);
        Assert.Equal(expected.Lingers, actual.Lingers);
        Assert.Equal(expected.LingerOnceOnly, actual.LingerOnceOnly);
        Assert.Equal(expected.SaveVersus, actual.SaveVersus);
        Assert.Equal(expected.SaveResult, actual.SaveResult);
        Assert.Equal(expected.Targeting, actual.Targeting);
        Assert.Equal(expected.DurationRate, actual.DurationRate);
        Assert.Equal(expected.CastCost, actual.CastCost);
        Assert.Equal(expected.CastPriority, actual.CastPriority);

        // Six on the way out whatever came in, so compare against the padded expectation.
        Assert.Equal(SpellRecordWriter.ParameterCount, actual.Parameters.Count);
        for (int i = 0; i < SpellRecordWriter.ParameterCount; i++)
        {
            Assert.Equal(i < expected.Parameters.Count
                             ? expected.Parameters[i]
                             : DicePlusWriter.Empty,
                         actual.Parameters[i]);
        }

        Assert.Equal(expected.Effects.Count, actual.Effects.Count);
        for (int i = 0; i < expected.Effects.Count; i++)
        {
            AssertSameEffect(expected.Effects[i], actual.Effects[i]);
        }

        Assert.Equal(expected.CastArt ?? PicDataWriter.Empty, actual.CastArt);
        Assert.Equal(expected.Art, actual.Art);

        Assert.Equal(SpellRecordWriter.SoundCount, actual.Sounds.Count);
        for (int i = 0; i < SpellRecordWriter.SoundCount; i++)
        {
            Assert.Equal(i < expected.Sounds.Count ? expected.Sounds[i] : string.Empty,
                         actual.Sounds[i]);
        }

        Assert.Equal(expected.CastMessage, actual.CastMessage);
        Assert.Equal(expected.Scripts, actual.Scripts);
        Assert.Equal(expected.EffectDuration ?? DicePlusWriter.Empty, actual.EffectDuration);
        Assert.Equal(expected.SpecialAbilities.Pairs, actual.SpecialAbilities.Pairs);
        Assert.Equal(expected.Attributes, actual.Attributes);
    }

    private static void AssertSameEffect(SpellEffect expected, SpellEffect actual)
    {
        Assert.Equal(expected.IndexKey, actual.IndexKey);
        Assert.Equal(expected.Flags, actual.Flags);
        Assert.Equal(expected.ChangeResult, actual.ChangeResult);
        Assert.Equal(expected.String2, actual.String2);
        Assert.Equal(expected.SourceOfEffect, actual.SourceOfEffect);
        Assert.Equal(expected.Parent, actual.Parent);
        Assert.Equal(expected.StopTime, actual.StopTime);
        Assert.Equal(expected.Data, actual.Data);
        Assert.Equal(expected.ChangeData, actual.ChangeData);

        Assert.Equal(SpellEffectsWriter.ScriptCount, actual.Scripts.Count);
        for (int i = 0; i < SpellEffectsWriter.ScriptCount; i++)
        {
            Assert.Equal(i < expected.Scripts.Count ? expected.Scripts[i] : string.Empty,
                         actual.Scripts[i]);
        }
    }
}
