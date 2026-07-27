using UAF.Common;

namespace UAF.Serialization;

/// <summary>One special ability as modern designs store it: a name and a value.</summary>
public sealed record SpecabPair(string Key, string Value);

/// <summary>
/// A legacy special-ability slot, as designs at or below 0.920 stored it.
/// </summary>
/// <remarks>
/// The reference reads these only to convert them into the modern form, discarding any slot whose
/// fields are all empty. The port keeps them: consuming the right number of bytes is what lets the
/// stream advance, and throwing the contents away would make the conversion unverifiable.
/// </remarks>
public sealed record LegacySpecabSlot(
    string ActivationScript,
    string ActivationBinary,
    string DeactivationScript,
    string DeactivationBinary,
    uint MessageTypes,
    int DisplayOnce,
    IReadOnlyList<string> Messages)
{
    /// <summary>
    /// Whether the reference would have kept this slot — its <c>empty != 0</c> test.
    /// </summary>
    /// <remarks>
    /// Note the test sums string <i>lengths</i> and adds one when <c>MessageTypes</c> is
    /// non-zero; <c>DisplayOnce</c> never contributes. So a slot that sets only
    /// <c>DisplayOnce</c> is dropped.
    /// </remarks>
    public bool IsKept =>
        ActivationScript.Length + ActivationBinary.Length
        + DeactivationScript.Length + DeactivationBinary.Length
        + Messages.Sum(m => m.Length)
        + (MessageTypes != 0 ? 1 : 0) != 0;
}

/// <summary>The result of reading an object's special abilities.</summary>
/// <param name="Pairs">Populated on the modern path.</param>
/// <param name="LegacySlots">Populated on the legacy path.</param>
/// <param name="LegacyOrdinals">Populated on the oldest path (below 0.850).</param>
public sealed record SpecabBlock(
    IReadOnlyList<SpecabPair> Pairs,
    IReadOnlyList<LegacySpecabSlot> LegacySlots,
    IReadOnlyList<ushort> LegacyOrdinals);

/// <summary>
/// Reads the special-abilities block that precedes every record's ASL
/// (<c>Specab.cpp:1153</c> / <c>:1418</c>).
/// </summary>
/// <remarks>
/// <para>
/// Despite <c>Specab.cpp</c> running to 2,240 lines, the serialized shape is small. Everything
/// turns on one gate:
/// </para>
/// <code>
///   if (version &lt;= 0.920 &amp;&amp; !ar.IsStoring())   // legacy conversion -- READING only
///       ...
///   else
///       m_specialAbilities.Serialize(ar);      // an A_CStringPAIR_L
/// </code>
/// <para>
/// <b>The gate is asymmetric.</b> The legacy branch is conditioned on <c>!IsStoring()</c>, so old
/// designs are read in the old shape but always written back in the new one. A port that treats
/// this as a symmetric format fork will write files the reference cannot read.
/// </para>
/// <para>
/// <b>Both fixture families are in play.</b> The DefaultDesign the C++ oracle dumps is 0.915 and
/// therefore takes the <i>legacy</i> branch, while the compressed designs (2.53, 3.55, 5.28) all
/// take the modern one. Neither path can be dismissed as unreachable.
/// </para>
/// <para>
/// <b>Two contrasts with the sibling ASL structure</b>, which is easy to conflate with this one
/// since both live in <c>ASL.cpp</c>: <c>A_CStringPAIR_L</c> counts with a 32-bit <c>int</c> where
/// <c>ASL</c> uses a <c>WORD</c>, and it reads strings <i>verbatim</i> where the legacy path below
/// wraps them in <c>DAS</c> (the <c>"*"</c>/blank convention). It also carries no map-name marker
/// and no flags byte.
/// </para>
/// </remarks>
public static class SpecabReader
{
    /// <summary>
    /// At or below this, reading takes the legacy conversion path.
    /// </summary>
    /// <remarks>
    /// Spelled as a literal in the reference (<c>Specab.cpp:1155</c>) rather than as one of the
    /// named version constants, though it coincides exactly with <c>_VERSION_0920_</c>.
    /// </remarks>
    public static DesignVersion LegacyGate => new(0.920);

    /// <summary>Below this the block is a bare array of ability ordinals.</summary>
    public static DesignVersion OrdinalArrayGate => DesignVersion.V0850;

    /// <summary>Cap on the per-slot message count; exceeding it is fatal in the reference.</summary>
    public const int MaxSpecabMessages = 14;

    /// <summary>True when <b>reading</b> this version takes the legacy conversion path.</summary>
    public static bool UsesLegacyConversion(DesignVersion version) => version <= LegacyGate;

    public static SpecabBlock Read(MfcArchiveReader reader, DesignVersion version) =>
        Read(ArchiveCursor.For(reader), version);

    public static SpecabBlock Read(CarArchiveReader reader, DesignVersion version) =>
        Read(ArchiveCursor.For(reader), version);

    /// <summary>
    /// Reads the block. Safe to share between the plain and compressed paths: unlike
    /// <c>ASLENTRY</c>, the two C++ twins here are byte-identical — <c>A_CStringPAIR_L</c> applies
    /// no key fixup (<c>ASL.cpp:1878</c>), and the legacy walk differs only in what it builds.
    /// </summary>
    public static SpecabBlock Read(IArchiveCursor cursor, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        return UsesLegacyConversion(version)
            ? ReadLegacy(cursor, version)
            : new SpecabBlock(ReadPairs(cursor), [], []);
    }

    /// <summary>
    /// Reads an <c>A_CStringPAIR_L</c> — the modern representation (<c>ASL.cpp:1848</c>).
    /// </summary>
    public static List<SpecabPair> ReadPairs(IArchiveCursor cursor)
    {
        int count = cursor.ReadInt32();            // int, not WORD
        var pairs = new List<SpecabPair>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            // Verbatim: no DAS here, so "*" stays "*".
            string key = cursor.ReadString();
            string value = cursor.ReadString();
            pairs.Add(new SpecabPair(key, value));
        }
        return pairs;
    }

    private static SpecabBlock ReadLegacy(IArchiveCursor cursor, DesignVersion version)
    {
        int count = cursor.ReadInt32();

        // The reference clamps rather than rejecting (Specab.cpp:1168). Worth mirroring: a
        // negative count is how a desynchronised stream shows up here, and clamping keeps the
        // failure where the reference put it instead of throwing somewhere new.
        if (count < 0) count = 0;

        if (version < OrdinalArrayGate)
        {
            // Oldest form: a bare array of ability ordinals, 16 bits each.
            var ordinals = new List<ushort>(count);
            for (int i = 0; i < count; i++)
            {
                ordinals.Add(cursor.ReadUInt16());
            }
            return new SpecabBlock([], [], ordinals);
        }

        var slots = new List<LegacySpecabSlot>(count);
        for (int i = 0; i < count; i++)
        {
            // Exact equality, not a range: only 0.850 itself carries this field
            // (Specab.cpp:1203). One of the few places the format tests a single version.
            if (version == DesignVersion.V0850)
            {
                cursor.ReadInt32();                // unused
            }

            string script = ReadDas(cursor);

            string binary = version >= DesignVersion.V0851 ? ReadDas(cursor) : string.Empty;

            string deactivationScript = string.Empty;
            string deactivationBinary = string.Empty;
            if (version >= DesignVersion.V0852)
            {
                deactivationScript = ReadDas(cursor);
                deactivationBinary = ReadDas(cursor);
            }

            uint messageTypes = 0;
            int displayOnce = 0;
            var messages = new List<string>();
            if (version >= DesignVersion.V0870)
            {
                messageTypes = cursor.ReadUInt32();
                displayOnce = cursor.ReadInt32();          // BOOL: 4 bytes on Win32
                int messageCount = cursor.ReadInt32();

                // The reference calls die(0xab537) here. A count above the cap means the stream
                // has desynchronised, and continuing would read arbitrary lengths as strings.
                if (messageCount > MaxSpecabMessages)
                {
                    throw new InvalidDataException(
                        $"Special-ability message count {messageCount} exceeds the maximum of " +
                        $"{MaxSpecabMessages}; the stream is misaligned.");
                }

                for (int m = 0; m < messageCount; m++)
                {
                    messages.Add(ReadDas(cursor));
                }
            }

            slots.Add(new LegacySpecabSlot(
                script, binary, deactivationScript, deactivationBinary,
                messageTypes, displayOnce, messages));
        }

        return new SpecabBlock([], slots, []);
    }

    /// <summary>Reads a string through the <c>DAS</c> blank convention (<c>Externs.h:1951</c>).</summary>
    private static string ReadDas(IArchiveCursor cursor) =>
        ArchiveStringConventions.Decode(cursor.ReadString());
}
