using UAF.Data;

namespace UAFcore.Tests;

/// <summary>
/// Writing <c>specialAbilities.txt</c> back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim is a round trip through the model, not a rewrite of the file.</b> Comments, blank
/// lines and the original spacing are not carried in <see cref="SpecialAbility"/>, so a written
/// file will not match the one that was read byte for byte. What must hold is that reading the
/// output gives back exactly the abilities that went in — which is the property an editor needs,
/// and the only one the model can support.
/// </para>
/// <para>
/// The corpus files are the real test: three of them carry 1,131 abilities between them, most with
/// multi-line GPDL scripts, which is where a writer that mishandled continuation lines would show
/// up. Every case over one returns early when it is absent.
/// </para>
/// </remarks>
public class SpecialAbilitiesWriterTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    private static List<SpecialAbility>? Real(string relative)
    {
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, Path.Combine(relative.Split('/')));

        return path is not null && File.Exists(path) ? SpecialAbilitiesFile.Load(path) : null;
    }

    /// <summary>Writes and reads straight back.</summary>
    private static List<SpecialAbility> RoundTrip(IEnumerable<SpecialAbility> abilities) =>
        SpecialAbilitiesFile.Parse(SpecialAbilitiesFile.Format(abilities));

    private static void AssertSame(IReadOnlyList<SpecialAbility> expected,
                                   IReadOnlyList<SpecialAbility> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name);
            Assert.Equal(expected[i].Entries.Count, actual[i].Entries.Count);

            for (int e = 0; e < expected[i].Entries.Count; e++)
            {
                // Compared field by field: the entries are records holding no lists, so this is
                // only spelled out to name the field that differs when one does.
                Assert.Equal(expected[i].Entries[e].Name, actual[i].Entries[e].Name);
                Assert.Equal(expected[i].Entries[e].Value, actual[i].Entries[e].Value);
                Assert.Equal(expected[i].Entries[e].Kind, actual[i].Entries[e].Kind);
            }
        }
    }

    private static readonly string[] CorpusFiles =
    [
        "reference/SomethingWild.dsn/Data/specialAbilities.txt",
        "reference/Case.dsn/Data/specialAbilities.txt",
        "src/UAFWinEd/DefaultDesign.dsn/Data/specialAbilities.txt",
    ];

    public static TheoryData<string> Corpus => [.. CorpusFiles];

    /// <summary>The premise: at least one real file is present and has abilities in it.</summary>
    /// <remarks>
    /// <c>DefaultDesign</c> is committed, so this runs on a bare checkout — which is what stops
    /// the theory below passing while proving nothing.
    /// </remarks>
    [Fact]
    public void At_least_one_real_file_is_present()
    {
        Assert.Contains(CorpusFiles, c => Real(c) is { Count: > 0 });
    }

    /// <summary>Every real file round-trips exactly.</summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void A_real_file_round_trips(string relative)
    {
        if (Real(relative) is not { } abilities)
        {
            return;
        }

        AssertSame(abilities, RoundTrip(abilities));
    }

    /// <summary>
    /// A second write produces the same text as the first.
    /// </summary>
    /// <remarks>
    /// The fixpoint property, and the one that would catch a value gaining a line ending on every
    /// pass — a real risk here, because a multi-line value is stored joined with CRLF and written
    /// back out as separate lines.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Writing_twice_produces_the_same_text(string relative)
    {
        if (Real(relative) is not { } abilities)
        {
            return;
        }

        var first = SpecialAbilitiesFile.Format(abilities).ToList();
        var second = SpecialAbilitiesFile.Format(SpecialAbilitiesFile.Parse(first)).ToList();

        Assert.Equal(first, second);
    }

    /// <summary>Each of the four entry kinds keeps its kind.</summary>
    /// <remarks>
    /// The kind is carried by the key's bracketing and nothing else, so a writer that forgot it
    /// would turn every script into a constant — and the abilities would still all be there.
    /// </remarks>
    [Fact]
    public void The_four_kinds_all_survive()
    {
        var ability = new SpecialAbility("Thing",
        [
            new("aScript", "$RETURN 1;", SpecialAbilityEntryKind.Script),
            new("aVariable", "3", SpecialAbilityEntryKind.Variable),
            new("aTable", "1,2,3", SpecialAbilityEntryKind.IntegerTable),
            new("aConstant", "7", SpecialAbilityEntryKind.Constant),
        ]);

        AssertSame([ability], RoundTrip([ability]));
    }

    /// <summary>A multi-line script comes back with its newlines intact.</summary>
    /// <remarks>
    /// <b>The case the format's continuation lines exist for.</b> The value is GPDL source, so the
    /// separator has to be a real newline: joined with a space, a <c>//</c> comment would swallow
    /// the statement after it.
    /// </remarks>
    [Fact]
    public void A_multi_line_script_keeps_its_newlines()
    {
        var ability = new SpecialAbility("Scripted",
        [
            new("hook", "// a comment\r\n$RETURN 1;", SpecialAbilityEntryKind.Script),
        ]);

        var back = RoundTrip([ability]);

        Assert.Equal("// a comment\r\n$RETURN 1;", back[0].Entries[0].Value);
    }

    /// <summary>
    /// A trailing newline is trimmed away, and that is the reader's doing.
    /// </summary>
    /// <remarks>
    /// The reference trims both halves of every <c>key = value</c> split, so a script saved with a
    /// final blank line comes back without it. Recorded because it is the one thing a caller might
    /// reasonably expect to survive and does not — and because it is idempotent, so it costs a
    /// design nothing after the first read.
    /// </remarks>
    [Fact]
    public void A_trailing_newline_is_trimmed_and_then_stays_trimmed()
    {
        var padded = new SpecialAbility("Scripted",
            [new("hook", "$RETURN 1;\r\n", SpecialAbilityEntryKind.Script)]);

        var once = RoundTrip([padded]);
        Assert.Equal("$RETURN 1;", once[0].Entries[0].Value);

        AssertSame(once, RoundTrip(once));
    }

    /// <summary>
    /// A continuation line that itself begins with a dash survives, doubled.
    /// </summary>
    /// <remarks>
    /// <b>Case.dsn really has one</b> — <c>&lt;DexInit&gt;</c>, an integer table with a negative
    /// number on a later line. The reader strips exactly one dash from a continuation, so writing
    /// one dash in front of <c>-3</c> is not a corruption, it is the encoding.
    /// </remarks>
    [Fact]
    public void A_continuation_beginning_with_a_dash_survives()
    {
        var table = new SpecialAbility("Table",
            [new("DexInit", "1\r\n-3\r\n2", SpecialAbilityEntryKind.IntegerTable)]);

        var lines = SpecialAbilitiesFile.Format([table]).ToList();

        Assert.Contains("--3", lines);
        AssertSame([table], RoundTrip([table]));
    }

    /// <summary>An empty list is still a readable file.</summary>
    [Fact]
    public void No_abilities_is_a_file_with_a_header_and_nothing_else()
    {
        var lines = SpecialAbilitiesFile.Format([]).ToList();

        Assert.Equal([SpecialAbilitiesFile.Header], lines);
        Assert.Empty(SpecialAbilitiesFile.Parse(lines));
    }

    /// <summary>
    /// A key containing '=' is refused rather than written where it would be split wrongly.
    /// </summary>
    /// <remarks>
    /// The one shape the format cannot carry. The reader splits on the FIRST <c>=</c>, so a key
    /// with one in it comes back as a shorter key and a value with the rest of the name glued to
    /// the front — a corruption that reads back as perfectly valid data.
    /// </remarks>
    [Fact]
    public void A_key_containing_an_equals_sign_is_refused()
    {
        var badKey = new SpecialAbility("X",
            [new("a=b", "1", SpecialAbilityEntryKind.Constant)]);

        Assert.Throws<ArgumentException>(() => SpecialAbilitiesFile.Format([badKey]).ToList());
    }
}
