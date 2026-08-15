using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>specialAbilities.dat</c> (<c>A_SPECABILITY_DEF_L::Serialize</c>, <c>ASL.cpp:2256</c>,
/// storing branch, with <c>SPECABILITY_DEF::Serialize</c>, <c>Specab.cpp:858</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <see cref="SpecabWriter"/>.</b> That writes the <c>SpecabBlock</c> embedded in an item,
/// monster, spell, ability, baseclass, class or race record — a bare list of name/value pairs with
/// no framing. This writes the whole-file database those names refer <i>to</i>, where the scripts
/// actually live. The two share three letters and no bytes.
/// </para>
/// <para>
/// <b>Its framing is unlike every other design file and the order is the whole trick.</b> There is
/// no magic sentinel and no version <c>double</c>: a counted string names the format, and only then
/// does <c>car.Compress(true)</c> switch the archive to LZW. So the stamp goes out through the
/// plain writer, the compression-type byte follows in the clear, and the count is the first
/// compressed thing on the wire.
/// </para>
/// <para>
/// <b>Only the storing half of this record type exists in the reference.</b>
/// <c>SPECABILITY_DEF::Serialize</c>'s loading branch is <c>NotImplemented(0xfda2324)</c>
/// (<c>Specab.cpp:866</c>) — the container reads the name and the string list itself rather than
/// delegating. There is therefore no read/write pair to compare inside the record, and what is
/// transcribed here is the storing branch alone.
/// </para>
/// </remarks>
public static class SpecialAbilityDatabaseWriter
{
    /// <summary>
    /// The earliest design version whose reader reads exactly the shape written here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The file carries no version of its own beyond its format string</b>, so this is an
    /// assumption about <c>game.dat</c> like the tagged databases'. Bound by <c>_ASL_LEVEL_</c>
    /// (0.505), below which each ability's string list is written as <i>nothing at all</i> — not an
    /// empty block, no map name, no count (<c>ASL.cpp:1226</c>) — and the next ability's name would
    /// be read where the map name should be.
    /// </para>
    /// <para>
    /// Stated as <c>VersionSpellNames</c> rather than 0.505 to match its four siblings: a design is
    /// saved at one version, and a caller writing all five files wants one number. The database is
    /// also only loaded at all above 0.930 (<c>Specab.cpp:881</c>).
    /// </para>
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.SpellNames;

    /// <summary>
    /// Whether a definition can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only refusal is a name the reader would silently rewrite.</b>
    /// <see cref="SpecialAbilityDatabaseReader.RepairName"/> adds <c>0x20</c> to every character
    /// below it (<c>ASL.cpp:2296</c>), and the same fix-up runs on the ASL keys inside
    /// (<c>ASL.cpp:1236</c>). Both are one-way: a key read as <c>'%'</c> could have been written as
    /// <c>'%'</c> or as <c>0x05</c>, so a name carrying a control character cannot be written such
    /// that it comes back unchanged.
    /// </para>
    /// <para>
    /// This cannot fire on anything this port has read — the reader repairs on the way in, so what
    /// it hands back is already a fixed point. It fires on a definition built by hand, which is
    /// exactly when the silent rewrite would be a surprise.
    /// </para>
    /// </remarks>
    public static bool CanWrite(SpecialAbilityDefinition ability, out string reason)
    {
        ArgumentNullException.ThrowIfNull(ability);

        if (HasControlCharacter(ability.Name))
        {
            reason = $"Special ability '{ability.Name}' has a character below 0x20 in its name. " +
                     "The reader adds 0x20 to those as it loads (ASL.cpp:2296), so this name " +
                     "would come back as a different one and nothing in the design would match it.";
            return false;
        }

        foreach (var entry in ability.Strings)
        {
            if (HasControlCharacter(entry.Key))
            {
                reason = $"Special ability '{ability.Name}' has a string keyed with a character " +
                         "below 0x20. The compressed ASL reader applies the same one-way fix-up " +
                         "to keys (ASL.cpp:1236), so the entry would come back under a different " +
                         "key.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one <c>SPECABILITY_DEF</c>: its name, then its string list.</summary>
    /// <exception cref="NotSupportedException">
    /// When a name or key would not survive the reader's fix-up — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <b>The name is written verbatim.</b> No blank sentinel — the reference writes
    /// <c>car &lt;&lt; m_specAbName</c> and the container reads it back with a plain
    /// <c>car &gt;&gt; name</c>, so an ability named <c>"*"</c> stays <c>"*"</c>. The string list
    /// underneath is the ordinary 16-bit-counted ASL, unlike the 32-bit one in
    /// <c>races.dat</c>.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, SpecialAbilityDefinition ability)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(ability);

        if (!CanWrite(ability, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteString(ability.Name);
        AslWriter.Write(ar, WrittenVersion, SpecialAbilityDatabaseReader.MapName, ability.Strings);
    }

    /// <summary>Writes the count and every definition, into an already-compressed cursor.</summary>
    /// <remarks>
    /// <b>The count is a plain <c>int</c>, not <c>WriteCount</c></b> (<c>ASL.cpp:2263</c>) — which
    /// is the same four bytes here and would not be in an uncompressed archive. This file has no
    /// uncompressed form: <c>Compress(true)</c> is unconditional on both sides.
    /// </remarks>
    public static void WriteAll(IArchiveWriteCursor ar,
                                IReadOnlyList<SpecialAbilityDefinition> abilities)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(abilities);

        foreach (var ability in abilities)
        {
            if (!CanWrite(ability, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        ar.WriteInt32(abilities.Count);
        foreach (var ability in abilities)
        {
            Write(ar, ability);
        }
    }

    /// <summary>
    /// Writes a whole <c>specialAbilities.dat</c>: the format stamp, the compression byte, the
    /// count, then the definitions.
    /// </summary>
    /// <remarks>
    /// <b>The stamp is written before compression is switched on and is the only thing identifying
    /// this file.</b> Compressing it too would leave a file whose first bytes are an LZW block, and
    /// the reader — which has no magic number to fall back on — would reject it as not being this
    /// database at all.
    /// </remarks>
    public static void WriteFile(Stream stream, IReadOnlyList<SpecialAbilityDefinition> abilities)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(abilities);

        var plain = new MfcArchiveWriter(stream);
        plain.WriteString(SpecialAbilityDatabaseReader.Version);

        using var car = CarArchiveWriter.Open(stream);
        WriteAll(ArchiveWriteCursor.For(car), abilities);
    }

    private static bool HasControlCharacter(string value) => value.Any(c => c < 0x20);
}
