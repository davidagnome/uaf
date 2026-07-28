using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One attribute a spell modifies, plus the scripts that drive it
/// (<c>class.h:2315</c>, serialized at <c>Spell.cpp:201</c>).
/// </summary>
/// <remarks>
/// <para>
/// The eleven strings are not a uniform array: they alternate between GPDL <b>source</b> and its
/// <b>compiled binary</b> from <c>String3</c> onward, and they arrived in three separate waves
/// (0.851, 0.904, 0.910). The struct comments name them only as "compiled GPDL script", so the
/// pairing is positional rather than declared.
/// </para>
/// <para>
/// Also used for timed special abilities, not just spells.
/// </para>
/// </remarks>
public sealed record SpellEffect(
    string IndexKey,
    uint Flags,
    double ChangeResult,
    string String2,
    uint SourceOfEffect,
    uint Parent,
    IReadOnlyList<string> Scripts,
    uint StopTime,
    uint Data,
    DicePlus ChangeData);

/// <summary>
/// Reads <c>SPELL_EFFECTS_DATA</c> (<c>Spell.cpp:201</c>).
/// </summary>
/// <remarks>
/// <para>
/// Two traps. <c>changeResult</c> is a <b><c>double</c></b> sitting between <c>DWORD</c>
/// neighbours (<c>class.h:2399</c>); reading it as 4 bytes misaligns the rest of the effect while
/// still producing readable strings. Note its "no change" sentinel is
/// <c>-1.2345678901234568e18</c>, which looks like corruption but is not.
/// </para>
/// <para>
/// More easily missed: <c>changeData</c> (a <see cref="DicePlus"/>) is serialized at
/// <c>Spell.cpp:273</c> — <b>outside</b> the storing/loading <c>if</c>, after the brace that
/// closes it. It is therefore read on both paths and belongs to every effect. The same shape
/// catches out <c>ITEM_DATA</c>, whose <c>specAbs</c> and ASL sit outside its branch too, so it is
/// worth checking the lines after the closing brace of every <c>Serialize</c>.
/// </para>
/// </remarks>
public static class SpellEffectsReader
{
    public static SpellEffect Read(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string indexKey = ReadDas(ar);

        // A retired field, still on the wire in old designs. It was `changeText`; the reference
        // reads it into a local named `unused` and drops it (Spell.cpp:231).
        if (version <= DesignVersion.V0682)
        {
            ar.ReadString();
        }

        uint flags = ar.ReadUInt32();

        // 8 bytes, not 4 -- the one width trap in this structure.
        double changeResult = version >= DesignVersion.V0690 ? ar.ReadDouble() : 0;

        string string2 = string.Empty;
        uint sourceOfEffect = 0;
        uint parent = 0;
        if (version >= DesignVersion.V0699)
        {
            string2 = ReadDas(ar);
            sourceOfEffect = ar.ReadUInt32();   // a POSITION in the editor, a db key in the engine
            parent = ar.ReadUInt32();
        }

        // Three waves of script strings. Each wave is all-or-nothing, so a version between two
        // waves yields a short list rather than empty slots.
        var scripts = new List<string>();
        if (version >= DesignVersion.V0851)
        {
            scripts.Add(ReadDas(ar));                       // m_string3
        }
        if (version >= DesignVersion.V0904)
        {
            for (int i = 0; i < 4; i++)
            {
                scripts.Add(ReadDas(ar));                   // m_string4..7
            }
            if (version >= DesignVersion.V0910)
            {
                for (int i = 0; i < 4; i++)
                {
                    scripts.Add(ReadDas(ar));               // m_string8..11
                }
            }
        }

        uint stopTime = version >= DesignVersion.V0906 ? ar.ReadUInt32() : 0;
        uint data = version >= DesignVersion.V0909 ? ar.ReadUInt32() : 0;

        // Outside the branch in the reference (Spell.cpp:273), so ungated and always present.
        var changeData = DicePlusReader.Read(ar);

        return new SpellEffect(indexKey, flags, changeResult, string2, sourceOfEffect,
                               parent, scripts, stopTime, data, changeData);
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
