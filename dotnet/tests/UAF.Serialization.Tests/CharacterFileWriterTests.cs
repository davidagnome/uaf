using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips the standalone <c>.chr</c> files a shipped design carries.
/// </summary>
/// <remarks>
/// The first whole file the port can write that is not a database, and the tightest test the
/// character record has: a <c>.chr</c> is a header and a <c>CHARACTER</c> and nothing else, so
/// there is no slack anywhere for a field of the wrong width to hide in.
/// </remarks>
public class CharacterFileWriterTests
{
    private static string? SavesRoot(string design)
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

        string saves = Path.Combine(dir.FullName, "reference", design, "Saves");
        return Directory.Exists(saves) ? saves : null;
    }

    private static List<string> Files()
    {
        string? root = SavesRoot("SomethingWild.dsn");
        return root is null
            ? []
            : [.. Directory.EnumerateFiles(root, "*.chr").OrderBy(p => p, StringComparer.Ordinal)];
    }

    private static byte[] Written(CharacterFile file)
    {
        var stream = new MemoryStream();
        CharacterFileWriter.Write(stream, file);
        return stream.ToArray();
    }

    [Fact]
    public void Every_shipped_character_file_round_trips()
    {
        var files = Files();
        if (files.Count == 0)
        {
            return;
        }

        Assert.Equal(6, files.Count);

        foreach (string path in files)
        {
            var original = CharacterFileReader.Read(path);

            using var written = new MemoryStream(Written(original));
            var read = CharacterFileReader.Read(written);

            Assert.True(read.HadHeader);
            Assert.Equal(CharacterFileWriter.MinimumWritableVersion, read.Version);
            AssertSameCharacter(original.Character, read.Character, path);
        }
    }

    [Fact]
    public void Writing_what_was_read_gives_the_same_bytes_the_second_time()
    {
        // The assertion that finds a field which never went out at all. It has to run against the
        // upgraded file rather than the shipped one -- see below.
        foreach (string path in Files())
        {
            byte[] first = Written(CharacterFileReader.Read(path));

            using var stream = new MemoryStream(first);
            byte[] second = Written(CharacterFileReader.Read(stream));

            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void The_shipped_files_are_upgraded_rather_than_reproduced()
    {
        var files = Files();
        if (files.Count == 0)
        {
            return;
        }

        foreach (string path in files)
        {
            var original = CharacterFileReader.Read(path);

            // All six declare 3.64, which is below the 5.24 the record is written at -- so writing
            // one back adds the icon's RestartFrame and the file grows. This is the same divergence
            // SomethingWild's monsters.dat has, from the same cause.
            Assert.True(original.Version < CharacterFileWriter.MinimumWritableVersion);

            byte[] shipped = File.ReadAllBytes(path);
            byte[] rewritten = Written(original);

            Assert.NotEqual(shipped, rewritten);

            // The first eight bytes -- the magic -- match; the next eight are the version, which
            // differs by construction because the file is being upgraded.
            const int HeaderSize = 16;
            Assert.Equal(shipped[..8], rewritten[..8]);
            Assert.NotEqual(shipped[8..HeaderSize], rewritten[8..HeaderSize]);

            // And the record differs in exactly two places, each of them four zero bytes: the
            // RestartFrame at the end of the icon's PIC_DATA and the one at the end of the small
            // pic's. Taking those two runs back out has to give the shipped bytes exactly --
            // which says what the upgrade *is*, where "the files differ" would only say that it
            // happened. In Chrysia.chr the first lands at 376, the byte after the icon ends.
            byte[] downgraded = RemoveTwoZeroedRestartFrames(shipped, rewritten);

            Assert.Equal(shipped[HeaderSize..], downgraded);
        }
    }

    [Fact]
    public void A_file_cannot_declare_a_version_the_record_is_not_written_at()
    {
        var files = Files();
        if (files.Count == 0)
        {
            return;
        }

        var character = CharacterFileReader.Read(files[0]).Character;

        // Stamping 3.64 on a 5.24 record is the one combination nothing can read.
        var ex = Assert.Throws<NotSupportedException>(
            () => CharacterFileWriter.Write(new MemoryStream(), character, new DesignVersion(3.64)));
        Assert.Contains("RestartFrame", ex.Message);
    }

    [Fact]
    public void A_version_at_or_above_the_floor_is_kept()
    {
        var files = Files();
        if (files.Count == 0)
        {
            return;
        }

        var character = CharacterFileReader.Read(files[0]).Character;

        var stream = new MemoryStream();
        CharacterFileWriter.Write(stream, character, new DesignVersion(5.29));
        stream.Position = 0;

        Assert.Equal(5.29, CharacterFileReader.Read(stream).Version.Value, 6);
    }

    [Fact]
    public void The_magic_is_at_offset_zero()
    {
        // The header goes through the archive but is read around it, and the two agree only
        // because a CAR emits nothing at construction.
        var files = Files();
        if (files.Count == 0)
        {
            return;
        }

        byte[] bytes = Written(CharacterFileReader.Read(files[0]));

        Assert.Equal(CharacterFileReader.Magic, BitConverter.ToUInt64(bytes, 0));
        Assert.Equal(CharacterFileWriter.MinimumWritableVersion.Value,
                     BitConverter.ToDouble(bytes, 8), 6);

        // ...and the shipped file agrees, byte for byte, over the same sixteen.
        Assert.Equal(File.ReadAllBytes(files[0])[..8], bytes[..8]);
    }

    /// <summary>
    /// Strips the two four-byte zero runs the upgrade inserts, returning the rewritten record body
    /// as it would have been at the shipped version.
    /// </summary>
    /// <remarks>
    /// Each run is located as the first byte at which the two streams disagree, so a run that was
    /// <i>not</i> four zeroes — or a third divergence anywhere — leaves the result unequal and the
    /// caller's assertion fails. The header is dropped because its version deliberately differs.
    /// </remarks>
    private static byte[] RemoveTwoZeroedRestartFrames(byte[] shipped, byte[] rewritten)
    {
        const int HeaderSize = 16;
        var body = new List<byte>(rewritten[HeaderSize..]);
        var original = shipped[HeaderSize..];

        for (int run = 0; run < 2; run++)
        {
            int at = 0;
            while (at < original.Length && at < body.Count && original[at] == body[at])
            {
                at++;
            }

            Assert.True(at + 4 <= body.Count,
                        $"expected a RestartFrame at byte {at}, past the end of the record");
            Assert.Equal<byte[]>([0, 0, 0, 0], [.. body.GetRange(at, 4)]);
            body.RemoveRange(at, 4);
        }

        return [.. body];
    }

    private static void AssertSameCharacter(CharacterRecord expected, CharacterRecord actual,
                                            string path)
    {
        string who = Path.GetFileName(path);

        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.PreSpellNamesKey, actual.PreSpellNamesKey);
        Assert.Equal(expected.Race, actual.Race);
        Assert.Equal(expected.ClassId, actual.ClassId);
        Assert.Equal(expected.UndeadType, actual.UndeadType);
        Assert.Equal(expected.CharacterId, actual.CharacterId);
        Assert.Equal(expected.Abilities, actual.Abilities);
        Assert.Equal(expected.HitPoints, actual.HitPoints);
        Assert.Equal(expected.MaxHitPoints, actual.MaxHitPoints);
        Assert.Equal(expected.NumberOfHitDice, actual.NumberOfHitDice);
        Assert.Equal(expected.NumberOfAttacks, actual.NumberOfAttacks);
        Assert.Equal(expected.MaxMovement, actual.MaxMovement);
        Assert.Equal(expected.UniquePartyId, actual.UniquePartyId);
        Assert.Equal(expected.OpenDoors, actual.OpenDoors);
        Assert.Equal(expected.OpenMagicDoors, actual.OpenMagicDoors);
        Assert.Equal(expected.BendBarsLiftGates, actual.BendBarsLiftGates);
        Assert.Equal(expected.BaseclassStats, actual.BaseclassStats);
        Assert.Equal(expected.SkillAdjustments, actual.SkillAdjustments);
        Assert.Equal(expected.SpellAdjustments, actual.SpellAdjustments);
        Assert.Equal(expected.Blockages, actual.Blockages);
        Assert.Equal(expected.TalkLabel, actual.TalkLabel);
        Assert.Equal(expected.ExamineLabel, actual.ExamineLabel);
        Assert.Equal(expected.Attributes, actual.Attributes);
        Assert.Equal(expected.SpecialAbilities.Pairs, actual.SpecialAbilities.Pairs);

        Assert.Equal(expected.SpellBook.UseLimits, actual.SpellBook.UseLimits);
        Assert.Equal(expected.SpellBook.Spells, actual.SpellBook.Spells);

        Assert.Equal(expected.Money!.Coins, actual.Money!.Coins);
        Assert.Equal(expected.Money.Gems, actual.Money.Gems);
        Assert.Equal(expected.Money.Jewelry, actual.Money.Jewelry);

        Assert.Equal(expected.Items.Items, actual.Items.Items);
        Assert.Equal(expected.Items.Ready.Slots, actual.Items.Ready.Slots);

        Assert.Equal(expected.SpellEffects.Count, actual.SpellEffects.Count);

        // The two PIC_DATA differ in exactly one field, and only because 3.64 predates it.
        Assert.Equal(expected.Icon! with { RestartFrame = 0 }, actual.Icon! with { RestartFrame = 0 });
        Assert.Equal(expected.SmallPic with { RestartFrame = 0 },
                     actual.SmallPic with { RestartFrame = 0 });
        Assert.Equal(0, actual.Icon!.RestartFrame);

        Assert.NotEmpty(actual.Name);
        _ = who;
    }
}
