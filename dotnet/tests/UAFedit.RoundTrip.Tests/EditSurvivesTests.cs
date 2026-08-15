using UAF.Serialization;
using Xunit.Abstractions;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// One deliberate change, saved and read back.
/// </summary>
/// <remarks>
/// <para>
/// The round trip in <see cref="RoundTripTests"/> proves the port can copy a design without
/// damaging it. That is necessary and not sufficient: an editor changes something, and the change
/// has to be the <i>only</i> thing that changes. A writer that quietly normalised a neighbouring
/// field would pass every test there — the normalisation would be applied on both the reference
/// save and the edited one, so the two would agree with each other — and would still corrupt a
/// design the moment a user edited one item in it.
/// </para>
/// <para>
/// <b>So the comparison here is against the port's own unedited save, not against the shipped
/// file.</b> That subtracts the upgrade — the version stamp and the fields it materialises — and
/// leaves exactly the edit, which can then be asserted to the field.
/// </para>
/// </remarks>
public class EditSurvivesTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Renaming an item and changing its cost survives a save, and moves nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two fields in two different blocks of the record: <c>IdName</c> is in the name preamble,
    /// which is <i>interned</i> in a compressed archive — the string goes into the archive's table
    /// and later records refer to it by index — and <c>Cost</c> is a plain <c>long</c> in the
    /// scalar block. The interned one is the interesting half: a new string shifts every index
    /// assigned after it, so a writer and reader that disagreed about interning would corrupt not
    /// this record but the ones downstream of it. Asserting that <i>no other field moved</i> is
    /// what catches that, and is why this checks the whole database rather than the edited record.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DesignNames))]
    public void An_items_name_and_cost_can_be_changed_and_nothing_else_moves(string designName)
    {
        if (Writable(designName, "items.dat") is not { } writable)
        {
            return;
        }

        var (codec, baselineBytes) = writable;
        var baseline = (ItemDatabase)DesignFiles.ReadBytes(codec, baselineBytes);
        Assert.NotEmpty(baseline.Items);

        // The earliest record with a name to change, because interning makes position matter: a
        // string added near the front takes an index every record after it then has to agree
        // about. Editing the last record would put the new string where nothing follows it and
        // prove the least. Both corpus designs open with a blank-named placeholder, and renaming
        // that would test the DAS empty-string sentinel instead of the thing under test.
        int index = baseline.Items.ToList().FindIndex(i => i.Names.IdName.Length > 0);
        Assert.InRange(index, 0, baseline.Items.Count - 2);

        var original = baseline.Items[index];

        string newName = original.Names.IdName + " [edited]";
        int newCost = original.Scalars.Cost + 1234;

        var edited = new ItemDatabase(
            [.. baseline.Items.Select((item, i) => i == index
                ? item with
                {
                    Names = item.Names with { IdName = newName },
                    Scalars = item.Scalars with { Cost = newCost },
                }
                : item)],
            baseline.AmmoTypes);

        var saved = (ItemDatabase)DesignFiles.ReadBytes(codec, codec.Write(edited));

        // The edit is there.
        Assert.Equal(newName, saved.Items[index].Names.IdName);
        Assert.Equal(newCost, saved.Items[index].Scalars.Cost);

        // And it is the only thing there. Comparing against the unedited save subtracts the
        // upgrade, so anything left is the edit or a defect.
        var moved = StructuralDiff.All(baseline, saved, "items.dat");

        Assert.Equal(
            [
                $"items.dat.Items[{index}].Names.IdName: \"{original.Names.IdName}\" -> \"{newName}\" [Value]",
                $"items.dat.Items[{index}].Scalars.Cost: {original.Scalars.Cost} -> {newCost} [Value]",
            ],
            moved.Select(d => d.ToString()));

        _output.WriteLine($"{designName}: renamed item {index} of {baseline.Items.Count} " +
                          $"(\"{original.Names.IdName}\" -> \"{newName}\") and changed its cost; " +
                          $"{moved.Count} fields moved");
    }

    /// <summary>
    /// Renaming the design survives a save of <c>game.dat</c>, and moves nothing else.
    /// </summary>
    /// <remarks>
    /// A different writer and a different string convention: the design name goes out through
    /// <c>DAS</c>, which substitutes a sentinel for the empty string rather than writing a
    /// zero-length one (<c>ArchiveStringConventions</c>). It is also the widest record in the
    /// format, so "nothing else moved" is a claim over the whole of <c>GLOBAL_STATS</c> — the art
    /// slots, the level table, the journal and the global event list included.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DesignNames))]
    public void The_design_name_can_be_changed_and_nothing_else_moves(string designName)
    {
        if (Writable(designName, "game.dat") is not { } writable)
        {
            return;
        }

        var (codec, baselineBytes) = writable;
        var baseline = (GameDataModel)DesignFiles.ReadBytes(codec, baselineBytes);

        string renamed = baseline.Global.DesignName + " [edited]";
        var edited = baseline with { Global = baseline.Global with { DesignName = renamed } };

        var saved = (GameDataModel)DesignFiles.ReadBytes(codec, codec.Write(edited));

        Assert.Equal(renamed, saved.Global.DesignName);

        var moved = StructuralDiff.All(baseline, saved, "game.dat");
        Assert.Equal(
            [$"game.dat.Global.DesignName: \"{baseline.Global.DesignName}\" -> \"{renamed}\" [Value]"],
            moved.Select(d => d.ToString()));

        _output.WriteLine($"{designName}: renamed the design and {moved.Count} fields moved");
    }

    /// <summary>
    /// The design's own save of one file, or null when it has none the port can write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The baseline is the port's <i>unedited</i> output, not the shipped file. Reading a design
    /// at 2.53 and saving it upgrades it, and comparing an edited 5.24 save against a shipped 2.53
    /// file would report the whole upgrade alongside the edit — with the edit indistinguishable
    /// from the noise around it.
    /// </para>
    /// <para>
    /// Returns null for a design the writers refuse, which is <c>DefaultDesign</c> at 0.915025 for
    /// every file it has. The refusals are enumerated by
    /// <c>RoundTripTests.Reading_a_design_and_writing_it_back</c>; nothing is hidden by returning
    /// early from them here.
    /// </para>
    /// </remarks>
    private (DesignFileCodec Codec, byte[] Baseline)? Writable(string designName, string file)
    {
        if (DesignCorpus.Find(designName) is not { } design)
        {
            _output.WriteLine($"{designName}: not on this machine (reference/ is gitignored)");
            return null;
        }

        string path = Path.Combine(design.DataDirectory, file);
        if (!File.Exists(path)
            || DesignFiles.CodecFor(path, DesignFiles.GlobalVersion(design)) is not { } codec)
        {
            return null;
        }

        try
        {
            return (codec, codec.Write(DesignFiles.ReadFile(codec, path)));
        }
        catch (Exception e) when (e is NotSupportedException or EndOfStreamException)
        {
            _output.WriteLine($"{designName}/{file}: not writable -- {e.Message}");
            return null;
        }
    }

    public static TheoryData<string> DesignNames => DesignCorpus.Names;
}
