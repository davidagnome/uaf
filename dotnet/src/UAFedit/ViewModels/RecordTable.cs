namespace UAFedit.ViewModels;

/// <summary>One column of the inspector's record list: its heading and the width it is drawn at.</summary>
public sealed record RecordColumn(string Header, double Width);

/// <summary>
/// One cell, carrying the width of the column it sits under.
/// </summary>
/// <remarks>
/// <b>The width is copied onto every cell rather than shared.</b> A row is rendered by an
/// <c>ItemsControl</c> over a variable-length cell list, and a cell's template has no other way to
/// reach the column it belongs to — templates see only their own item. The alternative, a
/// <c>Grid</c> with column definitions generated per row, buys nothing for a read-only view and
/// moves layout into code-behind.
/// </remarks>
public sealed record RecordCell(string Text, double Width);

/// <summary>One record of a category, flattened to text.</summary>
public sealed record RecordRow(IReadOnlyList<RecordCell> Cells);

/// <summary>
/// A category's columns, and its rows built on first read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows are built lazily, and that is not premature.</b> The level projection reads every
/// <c>.lvl</c> file whole — <see cref="UAFcore.LoadedDesign.Level"/> walks the entire event list to
/// reach the wall tables — so materialising every category as the tree is built would pay for a
/// full parse of every level in the design just to draw node labels.
/// </para>
/// <para>
/// The row factory yields plain strings; widths are attached here. That keeps each category's
/// projection free of layout, which is the only reason a view model gets to carry a pixel count at
/// all.
/// </para>
/// </remarks>
public sealed class RecordTable(IReadOnlyList<RecordColumn> columns,
                                Func<IEnumerable<IReadOnlyList<string>>> rows)
{
    /// <summary>The width used for a cell with no column above it, which is a projection bug.</summary>
    private const double OrphanCellWidth = 120;

    private IReadOnlyList<RecordRow>? built;

    /// <summary>What a node with no records of its own shows.</summary>
    public static RecordTable Empty { get; } = new([], static () => []);

    public IReadOnlyList<RecordColumn> Columns { get; } = columns;

    public IReadOnlyList<RecordRow> Rows => built ??= Build();

    /// <summary>Whether the rows have been built yet — the laziness above, made testable.</summary>
    public bool IsMaterialised => built is not null;

    private IReadOnlyList<RecordRow> Build() =>
        [.. rows().Select(cells =>
            new RecordRow([.. cells.Select((text, i) => new RecordCell(text, WidthAt(i)))]))];

    private double WidthAt(int index) =>
        index < Columns.Count ? Columns[index].Width : OrphanCellWidth;
}
