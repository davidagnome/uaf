using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$IntegerTable</c> — looking a number up in a table an ability carries.
/// </summary>
/// <remarks>
/// The lookup arithmetic is separated from where the table came from, so it can be pinned without a
/// design. The four queries are more surprising than they look.
/// </remarks>
public class GpdlIntegerTableTests
{
    /// <summary>An ascending table, which is how a design would write one.</summary>
    private static readonly int[] Ascending = [10, 20, 30, 40];

    /// <summary>
    /// The query is chosen by the FIRST CHARACTER, not by the whole word.
    /// </summary>
    /// <remarks>
    /// <b>So a design's readable name for the operation is never actually checked.</b>
    /// <c>"Index"</c>, <c>"I"</c> and <c>"Ignore"</c> all mean the same thing, and <c>"Lowest"</c>
    /// silently means <i>less-than</i>. Worth pinning because it makes some plausible names mean
    /// something other than they read.
    /// </remarks>
    [Theory]
    [InlineData("I", GpdlTableQuery.Index)]
    [InlineData("Index", GpdlTableQuery.Index)]
    [InlineData("Ignore", GpdlTableQuery.Index)]
    [InlineData("E", GpdlTableQuery.Equal)]
    [InlineData("Equal", GpdlTableQuery.Equal)]
    [InlineData("G", GpdlTableQuery.Greater)]
    [InlineData("Greater", GpdlTableQuery.Greater)]
    [InlineData("L", GpdlTableQuery.Less)]
    [InlineData("Lowest", GpdlTableQuery.Less)]
    [InlineData("index", GpdlTableQuery.Unknown)]
    [InlineData("Find", GpdlTableQuery.Unknown)]
    [InlineData("", GpdlTableQuery.Unknown)]
    public void The_first_character_chooses_the_query(string function, GpdlTableQuery expected) =>
        Assert.Equal(expected, GpdlIntegerTable.QueryOf(function));

    /// <summary>Index reads the entry at that position.</summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(2, 30)]
    [InlineData(3, 40)]
    public void Index_reads_that_entry(int index, int expected) =>
        Assert.Equal(expected, GpdlIntegerTable.Lookup(Ascending, index, GpdlTableQuery.Index));

    /// <summary>
    /// An index past the end answers the last entry; a negative one answers -1.
    /// </summary>
    /// <remarks>
    /// <b>Clamped at the top, refused at the bottom — so a script cannot tell "off the end" from
    /// "the last value".</b> Asking for entry 999 of a four-entry table looks exactly like asking
    /// for entry 3.
    /// </remarks>
    [Fact]
    public void Index_clamps_upward_and_refuses_downward()
    {
        Assert.Equal(40, GpdlIntegerTable.Lookup(Ascending, 999, GpdlTableQuery.Index));
        Assert.Equal(40, GpdlIntegerTable.Lookup(Ascending, 3, GpdlTableQuery.Index));

        Assert.Equal(GpdlIntegerTable.NotFound,
                     GpdlIntegerTable.Lookup(Ascending, -1, GpdlTableQuery.Index));
    }

    /// <summary>Equal answers the position, not the value.</summary>
    [Fact]
    public void Equal_answers_the_position()
    {
        Assert.Equal(2, GpdlIntegerTable.Lookup(Ascending, 30, GpdlTableQuery.Equal));
        Assert.Equal(GpdlIntegerTable.NotFound,
                     GpdlIntegerTable.Lookup(Ascending, 35, GpdlTableQuery.Equal));
    }

    /// <summary>
    /// Greater answers the table's LENGTH when nothing is greater, not -1.
    /// </summary>
    /// <remarks>
    /// <b>The reference's loop variable survives the loop</b>, so falling off the end yields the
    /// count — a valid index one past the last entry. A script testing for -1 to mean "not found"
    /// silently treats "nothing was bigger" as a successful lookup.
    /// </remarks>
    [Fact]
    public void Greater_answers_the_length_when_nothing_is_greater()
    {
        Assert.Equal(0, GpdlIntegerTable.Lookup(Ascending, 5, GpdlTableQuery.Greater));
        Assert.Equal(2, GpdlIntegerTable.Lookup(Ascending, 20, GpdlTableQuery.Greater));

        // Nothing is greater than 40 -- and the answer is 4, the length.
        Assert.Equal(4, GpdlIntegerTable.Lookup(Ascending, 40, GpdlTableQuery.Greater));
        Assert.Equal(4, GpdlIntegerTable.Lookup(Ascending, 999, GpdlTableQuery.Greater));

        Assert.NotEqual(GpdlIntegerTable.NotFound,
                        GpdlIntegerTable.Lookup(Ascending, 999, GpdlTableQuery.Greater));
    }

    /// <summary>
    /// Less finds the FIRST entry below the value, which in an ascending table is index 0 or
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <b>Much less useful than it sounds.</b> A design writing an ascending table and asking for
    /// "the last entry below this" gets 0 whenever any entry qualifies at all.
    /// </remarks>
    [Fact]
    public void Less_finds_the_first_below_not_the_last()
    {
        // 10 is below 25, and it is first -- so 0 rather than 1.
        Assert.Equal(0, GpdlIntegerTable.Lookup(Ascending, 25, GpdlTableQuery.Less));
        Assert.Equal(0, GpdlIntegerTable.Lookup(Ascending, 999, GpdlTableQuery.Less));

        // Nothing is below 10, so the length again.
        Assert.Equal(4, GpdlIntegerTable.Lookup(Ascending, 10, GpdlTableQuery.Less));
    }

    /// <summary>An unrecognised query answers its own code, distinct from the others.</summary>
    [Fact]
    public void An_unrecognised_query_has_its_own_code()
    {
        Assert.Equal(GpdlIntegerTable.NoSuchQuery,
                     GpdlIntegerTable.Lookup(Ascending, 0, GpdlTableQuery.Unknown));

        // The five codes really are five distinct values a script can tell apart -- unusual for
        // this engine, which normally collapses every failure into one answer.
        int[] codes =
        [
            GpdlIntegerTable.NotFound, GpdlIntegerTable.NotATable, GpdlIntegerTable.NoSuchTable,
            GpdlIntegerTable.NoSuchAbility, GpdlIntegerTable.NoSuchQuery,
        ];

        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(codes, c => Assert.True(c < 0));
    }

    /// <summary>An empty table answers each query without reading past it.</summary>
    [Fact]
    public void An_empty_table_answers_without_reading_past_it()
    {
        Assert.Equal(GpdlIntegerTable.NotFound,
                     GpdlIntegerTable.Lookup([], 0, GpdlTableQuery.Index));
        Assert.Equal(GpdlIntegerTable.NotFound,
                     GpdlIntegerTable.Lookup([], 0, GpdlTableQuery.Equal));

        // Zero is both "the length" and "nothing matched" here.
        Assert.Equal(0, GpdlIntegerTable.Lookup([], 0, GpdlTableQuery.Greater));
        Assert.Equal(0, GpdlIntegerTable.Lookup([], 0, GpdlTableQuery.Less));
    }

    /// <summary>The whole call runs through the VM with its arguments in the right order.</summary>
    [Fact]
    public void The_call_reaches_the_host_with_its_arguments_in_order()
    {
        var host = new RecordingHost();
        var compiler = new GpdlCompiler();

        Assert.True(compiler.Compile(
            """$PUBLIC $FUNC f() { $RETURN $IntegerTable("Bless", "Levels", "7", "Equal"); } f;""")
            == 0, string.Join("; ", compiler.Errors));

        Assert.Equal("42",
                     new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host).Execute("f"));

        Assert.Equal(("Bless", "Levels", 7, GpdlTableQuery.Equal), host.Asked);
    }

    private sealed class RecordingHost : GpdlUnhostedEnvironment
    {
        public (string Ability, string Table, int Value, GpdlTableQuery Query) Asked
        { get; private set; }

        public override int IntegerTable(
            string ability, string table, int value, GpdlTableQuery query)
        {
            Asked = (ability, table, value, query);
            return 42;
        }
    }
}
