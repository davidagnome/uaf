namespace UAF.Media;

/// <summary>
/// Everything the character sheet shows, already rendered to strings.
/// </summary>
/// <remarks>
/// Strings rather than a character type, so the form stays in <c>UAF.Media</c> and the caller keeps
/// the formatting decisions — how a level is worded, how a coin is named, whether a score carries
/// an adjustment. An empty string leaves the field blank.
/// </remarks>
/// <param name="ExperienceLines">
/// Up to three baseclass lines, each already worded like <c>"FIGHTER 25460"</c>. A multiclass
/// character fills more than one.
/// </param>
/// <param name="Coins">Ten denominations in order; an entry with a null amount is left off.</param>
public sealed record CharacterSheet(
    string Name, string Gender, string Age, string Status, string Alignment, string Race,
    string Class, string Level, string Hits, string MaxHits,
    IReadOnlyList<string> ExperienceLines,
    IReadOnlyList<string> Abilities,
    IReadOnlyList<ItemsFormCoin> Coins,
    string Available = "",
    string ArmorClass = "", string Thac0 = "", string Damage = "",
    string Weapon = "", string Armor = "", string Encumbrance = "", string Movement = "");

/// <summary>Field ids for <see cref="CharStatsForm"/> (<c>enum ST_FORM</c>).</summary>
/// <remarks>
/// <b>Three colour groups, set by re-assigning the enum mid-list.</b> The C++ writes
/// <c>STF_white = TEXT_FORM::white</c>, later <c>STF_green = TEXT_FORM::green</c>, and later still
/// <c>STF_Str = green + tab</c> — each restarting the count from that flag, so a field's colour and
/// its tab-stop-ness are carried in its id. This is the same trick <see cref="ItemsFormFields"/>
/// uses once, done three times.
/// <para>
/// Note which fields land in which group: the coin <i>labels</i> are white while the coin
/// <i>amounts</i> are green, and both the ability labels and their values are green.
/// </para>
/// </remarks>
public static class CharStatsFields
{
    private const int W = (int)FormFlags.White;
    private const int G = (int)FormFlags.Green;
    private const int S = (int)FormFlags.Green | (int)FormFlags.Tab;

    // White group.
    public const int StatusLabel = W + 1;
    public const int Gender = W + 2;
    public const int Name = W + 3;
    public const int Age = W + 4;
    public const int HitsLabel = W + 5;
    public const int MaxHits = W + 6;
    public const int Alignment = W + 7;
    public const int Race = W + 8;
    public const int Class = W + 9;
    public const int Exp1 = W + 10;
    public const int Exp2 = W + 11;
    public const int Exp3 = W + 12;
    public const int ExpValue = W + 13;
    public const int Level = W + 14;
    public const int ArmorClassLabel = W + 25;
    public const int Thac0Label = W + 26;
    public const int DamageLabel = W + 27;
    public const int Armor = W + 28;
    public const int Weapon = W + 29;
    public const int EncumbranceLabel = W + 30;
    public const int MovementLabel = W + 31;

    /// <summary>The ten coin labels (white), in denomination order.</summary>
    public static readonly int[] CoinLabels =
        [W + 15, W + 16, W + 17, W + 18, W + 19, W + 20, W + 21, W + 22, W + 23, W + 24];

    // Green group.
    public const int AvailableLabel = G + 1;
    public const int Available = G + 2;
    public const int Status = G + 3;
    public const int Hits = G + 4;
    public const int ArmorClass = G + 15;
    public const int Thac0 = G + 16;
    public const int Damage = G + 17;
    public const int Encumbrance = G + 18;
    public const int Movement = G + 19;

    /// <summary>The ten coin amounts (green).</summary>
    public static readonly int[] CoinAmounts =
        [G + 5, G + 6, G + 7, G + 8, G + 9, G + 10, G + 11, G + 12, G + 13, G + 14];

    /// <summary>The six ability labels — green, like their values.</summary>
    public static readonly int[] AbilityLabels =
        [G + 20, G + 22, G + 24, G + 26, G + 28, G + 30];

    /// <summary>The six ability values.</summary>
    public static readonly int[] AbilityValues =
        [G + 21, G + 23, G + 25, G + 27, G + 29, G + 31];

    /// <summary>The six tab stops, one per ability. Never placed — see <see cref="CharStatsForm"/>.</summary>
    public static readonly int[] AbilityStops = [S, S + 1, S + 2, S + 3, S + 4, S + 5];

    /// <summary>The ability names, in the order the sheet lists them.</summary>
    public static readonly string[] AbilityNames = ["STR", "INT", "WIS", "DEX", "CON", "CHA"];
}

/// <summary>
/// The character sheet (<c>CharStatsForm.cpp</c>) — the biggest of the game's forms, and what the
/// treasure screen's VIEW entry opens.
/// </summary>
/// <remarks>
/// <para>
/// <b>The layout is ported in full; the population is not.</b> <c>showCharStats</c> is 1,083 lines
/// because it derives most of the lower half — armour class, THAC0, damage, encumbrance and
/// movement all come from <c>GameRules.cpp</c>, which this port has not reached. Those fields are
/// laid out and left blank rather than filled with plausible numbers, and
/// <see cref="CharacterSheet"/> accepts them so they can be filled in without touching this class.
/// </para>
/// <para>
/// <b>The six ability selection fields are never drawn</b>, the same as in
/// <see cref="RestTimeForm"/> — they name the tab stops and the highlight goes on the value beside
/// each. Here they at least would work as rectangles if they were placed, since nothing flattens
/// their flags; <c>showCharStats</c> simply never gives them text.
/// </para>
/// </remarks>
public sealed class CharStatsForm
{
    /// <summary>The two columns the sheet is built around.</summary>
    private const short LeftX = 18;
    private const short RightX = 220;

    private readonly TextForm form;

    public CharStatsForm()
    {
        form = new TextForm(Layout());
    }

    public TextForm Form => form;

    /// <summary>The ability being adjusted during character creation, or -1.</summary>
    public int Selection { get; private set; } = -1;

    /// <summary>Transcribed from <c>statsForm[]</c> (<c>CharStatsForm.cpp:174</c>).</summary>
    public static List<FormField> Layout()
    {
        int end = (int)FormFlags.End;
        int right = (int)FormFlags.Right;
        int sel = (int)FormFlags.Sel;
        var f = CharStatsFields.AbilityLabels;
        var v = CharStatsFields.AbilityValues;
        var stops = CharStatsFields.AbilityStops;

        var rows = new List<FormField>
        {
            new(0, 0, CharStatsFields.StatusLabel, RightX, 35),
            new(CharStatsFields.StatusLabel | end, CharStatsFields.StatusLabel,
                CharStatsFields.Status, 16, 0),
            new(0, 0, CharStatsFields.Name, LeftX, 35),
            new(CharStatsFields.Name, CharStatsFields.Name, CharStatsFields.Gender, 0, 20),
            new(CharStatsFields.Gender, CharStatsFields.Gender, CharStatsFields.Age, 100, 0),
            new(CharStatsFields.StatusLabel, CharStatsFields.StatusLabel,
                CharStatsFields.HitsLabel, 0, 20),
            new(CharStatsFields.HitsLabel | end, CharStatsFields.HitsLabel,
                CharStatsFields.Hits, 16, 0),
            new(CharStatsFields.Hits | end, CharStatsFields.HitsLabel,
                CharStatsFields.MaxHits, 1, 0),
            new(CharStatsFields.Name, CharStatsFields.Age, CharStatsFields.Alignment, 0, 20),
            new(CharStatsFields.HitsLabel, CharStatsFields.HitsLabel, CharStatsFields.Race, 0, 20),
            new(CharStatsFields.Name, CharStatsFields.Alignment, CharStatsFields.Class, 0, 20),
            new(CharStatsFields.Name, CharStatsFields.Class, CharStatsFields.Exp1, 0, 20),
            new(0, CharStatsFields.Exp1, CharStatsFields.Exp2, LeftX, 20),
            new(0, CharStatsFields.Exp2, CharStatsFields.Exp3, LeftX, 20),
            new(CharStatsFields.Exp1 | end, CharStatsFields.Exp1, CharStatsFields.ExpValue, 16, 0),
            new(CharStatsFields.Name, CharStatsFields.Exp1, CharStatsFields.Level, 0, 20),
        };

        // The six ability rows: a label, a value 35 to its right, and an unplaced selection field
        // spanning both. The first is absolute at y=172; the rest hang 20 below the one above.
        for (int i = 0; i < 6; i++)
        {
            rows.Add(i == 0
                ? new FormField(0, 0, f[0], LeftX, 172)
                : new FormField(f[i - 1], f[i - 1], f[i], 0, 20));

            rows.Add(new FormField(f[i], f[i], v[i], 35, 0));
            rows.Add(new FormField(f[i] | sel, v[i] | sel, stops[i], 0, 0));
        }

        rows.Add(new FormField(f[5], f[5], CharStatsFields.AvailableLabel, 0, 20));
        rows.Add(new FormField(v[5], CharStatsFields.AvailableLabel, CharStatsFields.Available,
                               0, 0));

        // The money block, right column. Every amount is right-aligned against the first one, so
        // the numbers line up on their right edge however wide they get.
        rows.Add(new FormField(0, 0, CharStatsFields.CoinLabels[0], RightX, 135));
        rows.Add(new FormField(CharStatsFields.CoinLabels[0] | end, CharStatsFields.CoinLabels[0],
                               CharStatsFields.CoinAmounts[0], 50, 0));
        for (int c = 1; c < 10; c++)
        {
            rows.Add(new FormField(CharStatsFields.CoinLabels[c - 1],
                                   CharStatsFields.CoinLabels[c - 1],
                                   CharStatsFields.CoinLabels[c], 0, 20));
            rows.Add(new FormField(CharStatsFields.CoinAmounts[0] | right,
                                   CharStatsFields.CoinLabels[c],
                                   CharStatsFields.CoinAmounts[c], 0, 0));
        }

        // The combat block, bottom left.
        rows.Add(new FormField(0, 0, CharStatsFields.ArmorClassLabel, LeftX, 325));
        rows.Add(new FormField(CharStatsFields.ArmorClassLabel | end,
                               CharStatsFields.ArmorClassLabel, CharStatsFields.ArmorClass, 0, 0));
        rows.Add(new FormField(CharStatsFields.ArmorClassLabel, CharStatsFields.ArmorClassLabel,
                               CharStatsFields.Thac0Label, 0, 20));
        rows.Add(new FormField(CharStatsFields.Thac0Label | end, CharStatsFields.Thac0Label,
                               CharStatsFields.Thac0, 0, 0));
        rows.Add(new FormField(CharStatsFields.Thac0Label, CharStatsFields.Thac0Label,
                               CharStatsFields.DamageLabel, 0, 20));
        rows.Add(new FormField(CharStatsFields.DamageLabel | end, CharStatsFields.DamageLabel,
                               CharStatsFields.Damage, 20, 0));
        rows.Add(new FormField(CharStatsFields.DamageLabel, CharStatsFields.DamageLabel,
                               CharStatsFields.Weapon, 0, 20));
        rows.Add(new FormField(CharStatsFields.Weapon, CharStatsFields.Weapon,
                               CharStatsFields.Armor, 0, 20));

        // ...and the load block, bottom right.
        rows.Add(new FormField(0, 0, CharStatsFields.EncumbranceLabel, RightX, 325));
        rows.Add(new FormField(CharStatsFields.EncumbranceLabel | end,
                               CharStatsFields.EncumbranceLabel, CharStatsFields.Encumbrance,
                               20, 0));
        rows.Add(new FormField(CharStatsFields.EncumbranceLabel, CharStatsFields.EncumbranceLabel,
                               CharStatsFields.MovementLabel, 0, 20));
        rows.Add(new FormField(CharStatsFields.Encumbrance, CharStatsFields.MovementLabel,
                               CharStatsFields.Movement, 0, 0));

        return rows;
    }

    /// <summary>Fills the sheet in.</summary>
    public void Populate(BitmapFont font, CharacterSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(sheet);

        form.ClearForm();

        form.SetText(CharStatsFields.StatusLabel, "STATUS", font);
        form.SetText(CharStatsFields.Status, sheet.Status, font);
        form.SetText(CharStatsFields.Name, sheet.Name, font);
        form.SetText(CharStatsFields.Gender, sheet.Gender, font);
        form.SetText(CharStatsFields.Age, sheet.Age, font);
        form.SetText(CharStatsFields.HitsLabel, "HIT POINTS", font);
        form.SetText(CharStatsFields.Hits, sheet.Hits, font);
        form.SetText(CharStatsFields.MaxHits, sheet.MaxHits, font);
        form.SetText(CharStatsFields.Alignment, sheet.Alignment, font);
        form.SetText(CharStatsFields.Race, sheet.Race, font);
        form.SetText(CharStatsFields.Class, sheet.Class, font);

        // Three baseclass lines, one per class a multiclass character advances in.
        int[] expFields =
            [CharStatsFields.Exp1, CharStatsFields.Exp2, CharStatsFields.Exp3];
        for (int i = 0; i < expFields.Length; i++)
        {
            form.SetText(expFields[i],
                         i < sheet.ExperienceLines.Count ? sheet.ExperienceLines[i] : "", font);
        }

        form.SetText(CharStatsFields.ExpValue, "", font);
        form.SetText(CharStatsFields.Level, sheet.Level, font);

        for (int i = 0; i < 6; i++)
        {
            form.SetText(CharStatsFields.AbilityLabels[i], CharStatsFields.AbilityNames[i], font);
            form.SetText(CharStatsFields.AbilityValues[i],
                         i < sheet.Abilities.Count ? sheet.Abilities[i] : "", font);
        }

        form.SetText(CharStatsFields.AvailableLabel, sheet.Available.Length > 0 ? "AVAIL" : "",
                     font);
        form.SetText(CharStatsFields.Available, sheet.Available, font);

        for (int c = 0; c < 10; c++)
        {
            var coin = c < sheet.Coins.Count ? sheet.Coins[c] : default;
            bool active = coin.Amount is not null;
            form.SetText(CharStatsFields.CoinLabels[c], active ? coin.Name : "", font);
            form.SetText(CharStatsFields.CoinAmounts[c], active ? coin.Amount! : "", font);
        }

        // The combat and load blocks. Labels always, values only when the caller has them --
        // see this class's remarks on why they are usually empty.
        form.SetText(CharStatsFields.ArmorClassLabel, "ARMOR CLASS", font);
        form.SetText(CharStatsFields.ArmorClass, sheet.ArmorClass, font);
        form.SetText(CharStatsFields.Thac0Label, "THAC0", font);
        form.SetText(CharStatsFields.Thac0, sheet.Thac0, font);
        form.SetText(CharStatsFields.DamageLabel, "DAMAGE", font);
        form.SetText(CharStatsFields.Damage, sheet.Damage, font);
        form.SetText(CharStatsFields.Weapon, sheet.Weapon, font);
        form.SetText(CharStatsFields.Armor, sheet.Armor, font);
        form.SetText(CharStatsFields.EncumbranceLabel, "ENCUMBRANCE", font);
        form.SetText(CharStatsFields.Encumbrance, sheet.Encumbrance, font);
        form.SetText(CharStatsFields.MovementLabel, "MOVEMENT", font);
        form.SetText(CharStatsFields.Movement, sheet.Movement, font);

        ApplyHighlight();
    }

    /// <summary>Selects an ability by index, for the creation screen's score distribution.</summary>
    public void Select(int ability)
    {
        Selection = ability >= 0 && ability < 6 ? ability : -1;
        ApplyHighlight();
    }

    /// <summary>Moves to the next ability, wrapping.</summary>
    public void Tab() => Select(Selection < 0 ? 0 : (Selection + 1) % 6);

    private void ApplyHighlight()
    {
        for (int i = 0; i < 6; i++)
        {
            form.SetHighlight(CharStatsFields.AbilityValues[i], i == Selection);
        }
    }

    public void Display(Surface destination, BitmapFont font) => form.Display(destination, font);
}
