namespace UAF.Media;

/// <summary>One row of an items list, already rendered to strings.</summary>
/// <param name="Ready">The "READY" column, usually YES/NO. Empty when the list has no ready column.</param>
public readonly record struct ItemsFormRow(string Ready, string Quantity, string Cost, string Name);

/// <summary>One coin denomination as the form shows it.</summary>
/// <param name="Name">
/// The design's own name for the coin, not a fixed label — designs rename and add denominations.
/// </param>
/// <param name="Amount">Formatted amount. Null means the denomination is not in use.</param>
public readonly record struct ItemsFormCoin(string Name, string? Amount);

/// <summary>
/// The inventory / shop / treasure list (<c>ItemsForm.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// A layout table plus population, over <see cref="TextForm"/>. Everything game-specific arrives as
/// already-formatted strings, so this stays in <c>UAF.Media</c> with the rest of the presentation
/// and needs no item, money or party type.
/// </para>
/// <para>
/// <b>Every field id carries the white colour bit, by an enum trick that is easy to miss.</b> The
/// C++ enum opens <c>STIF_none, STIF_white = TEXT_FORM::white, STIF_READY, …</c>, so assigning
/// <c>STIF_white</c> restarts the count at <c>0x10000000</c> and <i>every id after it</i> inherits
/// that bit (<c>ItemsForm.cpp:53</c>). The form is white by default not because anything asks for
/// white, but because the ids say so — and since <c>fieldNumMask</c> keeps colour bits, dropping
/// them would break every relative placement in the table.
/// </para>
/// </remarks>
public static class ItemsFormFields
{
    private const int White = (int)FormFlags.White;

    public const int ReadyLabel = White + 1;
    public const int QuantityLabel = White + 2;
    public const int CostLabel = White + 3;
    public const int NameLabel = White + 4;
    public const int MoneyLabel = White + 5;

    /// <summary>The ten coin <i>labels</i>, in denomination order.</summary>
    public static readonly int[] CoinLabels =
        [White + 6, White + 7, White + 8, White + 9, White + 10,
         White + 11, White + 12, White + 13, White + 14, White + 15];

    /// <summary>The ten coin <i>amounts</i>, in the same order.</summary>
    public static readonly int[] CoinAmounts =
        [White + 16, White + 17, White + 18, White + 19, White + 20,
         White + 21, White + 22, White + 23, White + 24, White + 25];

    public const int Ready = White + 26;
    public const int Quantity = White + 27;
    public const int Cost = White + 28;
    public const int Name = White + 29;

    /// <summary>
    /// The row marker — <b>a zero-width placeholder, not a selection box.</b>
    /// </summary>
    /// <remarks>
    /// The layout writes it as <c>ready+SEL / name+SEL</c>, which reads as "span from Ready's left
    /// edge to Name's right", and that is what <see cref="TextForm.SetText"/> would do with it.
    /// It never gets the chance: auto-repeat expansion <b>overwrites both relative values with the
    /// plain field ids</b> of the row's first and last fields (<c>TextForm.cpp:91</c>), dropping
    /// the <see cref="FormFlags.Sel"/> bit. The field then takes its left from Ready and its top
    /// from Name, and since it carries no text its right equals its left.
    /// <para>
    /// This is why the reference builds a separate <c>InventoryRects</c> list in <c>showItems</c>
    /// rather than hit-testing rows through the form — and why
    /// <see cref="ItemsForm.RowAt"/> maps a click through the row's text fields instead.
    /// </para>
    /// </remarks>
    public const int Row = White + 30;

    /// <summary>The id offset applied to row <paramref name="index"/>'s fields.</summary>
    public static int RowOffset(int index) => (int)FormFlags.RepeatIncrement * index;
}

/// <summary>Builds and populates the items form.</summary>
public sealed class ItemsForm
{
    private const short LabelsX = 18;
    private const short LabelsY = 18;
    private const short ItemsY = 36;
    private const short MoneyX = 450;
    private const short MoneyY = 230;

    private readonly TextForm form;

    public ItemsForm(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        PageSize = pageSize;
        form = new TextForm(Layout(pageSize));
    }

    public int PageSize { get; }

    public TextForm Form => form;

    /// <summary>The currently selected row, or -1.</summary>
    public int Selection { get; private set; } = -1;

    /// <summary>
    /// The layout table, transcribed from <c>initItemsForm</c> (<c>ItemsForm.cpp:104</c>).
    /// </summary>
    /// <remarks>
    /// <b>The repeat block must come last</b> — the reference says so in a comment and it is
    /// structural: the block swallows every field after it, so anything following would silently
    /// become part of a row.
    /// </remarks>
    public static List<FormField> Layout(int pageSize)
    {
        int end = (int)FormFlags.End;
        int right = (int)FormFlags.Right;
        int sel = (int)FormFlags.Sel;

        var f = new List<FormField>
        {
            new(0, 0, ItemsFormFields.ReadyLabel, LabelsX, LabelsY),
            new(ItemsFormFields.ReadyLabel | end, 0, ItemsFormFields.QuantityLabel, 50, LabelsY),
            new(ItemsFormFields.QuantityLabel | end, 0, ItemsFormFields.CostLabel, 50, LabelsY),
            new(ItemsFormFields.CostLabel | end, 0, ItemsFormFields.NameLabel, 15, LabelsY),

            // The owner's name, well below the money block rather than above it.
            new(0, 0, ItemsFormFields.MoneyLabel, LabelsX, MoneyY + (19 * 10)),

            // Platinum anchors the money block; every other label hangs off it.
            new(0, 0, ItemsFormFields.CoinLabels[0], MoneyX, MoneyY),
            new(ItemsFormFields.CoinLabels[0] | end, ItemsFormFields.CoinLabels[0],
                ItemsFormFields.CoinAmounts[0], 50, 0),
        };

        // The other nine denominations: label 20 below the one above, amount right-aligned against
        // platinum's amount so the column of numbers lines up on its right edge.
        for (int coin = 1; coin < 10; coin++)
        {
            f.Add(new FormField(ItemsFormFields.CoinLabels[0], ItemsFormFields.CoinLabels[coin - 1],
                                ItemsFormFields.CoinLabels[coin], 0, 20));
            f.Add(new FormField(ItemsFormFields.CoinAmounts[0] | right,
                                ItemsFormFields.CoinLabels[coin],
                                ItemsFormFields.CoinAmounts[coin], 0, 0));
        }

        // Take the next five fields and repeat them pageSize times.
        f.Add(new FormField((int)FormFlags.AutoRepeat, 0, ItemsFormFields.Row + 1,
                            5, (short)pageSize));

        f.Add(new FormField(ItemsFormFields.ReadyLabel, 0, ItemsFormFields.Ready, 0, ItemsY));
        f.Add(new FormField(ItemsFormFields.QuantityLabel | right, 0, ItemsFormFields.Quantity,
                            0, ItemsY));
        f.Add(new FormField(ItemsFormFields.CostLabel | right, 0, ItemsFormFields.Cost, 0, ItemsY));
        f.Add(new FormField(ItemsFormFields.NameLabel, 0, ItemsFormFields.Name, 0, ItemsY));

        // The row marker. It is written `ready+SEL / name+SEL` and reads as a box spanning the four
        // columns -- but see the note on ItemsFormFields.Row: auto-repeat expansion overwrites both
        // flags, so it never behaves as one.
        f.Add(new FormField(ItemsFormFields.Ready | sel, ItemsFormFields.Name | sel,
                            ItemsFormFields.Row, 0, 0));

        return f;
    }

    /// <summary>
    /// Fills the form in.
    /// </summary>
    /// <param name="rows">The visible page. Shorter than <see cref="PageSize"/> is fine.</param>
    /// <param name="coins">
    /// Ten denominations in order, or empty for a list that shows no money. A coin whose
    /// <c>Amount</c> is null is blanked, which is how a design that uses only three denominations
    /// leaves the rest off the screen.
    /// </param>
    /// <remarks>
    /// <b>Column headers are blanked, not omitted.</b> A list with no cost column still lays the
    /// column out — the name column is placed relative to it, so removing the field would move
    /// every name.
    /// </remarks>
    public void Populate(BitmapFont font, IReadOnlyList<ItemsFormRow> rows,
                         bool useReady = true, bool useQuantity = true, bool useCost = true,
                         string moneyLabel = "", IReadOnlyList<ItemsFormCoin>? coins = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(rows);

        form.ClearForm();

        form.SetText(ItemsFormFields.ReadyLabel, useReady ? "READY" : "", font);
        form.SetText(ItemsFormFields.QuantityLabel, useQuantity ? "QTY" : "", font);
        form.SetText(ItemsFormFields.CostLabel, useCost ? "COST" : "", font);
        form.SetText(ItemsFormFields.NameLabel, "NAME", font);
        form.SetText(ItemsFormFields.MoneyLabel, moneyLabel, font);

        for (int coin = 0; coin < ItemsFormFields.CoinLabels.Length; coin++)
        {
            var value = coins is not null && coin < coins.Count ? coins[coin] : default;
            bool active = value.Amount is not null;

            form.SetText(ItemsFormFields.CoinLabels[coin], active ? value.Name : "", font);
            form.SetText(ItemsFormFields.CoinAmounts[coin], active ? value.Amount! : "", font);
        }

        // Every row is written, blank ones included: an unwritten field keeps no placement, and the
        // row below it is positioned relative to the row above.
        for (int i = 0; i < PageSize; i++)
        {
            int offset = ItemsFormFields.RowOffset(i);
            var row = i < rows.Count ? rows[i] : default;

            form.SetText(ItemsFormFields.Ready + offset, row.Ready ?? "", font);
            form.SetText(ItemsFormFields.Quantity + offset, row.Quantity ?? "", font);
            form.SetText(ItemsFormFields.Cost + offset, row.Cost ?? "", font);
            form.SetText(ItemsFormFields.Name + offset, row.Name ?? "", font);

            // The selection box is placed but never drawn -- it carries no text of its own.
            form.SetText(ItemsFormFields.Row + offset, "", font);
        }
    }

    /// <summary>
    /// Selects a row by index, highlighting its four fields and unhighlighting the previous one.
    /// </summary>
    public void Select(int row)
    {
        if (Selection >= 0)
        {
            SetRowHighlight(Selection, false);
        }

        Selection = row >= 0 && row < PageSize ? row : -1;

        if (Selection >= 0)
        {
            SetRowHighlight(Selection, true);
        }
    }

    private void SetRowHighlight(int row, bool highlight)
    {
        int offset = ItemsFormFields.RowOffset(row);
        form.SetHighlight(ItemsFormFields.Ready + offset, highlight);
        form.SetHighlight(ItemsFormFields.Quantity + offset, highlight);
        form.SetHighlight(ItemsFormFields.Cost + offset, highlight);
        form.SetHighlight(ItemsFormFields.Name + offset, highlight);
    }

    /// <summary>The row a click lands on, or -1.</summary>
    /// <remarks>
    /// Maps the click through the row's <i>text</i> fields, because the row marker has no width to
    /// be hit — see <see cref="ItemsFormFields.Row"/>. A click therefore only registers on the
    /// text itself, which is the reference's behaviour too, its own rect list aside.
    /// </remarks>
    public int RowAt(int x, int y)
    {
        int field = form.MouseClick(x, y);
        if (field == -1)
        {
            return -1;
        }

        // The row index lives in the byte above the field number, put there by RepeatIncrement.
        int row = (field & 0x00ff0000) / (int)FormFlags.RepeatIncrement;
        int baseField = field - ItemsFormFields.RowOffset(row);

        bool isRowField = baseField == ItemsFormFields.Ready
                          || baseField == ItemsFormFields.Quantity
                          || baseField == ItemsFormFields.Cost
                          || baseField == ItemsFormFields.Name
                          || baseField == ItemsFormFields.Row;

        return isRowField && row < PageSize ? row : -1;
    }

    public void Display(Surface destination, BitmapFont font) => form.Display(destination, font);
}
