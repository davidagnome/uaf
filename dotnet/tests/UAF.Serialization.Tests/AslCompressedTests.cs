using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// The ASL block as it appears in <b>compressed</b> designs, verified against three real ones
/// spanning 2.53 → 5.28.
/// </summary>
/// <remarks>
/// <para>
/// Worth separating from <see cref="AslReaderTests"/> because the compressed path is a different
/// encoding of the same structure, not merely the same bytes behind a decompressor: strings are
/// interned against a stream-wide table, and keys get a fixup the plain path does not apply.
/// </para>
/// <para>
/// These fixtures live under <c>reference/</c>, which is gitignored, so each test returns early
/// when its design is absent rather than failing. CI fetches the 5.28 one, so the compressed path
/// is still exercised there.
/// </para>
/// </remarks>
public class AslCompressedTests
{
    /// <summary>Design folder under <c>reference/</c>, and the value all four entries carry.</summary>
    public static TheoryData<string, string> Designs => new()
    {
        { "dc-default/data-files", "3.56" },
        { "SomethingWild.dsn/Data", "0.9140" },
        { "Case.dsn/Data", "0.9140" },
    };

    /// <summary>The four keys, in the order compressed designs actually store them.</summary>
    private static readonly string[] CompressedKeyOrder =
        ["GuidedTourVersion", "ItemUseEventVersion", "RunAsVersion", "SpecialItemKeyQtyVersion"];

    /// <summary>The same four keys, in the order the uncompressed DefaultDesign stores them.</summary>
    private static readonly string[] PlainKeyOrder =
        ["RunAsVersion", "GuidedTourVersion", "SpecialItemKeyQtyVersion", "ItemUseEventVersion"];

    private static string? DesignPath(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        string path = Path.Combine(dir!.FullName, "reference", rel, "game.dat");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Decompresses the whole payload so the block can be located by its marker.</summary>
    private static byte[] Decompress(string path, out DesignVersion version)
    {
        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        version = cursor.Version;
        Assert.Equal(GameDataFraming.CompressedMidStream, cursor.Framing);
        cursor.ReadString();                       // designName

        var buffer = new List<byte>();
        try
        {
            while (true) buffer.Add(cursor.ReadByte());
        }
        catch (EndOfStreamException)
        {
            // Expected: reading to exhaustion proves the LZW stream terminates cleanly rather
            // than running off into garbage.
        }
        return [.. buffer];
    }

    /// <summary>Offset of the first character of the marker, or -1.</summary>
    private static int FindMarker(byte[] data, string mapName)
    {
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(mapName);
        for (int i = 8; i + needle.Length < data.Length; i++)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Walks the block, returning each entry's key, flags, and either its literal value or the
    /// table index it refers to.
    /// </summary>
    private static List<(string Key, byte Flags, string? Value, uint ValueRef)> Walk(
        byte[] data, int marker, out ushort count)
    {
        int p = marker + AslMaps.GlobalStats.Length;

        // A WORD even here: CAR::operator>>(unsigned short&) calls decompress(&v, 2)
        // (class.cpp:11865), so 2 bytes, not 4.
        count = BitConverter.ToUInt16(data, p);
        p += 2;

        var result = new List<(string, byte, string?, uint)>();
        for (int i = 0; i < count; i++)
        {
            uint keyIndex = BitConverter.ToUInt32(data, p);
            Assert.Equal(0u, keyIndex);            // every key is a fresh string; see below
            int keyLength = BitConverter.ToInt32(data, p + 4);
            string key = System.Text.Encoding.Latin1.GetString(data, p + 8, keyLength);
            p += 8 + keyLength;

            byte flags = data[p++];

            uint valueIndex = BitConverter.ToUInt32(data, p);
            if (valueIndex != 0)
            {
                p += 4;
                result.Add((AslReader.FixUpCompressedKey(key), flags, null, valueIndex));
                continue;
            }
            int valueLength = BitConverter.ToInt32(data, p + 4);
            string value = System.Text.Encoding.Latin1.GetString(data, p + 8, valueLength);
            p += 8 + valueLength;
            result.Add((AslReader.FixUpCompressedKey(key), flags, value, 0));
        }
        return result;
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Compressed_block_has_the_same_shape_as_the_plain_one(string rel, string expectedValue)
    {
        string? path = DesignPath(rel);
        if (path is null) return;                  // gitignored fixture absent

        byte[] data = Decompress(path, out var version);
        int marker = FindMarker(data, AslMaps.GlobalStats);
        Assert.True(marker > 0, $"marker not found in {rel}");

        // The marker sits behind the CAR string prologue: a 4-byte intern index (0 = new) and a
        // 4-byte length. Checking that is itself a check on the string encoding.
        Assert.Equal(0u, BitConverter.ToUInt32(data, marker - 8));
        Assert.Equal(AslMaps.GlobalStats.Length, BitConverter.ToInt32(data, marker - 4));

        var entries = Walk(data, marker, out ushort count);

        Assert.Equal(4, count);
        Assert.Equal(CompressedKeyOrder, entries.Select(e => e.Key));
        Assert.All(entries, e => Assert.Equal(AslFlags.Editor, (AslFlags)e.Flags));

        // Only entry 0 carries the value literally.
        Assert.Equal(expectedValue, entries[0].Value);
        Assert.True(version >= AslReader.MinimumVersion);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Repeated_values_are_interned_so_the_block_needs_the_string_table(
        string rel, string expectedValue)
    {
        string? path = DesignPath(rel);
        if (path is null) return;

        byte[] data = Decompress(path, out _);
        var entries = Walk(data, FindMarker(data, AslMaps.GlobalStats), out _);

        // All four entries hold the same value, so the first writes it and the rest store a
        // table index instead. Every key, by contrast, is unique and therefore written out.
        //
        // This is the constraint that matters for the port: those indices are positions in a
        // table built while reading the stream from its very start, so a compressed ASL cannot
        // be read by seeking to it -- unlike the plain encoding of this same block, which is
        // fully self-describing. It is also why CarArchiveReader must keep interning state
        // rather than decoding strings independently.
        var references = entries.Skip(1).Select(e => e.ValueRef).ToArray();
        Assert.All(references, r => Assert.NotEqual(0u, r));

        // One shared index, because it is one shared string.
        Assert.Single(references.Distinct());
        Assert.All(entries.Skip(1), e => Assert.Null(e.Value));
        Assert.Equal(expectedValue, entries[0].Value);
    }

    [Fact]
    public void Entry_order_is_hash_order_and_must_not_be_relied_on()
    {
        // A_ASLENTRY_L is a CMapStringToPtr and Serialize walks it with GetNextAssoc -- hash
        // order, not insertion order. Observed directly: the same four keys come out in one
        // order in the uncompressed DefaultDesign and a different one in all three compressed
        // designs, which span three major versions.
        //
        // Consequences for the port: look entries up by key, never by position, and compare
        // round-trips as sets -- a writer will not reproduce byte-identical ordering.
        Assert.NotEqual(PlainKeyOrder, CompressedKeyOrder);
        Assert.Equal(PlainKeyOrder.Order(), CompressedKeyOrder.Order());
    }
}
