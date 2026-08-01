namespace UAF.Media;

/// <summary>Which part of the rest time the player is adjusting.</summary>
public enum RestField
{
    Days,
    Hours,
    Minutes,
}

/// <summary>
/// The rest-time picker (<c>RestTimeForm.cpp</c>) — <c>REST TIME  DD:HH:MM</c>.
/// </summary>
/// <remarks>
/// <para>
/// The smallest of the game's forms and a good contrast with <see cref="ItemsForm"/>: its three
/// <see cref="FormFlags.Sel"/> fields are <b>not</b> inside an auto-repeat block, so they keep
/// their flags rather than being flattened.
/// </para>
/// <para>
/// <b>They are still never drawn.</b> <c>showRestTime</c> gives text to the header, the three
/// numbers and the two colons, and nothing to <c>RTF_Days</c>, <c>RTF_Hours</c> or
/// <c>RTF_Minutes</c> — so those three are never placed and have no box. The enum comment calls
/// them a "selection rectangle"; in practice they are identity tokens for the tab order, and
/// highlighting is applied to the <i>number</i> field beside each one (<c>RTF_highlight</c>).
/// </para>
/// </remarks>
public sealed class RestTimeForm
{
    private const int White = (int)FormFlags.White;

    /// <summary>Green and a tab stop both, straight out of the C++ enum's initialiser.</summary>
    private const int Selectable = (int)FormFlags.Green | (int)FormFlags.Tab;

    public const int Header = White + 1;
    public const int DaysText = White + 2;
    public const int DaysColon = White + 3;
    public const int HoursText = White + 4;
    public const int HoursColon = White + 5;
    public const int MinutesText = White + 6;

    /// <summary>The three tab stops. Never placed — see this class's remarks.</summary>
    public const int DaysStop = Selectable;
    public const int HoursStop = Selectable + 1;
    public const int MinutesStop = Selectable + 2;

    private readonly TextForm form;

    public RestTimeForm(int x, int y)
    {
        form = new TextForm(Layout((short)x, (short)y));
        Selection = RestField.Days;
    }

    public TextForm Form => form;

    public long Days { get; private set; }

    public long Hours { get; private set; }

    public long Minutes { get; private set; }

    public RestField Selection { get; private set; }

    /// <summary>Transcribed from <c>GetRestForm</c> (<c>RestTimeForm.cpp:96</c>).</summary>
    public static List<FormField> Layout(short x, short y)
    {
        int end = (int)FormFlags.End;
        int sel = (int)FormFlags.Sel;

        return
        [
            new(0, 0, Header, x, y),
            new(Header | end, 0, DaysText, 48, y),
            new(DaysText | sel, DaysText | sel, DaysStop, 0, 0),
            new(DaysText | end, 0, DaysColon, 0, y),
            new(DaysColon | end, 0, HoursText, 0, y),
            new(HoursText | sel, HoursText | sel, HoursStop, 0, 0),
            new(HoursText | end, 0, HoursColon, 0, y),
            new(HoursColon | end, 0, MinutesText, 0, y),
            new(MinutesText | sel, MinutesText | sel, MinutesStop, 0, 0),
        ];
    }

    /// <summary>Sets the time and lays the form out.</summary>
    public void SetTime(BitmapFont font, long days, long hours, long minutes)
    {
        ArgumentNullException.ThrowIfNull(font);

        Days = Math.Max(days, 0);
        Hours = Math.Max(hours, 0);
        Minutes = Math.Max(minutes, 0);
        Refresh(font);
    }

    /// <summary>Re-renders the three numbers, keeping the current highlight.</summary>
    private void Refresh(BitmapFont font)
    {
        form.ClearForm();

        form.SetText(Header, "REST TIME", font);

        // Two digits, zero-padded, and no upper clamp: the reference formats %02I64i and lets days
        // run as high as the player cares to hold the key down.
        form.SetText(DaysText, $"{Days:00}", font, FontColor.White);
        form.SetText(DaysColon, ":", font, FontColor.White);
        form.SetText(HoursText, $"{Hours:00}", font, FontColor.White);
        form.SetText(HoursColon, ":", font, FontColor.White);
        form.SetText(MinutesText, $"{Minutes:00}", font, FontColor.White);

        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        form.SetHighlight(DaysText, Selection == RestField.Days);
        form.SetHighlight(HoursText, Selection == RestField.Hours);
        form.SetHighlight(MinutesText, Selection == RestField.Minutes);
    }

    /// <summary>Moves to the next field, wrapping (<c>KC_TAB</c>).</summary>
    public void Tab()
    {
        Selection = Selection switch
        {
            RestField.Days => RestField.Hours,
            RestField.Hours => RestField.Minutes,
            _ => RestField.Days,
        };
        ApplyHighlight();
    }

    public void Select(RestField field)
    {
        Selection = field;
        ApplyHighlight();
    }

    /// <summary>
    /// Adds one to the selected field (<c>RTF_IncrStat</c>).
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// <b>Incrementing carries.</b> 59 minutes becomes an hour, 23 hours becomes a day — and the
    /// minutes case checks the hour rollover itself rather than falling through, so a minute added
    /// at 23:59 advances the day too.
    /// </remarks>
    public bool Increment(BitmapFont font)
    {
        switch (Selection)
        {
            case RestField.Days:
                Days++;
                break;

            case RestField.Hours:
                Hours++;
                if (Hours >= 24) { Hours = 0; Days++; }
                break;

            default:
                Minutes++;
                if (Minutes >= 60) { Minutes = 0; Hours++; }
                if (Hours >= 24) { Hours = 0; Days++; }
                break;
        }

        Refresh(font);
        return true;
    }

    /// <summary>
    /// Takes one off the selected field (<c>RTF_DecrStat</c>).
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// <b>Decrementing does not borrow, and the asymmetry with <see cref="Increment"/> is
    /// deliberate in the reference.</b> Each field simply refuses at zero, so 1 day 00:00 stays
    /// there when the player takes a minute off it rather than becoming 0 days 23:59. Making the
    /// two symmetric would let a player walk the clock backwards past the rest they asked for.
    /// </remarks>
    public bool Decrement(BitmapFont font)
    {
        switch (Selection)
        {
            case RestField.Days when Days > 0:
                Days--;
                break;

            case RestField.Hours when Hours > 0:
                Hours--;
                break;

            case RestField.Minutes when Minutes > 0:
                Minutes--;
                break;

            default:
                return false;
        }

        Refresh(font);
        return true;
    }

    /// <summary>The total rest in minutes.</summary>
    public long TotalMinutes => (Days * 24 * 60) + (Hours * 60) + Minutes;

    public void Display(Surface destination, BitmapFont font) => form.Display(destination, font);
}
