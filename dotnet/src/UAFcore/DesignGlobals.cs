using UAF.Common;
using UAF.Serialization;

namespace UAFcore;

/// <summary>One of a design's global events: the type tag on the wire, and the body.</summary>
/// <remarks>
/// A pair rather than a <c>ValueTuple</c> because it travels through view models, and a tuple has
/// fields where anything binding to it wants properties.
/// </remarks>
public sealed record GlobalEventRecord(EventType Type, IGameEvent Body);

/// <summary>The whole of <c>game.dat</c>: the record, and the global event list beside it.</summary>
public sealed record GameData(GlobalStatsPrefix Global, IReadOnlyList<GlobalEventRecord> Events);

/// <summary>
/// Reads and writes a design's <c>game.dat</c> whole.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="LoadedDesign"/> deliberately stops short of the global events, so it cannot be the
/// source for a save.</b> It reads with no event reader, which leaves the parse before the global
/// event list — enough for the design's identity, level table, money and difficulty, and cheaper
/// than decoding events nothing on the engine's open path wants. A writer needs them back, so an
/// editor that means to save <c>game.dat</c> has to read it again, from here.
/// </para>
/// <para>
/// <b>Read as <see cref="ArchiveRole.Editor"/>.</b> The two roles differ only in what they accept
/// below 0.998101, and the editor is the legacy-capable one — the engine refuses such a design
/// outright (<c>Level.cpp:3365</c>).
/// </para>
/// </remarks>
public static class DesignGlobals
{
    /// <summary>The design's <c>game.dat</c>.</summary>
    public static string Path(string root) =>
        System.IO.Path.Combine(DesignSaver.DataDirectory(root), "game.dat");

    /// <summary>
    /// Reads the whole of <c>game.dat</c>, global events included.
    /// </summary>
    public static GameData Read(string root)
    {
        using var stream = File.OpenRead(Path(root));
        var cursor = GameDataReader.Open(stream);

        // The reader reports each event as it passes but hands the body to a callback rather than
        // keeping it, so they are collected on the way through.
        var events = new List<GlobalEventRecord>();

        var global = GlobalStatsReader.Read(
            cursor.Body, cursor.Version, ArchiveRole.Editor,
            (ar, type, version) =>
            {
                var body = EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor);
                if (body is not null)
                {
                    events.Add(new GlobalEventRecord(type, body));
                }

                return body;
            });

        return new GameData(global, events);
    }

    /// <summary>Writes <c>game.dat</c> back, atomically.</summary>
    public static void Write(string root, GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        DesignSaver.SaveGameData(root, data);
    }
}
