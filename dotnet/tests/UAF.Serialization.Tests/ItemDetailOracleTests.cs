using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Diffs every field of the eight fully-dumped item records against the C++ oracle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ItemWalkTests"/> proves alignment across all 285 records; this proves the
/// <i>values</i>. Names alone can be right while a numeric field is misread — reading a
/// <c>double</c> as two <c>long</c>s, say, keeps every later string in place while corrupting two
/// numbers. Only a field-level diff against the reference catches that.
/// </para>
/// <para>
/// The oracle is built into UAFWinEd, so these are editor semantics by construction.
/// </para>
/// </remarks>
public class ItemDetailOracleTests
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

    private static JsonElement[]? OracleDetails()
    {
        string path = Path.Combine(RepoRoot().FullName, "oracle", "golden", "DefaultDesign.json");
        if (!File.Exists(path)) return null;

        var doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.GetProperty("itemDetails").EnumerateArray()];
    }

    private static ItemDatabase ReadItems()
    {
        string path = Path.Combine(RepoRoot().FullName,
            "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "items.dat");
        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        var ar = new MfcArchiveReader(fs);
        return ItemRecordReader.ReadDatabase(ar, header.Version, ArchiveRole.Editor);
    }

    [Fact]
    public void Every_dumped_field_matches_the_oracle()
    {
        var expected = OracleDetails();
        if (expected is null) return;              // golden dump not produced yet

        var items = ReadItems().Items;
        Assert.True(items.Count >= expected.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            var e = expected[i];
            var actual = items[i];

            Assert.Equal(e.GetProperty("uniqueName").GetString(), actual.Names.UniqueName);
            Assert.Equal(e.GetProperty("idName").GetString(), actual.Names.IdName);
            Assert.Equal(e.GetProperty("hitSound").GetString(), actual.Names.HitSound);
            Assert.Equal(e.GetProperty("missSound").GetString(), actual.Names.MissSound);
            Assert.Equal(e.GetProperty("launchSound").GetString(), actual.Names.LaunchSound);
            Assert.Equal(e.GetProperty("preSpellNameKey").GetInt32(), actual.Names.PreSpellNameKey);

            Assert.Equal(e.GetProperty("ammoType").GetString(), actual.Scalars.AmmoType);
            Assert.Equal(e.GetProperty("experience").GetInt32(), actual.Scalars.Experience);
            Assert.Equal(e.GetProperty("cost").GetInt32(), actual.Scalars.Cost);
            Assert.Equal(e.GetProperty("encumbrance").GetInt32(), actual.Scalars.Encumbrance);
            Assert.Equal(e.GetProperty("attackBonus").GetInt32(), actual.Scalars.AttackBonus);
            Assert.Equal(e.GetProperty("cursed").GetInt32(), actual.Scalars.Cursed);
            Assert.Equal(e.GetProperty("bundleQty").GetInt32(), actual.Scalars.BundleQty);
            Assert.Equal(e.GetProperty("numCharges").GetInt32(), actual.Scalars.NumCharges);

            // The oracle reports the CONVERTED base-38 name, not the ordinal on disk.
            Assert.Equal(e.GetProperty("locationReadied").GetUInt32(),
                         ReadiedLocation.Convert(actual.Combat.LocationReadied));

            Assert.Equal(e.GetProperty("handsToUse").GetInt32(), actual.Combat.HandsToUse);
            Assert.Equal(e.GetProperty("dmgDiceSm").GetInt32(), actual.Combat.DmgDiceSm);
            Assert.Equal(e.GetProperty("nbrDiceSm").GetInt32(), actual.Combat.NbrDiceSm);
            Assert.Equal(e.GetProperty("dmgBonusSm").GetInt32(), actual.Combat.DmgBonusSm);
            Assert.Equal(e.GetProperty("dmgDiceLg").GetInt32(), actual.Combat.DmgDiceLg);
            Assert.Equal(e.GetProperty("nbrDiceLg").GetInt32(), actual.Combat.NbrDiceLg);
            Assert.Equal(e.GetProperty("dmgBonusLg").GetInt32(), actual.Combat.DmgBonusLg);
            Assert.Equal(e.GetProperty("rofPerRound").GetDouble(), actual.Combat.RofPerRound);
            Assert.Equal(e.GetProperty("protectionBase").GetInt32(), actual.Combat.ProtectionBase);
            Assert.Equal(e.GetProperty("protectionBonus").GetInt32(), actual.Combat.ProtectionBonus);
        }
    }

    [Fact]
    public void Base38_encoding_reproduces_the_declared_constants()
    {
        // Independently checkable: QUIVER is the value the oracle reports for an arrow, and the
        // arithmetic is fixed by Items.h:105. Pinning a couple of these means a regression in the
        // encoder is caught even without the golden dump present.
        Assert.Equal(2286454785u, ReadiedLocation.Base38("QUIVER"));
        Assert.Equal(ReadiedLocation.Base38("QUIVER"), ReadiedLocation.Convert(10));
        Assert.Equal(ReadiedLocation.Base38("WEAPON"), ReadiedLocation.Convert(0));

        // A blank encodes as 1, not 0 -- `blank` is defined as 'A'-11 (Items.h:108), so the +12
        // shift lands it on 1. Zero would collide with nothing, but it would still be wrong.
        Assert.Equal(ReadiedLocation.Base38("HEAD  "), ReadiedLocation.Convert(4));
        Assert.NotEqual(ReadiedLocation.Base38("HEAD  "), ReadiedLocation.Base38("HEADAA"));

        // Values at or above the ordinal range are already base-38 names and pass through.
        Assert.Equal(2286454785u, ReadiedLocation.Convert(2286454785u));
        Assert.False(ReadiedLocation.IsLegacyOrdinal(2286454785u));
    }
}
