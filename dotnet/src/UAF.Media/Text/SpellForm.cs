namespace UAF.Media;

/// <summary>One row of a spell list, already rendered to strings.</summary>
/// <param name="Selected">Usually "Yes" or blank.</param>
/// <param name="Memorized">How many copies are memorised.</param>
public readonly record struct SpellFormRow(string Level, string Selected, string Memorized,
                                           string Cost, string Name);

/// <summary>Field ids for <see cref="SpellForm"/> (<c>enum ST_SPELLFORM</c>).</summary>
public static class SpellFormFields
{
    private const int W = (int)FormFlags.White;

    public const int SchoolLabel = W + 1;
    public const int LevelLabel = W + 2;
    public const int SelectLabel = W + 3;
    public const int MemorizeLabel = W + 4;
    public const int AvailableLabel = W + 5;
    public const int CostLabel = W + 6;
    public const int NameLabel = W + 7;

    /// <summary>
    /// The seven "spells still available" labels, in the enum's order: magic user, cleric, thief,
    /// fighter, paladin, ranger, druid.
    /// </summary>
    public static readonly int[] ClassLabels =
        [W + 8, W + 10, W + 12, W + 14, W + 16, W + 18, W + 20];

    /// <summary>Their seven values.</summary>
    public static readonly int[] ClassValues =
        [W + 9, W + 11, W + 13, W + 15, W + 17, W + 19, W + 21];

    public const int Level = W + 28;
    public const int Select = W + 29;
    public const int Memorize = W + 30;
    public const int Cost = W + 32;
    public const int Name = W + 33;

    /// <summary>The id offset applied to row <paramref name="index"/>'s fields.</summary>
    public static int RowOffset(int index) => (int)FormFlags.RepeatIncrement * index;
}

/// <summary>
/// The spell list (<c>SpellForm.cpp</c>) — memorising, casting and shopping all use it.
/// </summary>
/// <remarks>
/// <para>
/// Structurally the closest relative of <see cref="ItemsForm"/>: column headers, an auto-repeat
/// block of five fields per row, and a side block of counts. It has no money block; what sits
/// alongside instead is <b>spells still available per class</b>.
/// </para>
/// <para>
/// <b>Two pairs of classes deliberately share a row.</b> Ranger hangs off <c>FIGHTERAVAIL+END</c> —
/// the same anchor as paladin — and druid off <c>CLERICAVAIL+END</c>, the same as thief. They share
/// a row and a <i>right edge</i> rather than a left one, since <see cref="FormFlags.Right"/>
/// right-aligns: PALADIN and RANGER end flush and begin a letter apart.
/// The reference's own comment says they were "moved up from bottom to avoid being
/// displayed over border graphics". Since no character is both a paladin and a ranger, or both a
/// thief and a druid, only one of each pair is ever filled in, and the overlap never shows. Laying
/// them out on separate rows would be tidier and would not match.
/// </para>
/// </remarks>
public sealed class SpellForm
{
    private const short LabelsX = 18;
    private const short LabelsY = 18;
    private const short ItemsY = 36;

    /// <summary>Where the availability block starts. The reference computes this from the text-box
    /// width, which varies with resolution; the caller supplies it for the same reason.</summary>
    private readonly short availableX;
    private readonly short availableY;

    private readonly TextForm form;

    public SpellForm(int pageSize, int availableX = 470, int availableY = 40)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        PageSize = pageSize;
        this.availableX = (short)availableX;
        this.availableY = (short)availableY;
        form = new TextForm(Layout(pageSize, this.availableX, this.availableY));
    }

    public int PageSize { get; }

    public TextForm Form => form;

    /// <summary>The selected row, or -1.</summary>
    public int Selection { get; private set; } = -1;

    /// <summary>The class names the availability block labels, in field order.</summary>
    public static readonly string[] ClassNames =
        ["MAGIC USER", "CLERIC", "THIEF", "FIGHTER", "PALADIN", "RANGER", "DRUID"];

    /// <summary>Transcribed from <c>initSpellForms</c> (<c>SpellForm.cpp:115</c>).</summary>
    public static List<FormField> Layout(int pageSize, short availableX, short availableY)
    {
        int end = (int)FormFlags.End;
        int right = (int)FormFlags.Right;
        int rightJust = (int)FormFlags.RightJust;
        var labels = SpellFormFields.ClassLabels;
        var values = SpellFormFields.ClassValues;

        var f = new List<FormField>
        {
            new(0, 0, SpellFormFields.LevelLabel, LabelsX, LabelsY),
            new(SpellFormFields.LevelLabel | end, 0, SpellFormFields.SelectLabel, 5, LabelsY),
            new(SpellFormFields.SelectLabel | end, 0, SpellFormFields.MemorizeLabel, 5, LabelsY),
            new(SpellFormFields.MemorizeLabel | end, 0, SpellFormFields.CostLabel, 5, LabelsY),
            new(SpellFormFields.CostLabel | end, 0, SpellFormFields.NameLabel, 40, LabelsY),

            // The magic-user row anchors the block: right-justified at an absolute x, with every
            // other label right-aligned against it so the column ends flush.
            new(rightJust, 0, labels[0], availableX, availableY),
            new(labels[0] | end, labels[0], values[0], 0, 0),
        };

        // Cleric, thief, fighter, paladin, ranger, druid. The anchors are not a simple chain --
        // see this class's remarks on the two shared rows.
        int[] rowAnchors =
        [
            labels[0],   // cleric under magic user
            labels[1],   // thief under cleric
            labels[2],   // fighter under thief
            labels[3],   // paladin under fighter
            labels[3],   // ranger ALSO under fighter
            labels[1],   // druid ALSO under cleric
        ];

        for (int i = 1; i < labels.Length; i++)
        {
            f.Add(new FormField(labels[0] | right, rowAnchors[i - 1] | end, labels[i], 0, 0));
            f.Add(new FormField(values[0], labels[i], values[i], 0, 0));
        }

        // Five fields per row, repeated down the page.
        f.Add(new FormField((int)FormFlags.AutoRepeat, 0, SpellFormFields.Name + 1,
                            5, (short)pageSize));

        f.Add(new FormField(SpellFormFields.LevelLabel | right, 0, SpellFormFields.Level, 0, ItemsY));
        f.Add(new FormField(SpellFormFields.SelectLabel | right, 0, SpellFormFields.Select,
                            0, ItemsY));
        f.Add(new FormField(SpellFormFields.MemorizeLabel | right, 0, SpellFormFields.Memorize,
                            0, ItemsY));
        f.Add(new FormField(SpellFormFields.CostLabel | right, 0, SpellFormFields.Cost, 0, ItemsY));
        f.Add(new FormField(SpellFormFields.NameLabel, 0, SpellFormFields.Name, 0, ItemsY));

        return f;
    }

    /// <summary>
    /// Fills the list in.
    /// </summary>
    /// <param name="available">
    /// Spells still available per class, in <see cref="ClassNames"/> order. A null entry leaves that
    /// class off, which is the normal case — a character belongs to one or two.
    /// </param>
    public void Populate(BitmapFont font, IReadOnlyList<SpellFormRow> rows,
                         bool useSelect = true, bool useMemorize = true, bool useCost = false,
                         IReadOnlyList<string?>? available = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(rows);

        form.ClearForm();

        // Blanked rather than removed, as in ItemsForm: the name column is placed relative to the
        // cost column, so dropping a header would move every spell name.
        form.SetText(SpellFormFields.LevelLabel, "LEVEL", font);
        form.SetText(SpellFormFields.SelectLabel, useSelect ? "SELECTED" : "", font);
        form.SetText(SpellFormFields.MemorizeLabel, useMemorize ? "MEMORIZED" : "", font);
        form.SetText(SpellFormFields.CostLabel, useCost ? "COST" : "", font);
        form.SetText(SpellFormFields.NameLabel, "SPELL", font);

        for (int c = 0; c < SpellFormFields.ClassLabels.Length; c++)
        {
            string? count = available is not null && c < available.Count ? available[c] : null;
            form.SetText(SpellFormFields.ClassLabels[c], count is null ? "" : ClassNames[c], font);
            form.SetText(SpellFormFields.ClassValues[c], count ?? "", font);
        }

        for (int i = 0; i < PageSize; i++)
        {
            int offset = SpellFormFields.RowOffset(i);
            var row = i < rows.Count ? rows[i] : default;

            form.SetText(SpellFormFields.Level + offset, row.Level ?? "", font);
            form.SetText(SpellFormFields.Select + offset, row.Selected ?? "", font);
            form.SetText(SpellFormFields.Memorize + offset, row.Memorized ?? "", font);
            form.SetText(SpellFormFields.Cost + offset, row.Cost ?? "", font);
            form.SetText(SpellFormFields.Name + offset, row.Name ?? "", font);
        }

        ApplyHighlight();
    }

    /// <summary>Selects a row, highlighting its five columns.</summary>
    public void Select(int row)
    {
        Selection = row >= 0 && row < PageSize ? row : -1;
        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        for (int i = 0; i < PageSize; i++)
        {
            int offset = SpellFormFields.RowOffset(i);
            bool on = i == Selection;

            form.SetHighlight(SpellFormFields.Level + offset, on);
            form.SetHighlight(SpellFormFields.Select + offset, on);
            form.SetHighlight(SpellFormFields.Memorize + offset, on);
            form.SetHighlight(SpellFormFields.Cost + offset, on);
            form.SetHighlight(SpellFormFields.Name + offset, on);
        }
    }

    /// <summary>The row a click lands on, or -1.</summary>
    public int RowAt(int x, int y)
    {
        int field = form.MouseClick(x, y);
        if (field == -1)
        {
            return -1;
        }

        int row = (field & 0x00ff0000) / (int)FormFlags.RepeatIncrement;
        int baseField = field - SpellFormFields.RowOffset(row);

        bool isRowField = baseField == SpellFormFields.Level
                          || baseField == SpellFormFields.Select
                          || baseField == SpellFormFields.Memorize
                          || baseField == SpellFormFields.Cost
                          || baseField == SpellFormFields.Name;

        return isRowField && row < PageSize ? row : -1;
    }

    public void Display(Surface destination, BitmapFont font) => form.Display(destination, font);
}
