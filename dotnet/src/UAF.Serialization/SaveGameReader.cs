using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>.pty</c> savegame header, and a cursor positioned at its body.</summary>
public sealed record SaveGame(DesignVersion Version, IArchiveCursor Body);

/// <summary>
/// Reads a saved game (<c>.pty</c>), written by <c>serializeGame</c>
/// (<c>UAFWin/Dgngame.cpp:95</c>).
/// </summary>
/// <remarks>
/// <para>
/// The sixth container framing: an 8-byte <c>double</c> version read straight off the file, then a
/// <b>compressed</b> CAR — <c>CAR car(&amp;myFile, load); car.Compress(true);</c>
/// (<c>Dgngame.cpp:184-186</c>). The compression-type byte sits at offset 8 and reads 0x02 in both
/// shipped files, so this is tier 3, the same LZW layer as a compressed <c>game.dat</c>.
/// </para>
/// <para>
/// <b>Only the framing is ported. The body is not.</b> A savegame continues with
/// <c>PARTY::Serialize(CAR&amp;)</c> (<c>Party.cpp:953</c>) and then
/// <c>QUEST_LIST</c>, two <c>SPECIAL_OBJECT_LIST</c>s, the global vaults, an
/// <c>QUEST_LIST</c>, two <c>SPECIAL_OBJECT_LIST</c>s, the global vaults, an
/// <c>ACTIVE_SPELL_LIST</c>, and seven <c>Restore</c> calls covering spells, globals, level info,
/// keys, special items, items and monsters (<c>Dgngame.cpp:188-236</c>) — a different verb from
/// <c>Serialize</c>, and one not yet examined.
/// </para>
/// <para>
/// <b>What is known about <c>PARTY</c>, and what defeated a first attempt.</b> Its record does not
/// begin where a search for its clock fields lands: a task state stack comes first
/// (<c>Party.cpp:996</c>), and reading from <c>days</c> consumes that stack's first four values as
/// the time of day. Adding it got the clock right — day 1, 08:02 in <c>SomethingWild</c>'s save —
/// but everything after still drifts, and a hand decode of the inflated stream shows why: the
/// second task's flags read as <c>0x1F60C049</c> and the third's <c>datacount</c> as 249 against a
/// declared maximum of 5 (<c>MAX_TASK_STATE_SAVE_BYTES</c>). So <c>TASK_STATE_SAVE</c>'s field
/// widths are still wrong, and note that its name lies twice over: <c>datacount</c> is an
/// <c>unsigned char</c> between two <c>unsigned int</c>s, and <c>data[]</c> is an array of
/// <b>uints</b> despite the constant saying bytes (<c>Party.h:421-428</c>).
/// </para>
/// <para>
/// The framing itself <i>is</i> verified: both shipped saves declare a plausible version, the LZW
/// layer inflates, and the first value out is a task count of 5 and 4 respectively. That is what
/// this class exposes, and no more — a reader whose output is known to be wrong is worse than
/// none, because it would be believed.
/// </para>
/// </remarks>
public static class SaveGameReader
{
    /// <summary>
    /// Below this the engine refuses outright — the event system changed shape
    /// (<c>Dgngame.cpp:157</c>).
    /// </summary>
    public static DesignVersion MinimumVersion => DesignVersion.V0573;

    /// <summary>
    /// The engine also refuses anything below <c>VersionSpellNames</c>, and that same threshold
    /// selects the compressed path (<c>Dgngame.cpp:164,180</c>) — so every loadable save is
    /// compressed and the plain-<c>CArchive</c> branch below it is unreachable in practice.
    /// </summary>
    public static DesignVersion CompressedFrom => DesignVersion.SpellNames;

    public static SaveGame Read(Stream stream, ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Seek(0, SeekOrigin.Begin);

        // Read off the raw file, not through any archive.
        var header = new MfcArchiveReader(stream);
        var version = new DesignVersion(header.ReadDouble());

        if (version < MinimumVersion)
        {
            throw new NotSupportedException(
                $"save game version {version.Value} pre-dates the event conversion; the engine " +
                "refuses it too (Dgngame.cpp:157)");
        }

        if (version < CompressedFrom)
        {
            throw new NotSupportedException(
                $"save game version {version.Value} is below VersionSpellNames " +
                $"({CompressedFrom.Value}); the engine refuses it (Dgngame.cpp:164)");
        }

        return new SaveGame(version, ArchiveCursor.For(CarArchiveReader.Open(stream)));
    }

    public static SaveGame Read(string path, ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Read(stream, role);
    }

}
