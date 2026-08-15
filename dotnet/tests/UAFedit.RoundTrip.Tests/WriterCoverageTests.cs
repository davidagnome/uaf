using System.Reflection;
using UAF.Serialization;
using Xunit.Abstractions;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// Which design files the port can write at all.
/// </summary>
/// <remarks>
/// <para>
/// The round trip can only speak for files it has both halves of. Five of a design's databases
/// have a reader and no writer, so a save built on today's <c>UAF.Serialization</c> would
/// necessarily leave them as it found them — which is survivable while the editor cannot change
/// them and a shipped defect the moment it can.
/// </para>
/// <para>
/// <b>The gap is asserted rather than written down.</b> A comment saying "no writer for
/// races.dat" is true until somebody adds one and does not update the comment; a test that fails
/// when the writer appears is a note that cannot rot. It fails <i>green-to-red on good news</i>,
/// which is the intended behaviour: the person who adds the writer is the person who should
/// delete this.
/// </para>
/// </remarks>
public class WriterCoverageTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// The five databases with a reader, no writer, and no way for an editor to save them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named against the reader that proves the port understands the format, so the pairing is
    /// visible: the bytes are decodable and there is simply nothing to encode them again.
    /// </para>
    /// <para>
    /// <b>Two names in the writer list are traps and neither closes this gap.</b>
    /// <c>SpecabWriter</c> writes the <c>SpecabBlock</c> embedded inside item, monster and spell
    /// records, not <c>specialAbilities.dat</c> — that file is a whole-file format of its own with
    /// a <c>"SpecAbVer01"</c> stamp (<c>SpecialAbilityDatabaseReader</c>). And
    /// <c>BaseclassListWriter</c> (in <c>DicePlusWriter.cs</c>) writes the allowed-baseclass id
    /// list inside a spell record, not <c>baseclass.dat</c>.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> ReadOnlyDatabases => new()
    {
        { "ability.dat", nameof(AbilityRecordReader) },
        { "baseclass.dat", nameof(BaseclassRecordReader) },
        { "classes.dat", nameof(ClassRecordReader) },
        { "races.dat", nameof(RaceRecordReader) },
        { "specialAbilities.dat", nameof(SpecialAbilityDatabaseReader) },
    };

    /// <summary>
    /// Each of the five still has a reader and still has no writer.
    /// </summary>
    /// <remarks>
    /// The search is over the whole assembly rather than over a list of expected type names,
    /// because a writer added under any name at all closes the gap and should fail this.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReadOnlyDatabases))]
    public void A_database_the_port_can_read_and_cannot_write(string file, string readerName)
    {
        var assembly = typeof(ItemRecordReader).Assembly;

        Assert.NotNull(assembly.GetTypes().SingleOrDefault(t => t.Name == readerName));

        // The record type the reader produces. Anything that writes this database has to accept
        // it, so a writer for it is a method somewhere taking one -- whatever the method is called.
        string recordName = readerName.Replace("Reader", string.Empty, StringComparison.Ordinal)
                                      .Replace("Record", "Record", StringComparison.Ordinal);

        var writers = assembly.GetTypes()
            .Where(t => t.IsPublic && t.Name.EndsWith("Writer", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetParameters()
                         .Any(p => Mentions(p.ParameterType, recordName)))
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .Distinct()
            .ToList();

        _output.WriteLine($"{file}: read by {readerName}; writers accepting {recordName}: "
                          + (writers.Count == 0 ? "none" : string.Join(", ", writers)));

        Assert.True(writers.Count == 0,
                    $"{file} has gained a writer ({string.Join(", ", writers)}). That is good " +
                    "news and this test is now wrong: add the file to DesignCorpus.Files so the " +
                    "round trip covers it, and drop this case.");
    }

    /// <summary>
    /// Whether a parameter type is, or is a collection of, the reader's record type.
    /// </summary>
    private static bool Mentions(Type type, string recordName) =>
        type.Name.StartsWith(recordName, StringComparison.Ordinal)
        || type.GetGenericArguments()
               .Any(a => a.Name.StartsWith(recordName, StringComparison.Ordinal));

    /// <summary>
    /// The five are really in the designs on disk, so the gap is not theoretical.
    /// </summary>
    /// <remarks>
    /// A missing writer for a file no design carries would be a curiosity. Every one of these is
    /// in every design in the corpus.
    /// </remarks>
    [Fact]
    public void The_unwritable_databases_are_in_the_designs()
    {
        foreach (var design in DesignCorpus.Present())
        {
            var missing = DesignCorpus.UnwritableDatabases
                .Where(f => !File.Exists(Path.Combine(design.DataDirectory, f)))
                .ToList();

            _output.WriteLine(
                $"{design.Name}: carries "
                + $"{DesignCorpus.UnwritableDatabases.Count - missing.Count} of "
                + $"{DesignCorpus.UnwritableDatabases.Count} unwritable databases"
                + (missing.Count == 0 ? string.Empty : $" (absent: {string.Join(", ", missing)})"));
        }

        // SomethingWild and Case carry all five; DefaultDesign ships specialAbilities as .txt
        // only, so the claim worth asserting is that at least one design has each of them.
        foreach (string file in DesignCorpus.UnwritableDatabases)
        {
            Assert.Contains(DesignCorpus.Present(),
                            d => File.Exists(Path.Combine(d.DataDirectory, file)));
        }
    }
}
