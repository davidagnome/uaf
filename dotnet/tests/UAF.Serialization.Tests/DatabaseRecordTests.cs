using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the first monster and spell records and diffs their names against the oracle.
/// </summary>
public class DatabaseRecordTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string DataFile(string name) =>
        Path.Combine(RepoRoot(), "src", "UAFWinEd", "DefaultDesign.dsn", "Data", name);

    private static JsonElement? Golden()
    {
        string path = Path.Combine(RepoRoot(), "oracle", "golden", "DefaultDesign.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static (int Count, string FirstName, DesignVersion Version) ReadFirst(string fileName)
    {
        using var fs = File.OpenRead(DataFile(fileName));
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        int count = ar.ReadInt32();
        var (_, name) = DatabaseRecordReader.ReadPreamble(ar, header.Version);
        return (count, name, header.Version);
    }

    [Theory]
    [InlineData("monsters.dat", "monsterNames")]
    [InlineData("spells.dat", "spellNames")]
    public void First_record_name_agrees_with_the_oracle(string fileName, string goldenKey)
    {
        if (Golden() is not { } root) { return; }
        var names = root.GetProperty(goldenKey);

        var (count, firstName, _) = ReadFirst(fileName);

        Assert.Equal(names.GetArrayLength(), count);
        Assert.Equal(names[0].GetString(), firstName);
    }

    [Fact]
    public void Monster_and_spell_preambles_omit_the_item_spellID_field()
    {
        // ITEM_DATA reads preSpellNameKey AND spellID (the latter a string, since SPELL_ID
        // derives from CString). Monster and spell records read only preSpellNameKey. Reusing
        // the item preamble here would consume a field that is not in the stream -- proven by
        // the fact that the names below come out correct without it.
        var (_, monster, _) = ReadFirst("monsters.dat");
        var (_, spell, _) = ReadFirst("spells.dat");

        Assert.Equal("Kobold", monster);
        Assert.Equal("Bless", spell);
    }

    [Fact]
    public void Pre_spell_name_key_is_absent_only_inside_the_unreliable_window()
    {
        // The field is skipped exactly on [VersionSpellNames, VersionSaveIDs) -- which is also
        // the range the editor warns it cannot load reliably (Level.cpp:3340). Designs outside
        // it, in either direction, carry the key.
        Assert.True(DatabaseRecordReader.HasPreSpellNameKey(new DesignVersion(0.915025)));
        Assert.True(DatabaseRecordReader.HasPreSpellNameKey(DesignVersion.SaveIDs));
        Assert.True(DatabaseRecordReader.HasPreSpellNameKey(DesignVersion.V529));

        Assert.False(DatabaseRecordReader.HasPreSpellNameKey(DesignVersion.SpellNames));
        Assert.False(DatabaseRecordReader.HasPreSpellNameKey(new DesignVersion(0.9985)));
    }

    [Fact]
    public void Spell_names_carry_the_pipe_qualifier_like_items()
    {
        if (Golden() is not { } root) { return; }

        // e.g. "Detect Magic|Cleric" -- the same convention items use, on a single Name field
        // rather than an id/unique pair. Stripping it would collide distinct spells.
        var spells = root.GetProperty("spellNames").EnumerateArray()
                         .Select(e => e.GetString() ?? string.Empty)
                         .ToList();
        Assert.Contains(spells, s => s.Contains('|'));
    }
}
