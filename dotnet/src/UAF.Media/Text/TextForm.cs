namespace UAF.Media;

/// <summary>
/// Flags packed into a form field's id and into its relative-placement values
/// (<c>TEXT_FORM::flags</c>, <c>UAFWin/TextForm.h:120</c>).
/// </summary>
/// <remarks>
/// <b>These live in two different places and the split is not obvious.</b> <see cref="Tab"/>,
/// <see cref="White"/> and <see cref="Green"/> are bits of the <i>field id</i>; <see cref="Sel"/>,
/// <see cref="End"/>, <see cref="Right"/>, <see cref="RightJust"/> and <see cref="AutoRepeat"/> are
/// bits of the <i>relative</i> values. A field id therefore is not a plain number, and comparing
/// ids must go through <see cref="FormField.NumberMask"/>, which deliberately keeps the colour
/// bits — two fields differing only in colour are different fields.
/// </remarks>
[Flags]
public enum FormFlags : uint
{
    None = 0,

    /// <summary>In the field id: this field is a tab stop.</summary>
    Tab = 0x40000000,

    /// <summary>In the field id: draw white unless a colour is given.</summary>
    White = 0x10000000,

    /// <summary>In the field id: draw green unless a colour is given.</summary>
    Green = 0x20000000,

    /// <summary>In a relative value: place after the related field's far edge.</summary>
    End = 0x01000000,

    /// <summary>In a relative value: this field is selectable, and shares the related field's box.</summary>
    Sel = 0x02000000,

    /// <summary>In <c>XRelative</c> only, and as an exact value rather than a bit — see the remarks
    /// on <see cref="TextForm"/>.</summary>
    AutoRepeat = 0x03000000,

    /// <summary>In a relative value: right-align against the related field's right edge.</summary>
    Right = 0x04000000,

    /// <summary>In <c>XRelative</c>: right-justify the text at its own x.</summary>
    RightJust = 0x08000000,

    /// <summary>Added to a field id once per generated row of an auto-repeat block.</summary>
    RepeatIncrement = 0x00010000,
}

/// <summary>
/// One row of a form's layout table (<c>DISPLAY_FORM</c>, <c>UAFWin/TextForm.h:24</c>).
/// </summary>
/// <param name="XRelative">
/// Either flags plus the id of the field this one is placed against, or 0 for absolute.
/// </param>
/// <param name="Column">
/// A column group, or 0 for none. Columns are pushed right at draw time so each clears every
/// column before it; that is how variable-width tables line up.
/// </param>
/// <param name="Space">Minimum gap before this field's column.</param>
public readonly record struct FormField(int XRelative, int YRelative, int FieldId, short X, short Y,
                                        short Column = 0, short Space = 0)
{
    /// <summary>
    /// The mask that turns a field id into something comparable — <b>and it keeps the colour
    /// bits</b> (<c>fieldNumMask = 0x30ffffff</c>), so two fields with the same number but
    /// different colours do not match each other.
    /// </summary>
    public const int NumberMask = 0x30ffffff;
}

/// <summary>
/// A laid-out field: where it ended up, what it says, and whether it is drawn.
/// </summary>
public sealed class FormItem
{
    public int XRelative { get; set; }

    public int YRelative { get; set; }

    public int FieldId { get; set; }

    public short X { get; set; }

    public short Y { get; set; }

    public short Column { get; set; }

    public short Space { get; set; }

    /// <summary>Filled in by <see cref="TextForm.SetText"/>; -1 until then.</summary>
    public int Left { get; set; } = -1;

    /// <inheritdoc cref="Left"/>
    public int Right { get; set; } = -1;

    /// <inheritdoc cref="Left"/>
    public int Top { get; set; } = -1;

    /// <inheritdoc cref="Left"/>
    public int Bottom { get; set; } = -1;

    public int Width { get; set; }

    public string Text { get; set; } = string.Empty;

    public FontColor Color { get; set; } = FontColor.Black;

    public bool Highlight { get; set; }

    /// <summary>False hides the field without removing it from the layout.</summary>
    public bool Show { get; set; }

    public void Clear()
    {
        Left = Right = Top = Bottom = -1;
        Width = 0;
        Text = string.Empty;
        Color = FontColor.Black;
        Highlight = false;
        Show = true;
    }
}

/// <summary>
/// The engine behind every one of the game's forms (<c>TEXT_FORM</c>,
/// <c>UAFWin/TextForm.cpp:49</c>): relative layout, column alignment, tab order and mouse
/// hit-testing over a table of fields.
/// </summary>
/// <remarks>
/// <para>
/// A form is a <b>table</b>, not code: <c>CharStatsForm</c>, <c>ItemsForm</c>, <c>SpellForm</c> and
/// <c>RestTimeForm</c> each declare an array of <see cref="FormField"/> and then only fill values
/// in. Porting this one class is what makes those four layout data rather than drawing code.
/// </para>
/// <para>
/// <b>Placement is relative to other fields, resolved in table order.</b> A field whose
/// <c>XRelative</c> names another field is positioned from that field's already-computed box, so
/// the table's order is load-bearing — the reference asserts the target already has a placement.
/// This port throws instead, because a silently unplaced field lands at 0,0 and looks like a
/// layout bug rather than a table-ordering one.
/// </para>
/// <para>
/// <b><see cref="FormFlags.AutoRepeat"/> is tested with <c>==</c>, not <c>&amp;</c></b>
/// (<c>TextForm.cpp:58</c>), which is why it can share bits with <see cref="FormFlags.End"/> and
/// <see cref="FormFlags.Sel"/> without ambiguity. An auto-repeat row is not a field: its
/// <c>Y</c> is the number of rows to generate and its <c>X</c> is how many of the following fields
/// belong to each row. Generated rows get <see cref="FormFlags.RepeatIncrement"/> added to their
/// ids once per row, and every row after the first is placed below the one above it.
/// </para>
/// </remarks>
public sealed class TextForm
{
    private readonly FormItem[] items;

    /// <summary>Builds a form from its layout table.</summary>
    public TextForm(IReadOnlyList<FormField> layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        items = Expand(layout);
        ClearForm();
    }

    public IReadOnlyList<FormItem> Items => items;

    public int Count => items.Length;

    /// <summary>Expands auto-repeat blocks into real fields.</summary>
    private static FormItem[] Expand(IReadOnlyList<FormField> layout)
    {
        var built = new List<FormItem>(layout.Count);

        for (int i = 0; i < layout.Count; i++)
        {
            var spec = layout[i];
            if (spec.FieldId == 0)
            {
                break;      // the reference terminates its tables with a zero id
            }

            if (spec.XRelative != (int)FormFlags.AutoRepeat)
            {
                built.Add(FromSpec(spec));
                continue;
            }

            int rows = spec.Y;
            int perRow = spec.X;

            for (int row = 0; row < rows; row++)
            {
                for (int item = 0; item < perRow; item++)
                {
                    var source = layout[i + 1 + item];
                    var generated = FromSpec(source);
                    generated.FieldId += (int)FormFlags.RepeatIncrement * row;

                    if ((source.XRelative & (int)FormFlags.Sel) != 0)
                    {
                        // A selection box spans its row: left from the row's first field, top from
                        // the field immediately above it.
                        generated.XRelative = built[^(perRow - 1)].FieldId;
                        generated.YRelative = built[^1].FieldId;
                    }
                    else if (row > 0)
                    {
                        // Later rows sit under the same field one row up; only the first row uses
                        // the coordinates as written.
                        generated.Y = 0;
                        generated.YRelative =
                            built[^perRow].FieldId + (int)FormFlags.End;
                    }

                    built.Add(generated);
                }
            }

            i += perRow;    // step over the fields the block consumed
        }

        return [.. built];
    }

    private static FormItem FromSpec(FormField spec) => new()
    {
        XRelative = spec.XRelative,
        YRelative = spec.YRelative,
        FieldId = spec.FieldId,
        X = spec.X,
        Y = spec.Y,
        Column = spec.Column,
        Space = spec.Space,
    };

    /// <summary>Resets every field's placement, text and colour.</summary>
    public void ClearForm()
    {
        foreach (var item in items)
        {
            item.Clear();
        }
    }

    /// <summary>The field with this exact id, or null.</summary>
    public FormItem? Field(int fieldId)
    {
        foreach (var item in items)
        {
            if (item.FieldId == fieldId)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>Moves a field, before it is placed.</summary>
    public void SetXY(int fieldId, short x, short y)
    {
        if (Field(fieldId) is { } item)
        {
            item.X = x;
            item.Y = y;
        }
    }

    public void SetHighlight(int fieldId, bool highlight = true)
    {
        if (Field(fieldId) is { } item)
        {
            item.Highlight = highlight;
        }
    }

    public void EnableItem(int fieldId, bool enable = true)
    {
        if (Field(fieldId) is { } item)
        {
            item.Show = enable;
        }
    }

    /// <summary>
    /// Gives a field its text and places it, returning the box it occupies.
    /// </summary>
    /// <remarks>
    /// <b>Placement happens here, not at construction</b>, because a field's box depends on how wide
    /// its text turns out to be — which is also why fields must be filled in table order when they
    /// are placed relative to one another.
    /// </remarks>
    public SurfaceRect SetText(int fieldId, string? text, BitmapFont font,
                               FontColor color = FontColor.Black)
    {
        ArgumentNullException.ThrowIfNull(font);

        var item = Field(fieldId)
            ?? throw new ArgumentException($"no form field with id 0x{fieldId:x8}", nameof(fieldId));

        string value = text ?? string.Empty;
        int width = font.GetTextWidth(value);

        int relativeX = 0;
        if ((item.XRelative & (int)FormFlags.RightJust) != 0)
        {
            relativeX = -width;
        }
        else if (item.XRelative != 0)
        {
            var anchor = Anchor(item.XRelative, fieldId);
            if ((item.XRelative & (int)FormFlags.Sel) != 0)
            {
                item.Top = anchor.Top;
                item.Left = anchor.Left;
            }
            else if ((item.XRelative & (int)FormFlags.End) != 0)
            {
                relativeX = anchor.Right;
            }
            else if ((item.XRelative & (int)FormFlags.Right) != 0)
            {
                relativeX = anchor.Right - width;
            }
            else
            {
                relativeX = anchor.Left;
            }
        }

        int relativeY = 0;
        if (item.YRelative != 0)
        {
            var anchor = Anchor(item.YRelative, fieldId);
            if ((item.YRelative & (int)FormFlags.Sel) != 0)
            {
                item.Bottom = anchor.Bottom;
                item.Right = anchor.Right;
            }
            else if ((item.YRelative & (int)FormFlags.End) != 0)
            {
                relativeY = anchor.Bottom;
            }
            else
            {
                relativeY = anchor.Top;
            }
        }

        if ((item.XRelative & (int)FormFlags.Sel) == 0)
        {
            item.Left = item.X + relativeX;
        }

        if ((item.YRelative & (int)FormFlags.Sel) == 0)
        {
            item.Top = item.Y + relativeY;
        }

        // A colour given at the call site wins; otherwise the field id's own colour bits decide,
        // and anything else is white.
        item.Color = color != FontColor.Black
            ? color
            : (FormFlags)(item.FieldId & (int)(FormFlags.White | FormFlags.Green)) switch
            {
                FormFlags.Green => FontColor.Green,
                _ => FontColor.White,
            };

        item.Text = value;
        item.Width = width;

        if ((item.XRelative & (int)FormFlags.Sel) == 0)
        {
            item.Right = item.Left + width;
        }

        if ((item.YRelative & (int)FormFlags.Sel) == 0)
        {
            // Height comes from a capital H, as the reference does -- a per-string height would
            // make rows of different text sit at different heights.
            item.Bottom = item.Top + font.GetCharacterHeight((byte)'H');
        }

        return new SurfaceRect(item.Left, item.Top, item.Right, item.Bottom);
    }

    /// <summary>The already-placed field another field is positioned against.</summary>
    private FormItem Anchor(int relative, int fieldId)
    {
        int target = relative & FormField.NumberMask;
        foreach (var candidate in items)
        {
            if (candidate.FieldId == target)
            {
                if (candidate.Left < 0 || candidate.Top < 0)
                {
                    throw new InvalidOperationException(
                        $"field 0x{fieldId:x8} is placed relative to 0x{target:x8}, which has no "
                        + "placement yet -- fill fields in table order.");
                }
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"field 0x{fieldId:x8} is placed relative to 0x{target:x8}, which is not in this form.");
    }

    /// <summary>The next tab stop after <paramref name="current"/>, or -1 when there is none.</summary>
    /// <remarks>
    /// Wraps, and returns <paramref name="current"/> unchanged when it is the only tab stop, so a
    /// one-stop form does not cycle forever.
    /// </remarks>
    public int Tab(int current)
    {
        if (items.Length == 0)
        {
            return -1;
        }

        int i;
        if (current == -1)
        {
            i = 0;
        }
        else
        {
            for (i = 0; i < items.Length; i++)
            {
                if (items[i].FieldId == current)
                {
                    break;
                }
            }

            if (i == items.Length)
            {
                return -1;
            }
            i++;
        }

        while (i >= items.Length || (items[i].FieldId & (int)FormFlags.Tab) == 0)
        {
            i++;
            if (i >= items.Length)
            {
                if (current == -1)
                {
                    return -1;
                }
                i = 0;
            }

            if (items[i].FieldId == current)
            {
                return current;
            }
        }

        return items[i].FieldId;
    }

    /// <summary>The first selectable field, or -1.</summary>
    public int FirstSelectable()
    {
        foreach (var item in items)
        {
            if ((item.XRelative & (int)FormFlags.Sel) != 0)
            {
                return item.FieldId;
            }
        }
        return -1;
    }

    /// <summary>
    /// The field under a point, or -1.
    /// </summary>
    /// <remarks>
    /// <b>Returns the largest hit by area, not the first.</b> Selection boxes overlap the text they
    /// contain, and taking the first match would return the label rather than the row the player
    /// meant to click.
    /// </remarks>
    public int MouseClick(int x, int y)
    {
        int selection = -1;
        int area = 0;

        foreach (var item in items)
        {
            if (x < item.Left || x > item.Right || y < item.Top || y > item.Bottom)
            {
                continue;
            }

            int candidate = (item.Right - item.Left) * (item.Bottom - item.Top);
            if (candidate > area)
            {
                selection = item.FieldId;
                area = candidate;
            }
        }

        return selection;
    }

    /// <summary>The highest column number this form uses.</summary>
    private const int MaxColumn = 30;

    /// <summary>
    /// How far right each column must move so that every column clears the ones before it.
    /// </summary>
    /// <remarks>
    /// This is what lets a table of variable-width text line up without anyone measuring it in
    /// advance: a column's start is pushed until it is past the widest end of every earlier column
    /// plus its own required space.
    /// </remarks>
    public int[] ColumnAdjustments()
    {
        var starts = new int[MaxColumn];
        var ends = new int[MaxColumn];
        var spaces = new int[MaxColumn];
        var adjustments = new int[MaxColumn];
        Array.Fill(starts, int.MaxValue);

        int highest = 0;
        foreach (var item in items)
        {
            if (item.Column <= 0 || item.Column >= MaxColumn)
            {
                continue;
            }

            highest = Math.Max(highest, item.Column);
            starts[item.Column] = Math.Min(starts[item.Column], item.Left);
            ends[item.Column] = Math.Max(ends[item.Column], item.Right);
            spaces[item.Column] = Math.Max(spaces[item.Column], item.Space);
        }

        for (int i = 0; i <= highest; i++)
        {
            for (int j = 0; j < i; j++)
            {
                int adjust = ends[j] + spaces[i] + adjustments[j] - starts[i];
                if (adjust > adjustments[i])
                {
                    adjustments[i] = adjust;
                }
            }
        }

        return adjustments;
    }

    /// <summary>
    /// Draws every visible field.
    /// </summary>
    /// <remarks>
    /// Empty text, a colour of <see cref="FontColor.Black"/> (the reference's "illegal colour"
    /// sentinel) and a hidden field are all skipped. Markup is not interpreted — the original
    /// disables font colour tags for the duration, exactly as the menu does, so a value containing
    /// <c>/R</c> draws those two characters.
    /// </remarks>
    public void Display(Surface destination, BitmapFont font, FontColor[]? palette = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(font);

        var adjustments = ColumnAdjustments();

        foreach (var item in items)
        {
            if (item.Text.Length == 0 || !item.Show || item.Color == FontColor.Black)
            {
                continue;
            }

            int column = item.Column > 0 && item.Column < MaxColumn ? item.Column : 0;
            font.Draw(destination, item.Left + adjustments[column], item.Top, item.Text);
        }
    }
}
