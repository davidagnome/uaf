using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The eleven <c>$Gr*</c> drawing calls — how a design draws the character sheet.
/// </summary>
/// <remarks>
/// The engine runs a script for the sheet (the <c>Global_Display</c> ability, or a built-in one)
/// and these are its only primitives, so a design can replace the whole layout without touching
/// the engine.
/// </remarks>
public class GpdlGraphicsTests
{
    /// <summary>A host that measures every character as the same width.</summary>
    private sealed class FixedWidthHost(int perCharacter) : GpdlUnhostedEnvironment
    {
        public override int DrawText(string text, int x, int y, int color)
        {
            base.DrawText(text, x, y, color);
            return (text ?? string.Empty).Length * perCharacter;
        }
    }

    private static void Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile("$PUBLIC $FUNC f() { " + body + " } f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
    }

    /// <summary>A point is defined and read back.</summary>
    [Fact]
    public void A_point_is_defined_by_number()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$GrSet("LeftCol", "20", "30");""", host);

        Assert.Equal((20, 30), host.Graphics.Point_("LeftCol"));
    }

    /// <summary>
    /// A point's coordinate can come from another point rather than a number.
    /// </summary>
    /// <remarks>
    /// <b>The two are told apart by whether the name is already defined</b>, and the x and y
    /// sources are looked up independently — so one point can take its column from a second and its
    /// row from a number. This is what lets a sheet's layout be written once and shifted as a
    /// whole.
    /// </remarks>
    [Fact]
    public void A_point_can_be_defined_from_another()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""
            $GrSet("Left", "20", "30");
            $GrSet("Right", "300", "40");
            $GrSet("Mixed", "Right", "Left");
            """, host);

        // x from Right, y from Left -- each side resolved on its own.
        Assert.Equal((300, 30), host.Graphics.Point_("Mixed"));
    }

    /// <summary>
    /// A name nothing defined is zero, not an error.
    /// </summary>
    /// <remarks>
    /// The fallback is <c>atoi</c>, which reads text with no leading digits as 0 — so a typo'd
    /// point name silently places the point at the origin.
    /// </remarks>
    [Fact]
    public void An_undefined_name_reads_as_zero()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$GrSet("Oops", "NoSuchPoint", "40");""", host);

        Assert.Equal((0, 40), host.Graphics.Point_("Oops"));
    }

    /// <summary>Unquoted numbers are accepted, as the built-in sheet script writes them.</summary>
    /// <remarks>
    /// The engine's own script says <c>$GrSet("LeftCol",20,20)</c> with bare integers where the
    /// table declares STRING — so a compiler that insisted on quotes could not compile the sheet
    /// the reference ships.
    /// </remarks>
    [Fact]
    public void Unquoted_numbers_compile()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$GrSet("LeftCol", 20, 30);""", host);

        Assert.Equal((20, 30), host.Graphics.Point_("LeftCol"));
    }

    /// <summary><c>$GrMoveTo</c> sets both the anchor and the cursor.</summary>
    [Fact]
    public void MoveTo_sets_the_anchor_and_the_cursor()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""
            $GrSet("Here", "50", "60");
            $GrMoveTo("Here");
            """, host);

        Assert.Equal(50, host.Graphics.AnchorX);
        Assert.Equal(60, host.Graphics.AnchorY);
        Assert.Equal(50, host.Graphics.CursorX);
        Assert.Equal(60, host.Graphics.CursorY);
    }

    /// <summary>
    /// <c>$GrTab</c> moves the cursor and leaves the anchor, which is what keeps a column straight.
    /// </summary>
    /// <remarks>
    /// <b>The distinction the two cursors exist for.</b> Tabbing twice on one line both times
    /// measures from the start of the line — if the anchor moved, every column after the first
    /// would drift right by the width of what was printed before it.
    /// </remarks>
    [Fact]
    public void Tab_moves_the_cursor_from_the_anchor_not_from_itself()
    {
        var host = new FixedWidthHost(7);

        Run("""
            $GrSet("Start", "10", "20");
            $GrSet("Col", "100", "0");
            $GrMoveTo("Start");
            $GrPrint("LABEL");
            $GrTab("Col");
            """, host);

        // The anchor never moved, so the tab lands at anchor + 100 rather than after the text.
        Assert.Equal(10, host.Graphics.AnchorX);
        Assert.Equal(110, host.Graphics.CursorX);
        Assert.Equal(20, host.Graphics.CursorY);

        // And a second tab measures from the same place, not from the first.
        Run("""$GrTab("Col");""", host);
        Assert.Equal(110, host.Graphics.CursorX);
    }

    /// <summary><c>$GrMove</c> shifts the anchor and brings the cursor with it.</summary>
    [Fact]
    public void Move_shifts_the_anchor()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""
            $GrSet("Start", "10", "20");
            $GrSet("Down", "0", "12");
            $GrMoveTo("Start");
            $GrMove("Down");
            $GrMove("Down");
            """, host);

        // Relative, where $GrMoveTo is absolute: two moves is two line heights.
        Assert.Equal(10, host.Graphics.AnchorX);
        Assert.Equal(44, host.Graphics.AnchorY);
        Assert.Equal(44, host.Graphics.CursorY);
    }

    /// <summary><c>$GrMark</c> records where the cursor is, under a new name.</summary>
    [Fact]
    public void Mark_records_the_cursor()
    {
        var host = new FixedWidthHost(6);

        Run("""
            $GrSet("Start", "10", "20");
            $GrMoveTo("Start");
            $GrPrint("ABC");
            $GrMark("AfterLabel");
            """, host);

        // Three characters at six pixels each.
        Assert.Equal((28, 20), host.Graphics.Point_("AfterLabel"));
    }

    /// <summary>
    /// <c>$GrPrint</c> advances the cursor by the text's width; <c>$GrPrtLF</c> does not.
    /// </summary>
    /// <remarks>
    /// <b>They are not "print" and "print then newline".</b> <c>$GrPrtLF</c> discards the
    /// measurement entirely and jumps to the next line, so a <c>$GrPrint</c> after one starts at the
    /// line's beginning rather than after the text.
    /// </remarks>
    [Fact]
    public void Print_advances_the_cursor_and_PrtLF_starts_a_line()
    {
        var host = new FixedWidthHost(5);

        Run("""
            $GrSet("Start", "10", "20");
            $GrSet("Height", "0", "12");
            $GrSetLinefeed("Height");
            $GrMoveTo("Start");
            $GrPrint("ABCD");
            """, host);

        Assert.Equal(30, host.Graphics.CursorX);
        Assert.Equal(20, host.Graphics.CursorY);

        Run("""$GrPrtLF("EFGH");""", host);

        // Back to the line's start and down one linefeed -- the four characters just drawn did not
        // move the cursor along.
        Assert.Equal(10, host.Graphics.CursorX);
        Assert.Equal(32, host.Graphics.CursorY);

        // Both drew, at where the cursor was when each was called.
        Assert.Equal([("ABCD", 10, 20), ("EFGH", 30, 20)],
                     host.Drawn.Select(d => (d.Text, d.X, d.Y)));
    }

    /// <summary>The linefeed defaults to twelve pixels down, before any script sets it.</summary>
    [Fact]
    public void The_linefeed_starts_at_twelve_down()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(0, host.Graphics.LinefeedX);
        Assert.Equal(12, host.Graphics.LinefeedY);

        Run("""$GrPrtLF("x");""", host);
        Assert.Equal(12, host.Graphics.CursorY);
    }

    /// <summary>Each name selects its colour, and the colour sticks until changed.</summary>
    [Theory]
    [InlineData("WHITE", GpdlFontColor.White)]
    [InlineData("GREEN", GpdlFontColor.Green)]
    [InlineData("YELLOW", GpdlFontColor.Yellow)]
    [InlineData("ORANGE", GpdlFontColor.Orange)]
    [InlineData("RED", GpdlFontColor.Red)]
    [InlineData("CYAN", GpdlFontColor.Cyan)]
    [InlineData("MAGENTA", GpdlFontColor.Magenta)]
    [InlineData("SILVER", GpdlFontColor.Silver)]
    [InlineData("BLACK", GpdlFontColor.Black)]
    [InlineData("BLUE", GpdlFontColor.Blue)]
    public void A_colour_name_selects_its_colour(string name, GpdlFontColor expected)
    {
        var host = new GpdlUnhostedEnvironment();

        Run($"""$GrColor("{name}"); $GrPrint("x");""", host);

        Assert.Equal((int)expected, host.Graphics.Color);
        Assert.Equal((int)expected, Assert.Single(host.Drawn).Color);
    }

    /// <summary>
    /// The colour names are case-sensitive, and anything unrecognised is white.
    /// </summary>
    /// <remarks>
    /// <b>A real trap, and kept.</b> <c>GrColor</c> compares with <c>CString::operator==</c> where
    /// the engine's other colour lookup (<c>ASCII_TO_COLOR</c>) uses <c>CompareNoCase</c> — so
    /// <c>$GrColor("Red")</c> draws in <b>white</b>, silently. Making it case-insensitive would be
    /// a visible change to every existing design with a lower-case colour name in it: text that
    /// renders white today would start rendering red. This is a rendering contract, not a defect
    /// that stops a design loading.
    /// </remarks>
    [Theory]
    [InlineData("Red")]
    [InlineData("red")]
    [InlineData("BRIGHTORANGE")]
    [InlineData("PUCE")]
    [InlineData("")]
    public void An_unrecognised_colour_name_is_white(string name)
    {
        var host = new GpdlUnhostedEnvironment();

        Run($"""$GrColor("RED"); $GrColor("{name}");""", host);

        Assert.Equal((int)GpdlFontColor.White, host.Graphics.Color);
    }

    /// <summary>A name the movers do not know leaves everything where it was.</summary>
    [Fact]
    public void An_undefined_point_moves_nothing()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""
            $GrSet("Start", "40", "50");
            $GrMoveTo("Start");
            $GrMoveTo("NoSuchPoint");
            $GrTab("NoSuchPoint");
            $GrMove("NoSuchPoint");
            $GrSetLinefeed("NoSuchPoint");
            """, host);

        Assert.Equal(40, host.Graphics.CursorX);
        Assert.Equal(50, host.Graphics.CursorY);
        Assert.Equal(12, host.Graphics.LinefeedY);
    }

    /// <summary>
    /// <c>$GrPic</c> does nothing at all, and <c>$GrFormat</c> is stored and never read.
    /// </summary>
    /// <remarks>
    /// Both are declared and inert in the reference: <c>GrPic</c>'s whole body is
    /// <c>return "";</c>, and <c>grc.format</c> is assigned at <c>CharStatsForm.cpp:1525</c> and
    /// looked at nowhere. Pinned so the next person does not have to re-derive it.
    /// </remarks>
    [Fact]
    public void GrPic_does_nothing_and_GrFormat_is_never_read()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""
            $GrSet("Start", "10", "20");
            $GrMoveTo("Start");
            $GrPic("SomePicture");
            $GrFormat("SL");
            """, host);

        Assert.Empty(host.Drawn);
        Assert.Equal(10, host.Graphics.CursorX);
        Assert.Equal("SL", host.Graphics.Format);
    }

    /// <summary>
    /// Every one of the eleven answers the empty string.
    /// </summary>
    /// <remarks>
    /// So each is a statement rather than a value — a script writing <c>$IF $GrPrint(...)</c> is
    /// always taking the else arm.
    /// </remarks>
    [Theory]
    [InlineData("""$GrSet("a", "1", "2")""")]
    [InlineData("""$GrSetLinefeed("a")""")]
    [InlineData("""$GrMoveTo("a")""")]
    [InlineData("""$GrMove("a")""")]
    [InlineData("""$GrTab("a")""")]
    [InlineData("""$GrMark("a")""")]
    [InlineData("""$GrFormat("a")""")]
    [InlineData("""$GrColor("RED")""")]
    [InlineData("""$GrPrint("a")""")]
    [InlineData("""$GrPrtLF("a")""")]
    [InlineData("""$GrPic("a")""")]
    public void Every_call_answers_nothing(string call)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile($"$PUBLIC $FUNC f() {{ $RETURN {call}; }} f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var host = new GpdlUnhostedEnvironment();
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);

        Assert.Equal(string.Empty, vm.Execute("f"));
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
    }

    /// <summary>
    /// A slice of the engine's own sheet script lays out where the reference puts it.
    /// </summary>
    /// <remarks>
    /// <b>Taken verbatim from <c>defaultCharStats</c></b> (<c>CharStatsForm.cpp:1055</c>) — the
    /// script the engine falls back on when a design has no <c>Global_Display</c> ability. Running
    /// the real thing is what shows the eleven calls compose into a layout rather than each working
    /// alone.
    /// </remarks>
    [Fact]
    public void A_slice_of_the_engines_own_sheet_lays_out()
    {
        var host = new FixedWidthHost(8);

        Run("""
            $GrSet("LeftCol",20,20);
            $GrSet("RightCol", 300,20);
            $GrSet("StatusTab", 150, 0);
            $GrSet("TextHeight",0,20);
            $GrSetLinefeed("TextHeight");
            $GrMoveTo("LeftCol");
            $GrColor("YELLOW");
            $GrPrtLF("HERO");
            $GrMark("Level");
            $GrMoveTo("RightCol");
            $GrColor("WHITE");
            $GrPrint("STATUS");
            $GrColor("GREEN");
            $GrTab("StatusTab");
            $GrPrtLF("OKAY");
            """, host);

        Assert.Equal(
        [
            // The name, at the left column.
            ("HERO", 20, 20, (int)GpdlFontColor.Yellow),

            // "STATUS" at the right column. $GrMoveTo is absolute, so it discards the line the
            // $GrPrtLF above had advanced to and starts again at RightCol's own y.
            ("STATUS", 300, 20, (int)GpdlFontColor.White),

            // And the value tabbed 150 across from the right column's start, NOT from after
            // "STATUS" -- which is the whole reason the anchor and the cursor are separate.
            ("OKAY", 450, 20, (int)GpdlFontColor.Green),
        ], host.Drawn);

        // "Level" was marked after the name's line feed, so it is the start of the second line.
        Assert.Equal((20, 40), host.Graphics.Point_("Level"));
    }

    /// <summary>
    /// Clearing resets the cursor and the colour too.
    /// </summary>
    /// <remarks>
    /// <b>A divergence.</b> <c>GR_CONTROL::Clear</c> resets the points, the linefeed, the anchor
    /// and the format — but leaves the cursor and the colour alone, so both carry over from the
    /// previous sheet. The built-in script sets them before drawing, which is why nobody noticed.
    /// A sheet's appearance depending on which character was looked at last is not worth
    /// reproducing.
    /// </remarks>
    [Fact]
    public void Clearing_resets_the_cursor_and_the_colour()
    {
        var host = new FixedWidthHost(5);

        Run("""
            $GrSet("Start", "40", "50");
            $GrMoveTo("Start");
            $GrColor("RED");
            """, host);

        host.Graphics.Clear();

        Assert.Equal(0, host.Graphics.CursorX);
        Assert.Equal(0, host.Graphics.CursorY);
        Assert.Equal((int)GpdlFontColor.White, host.Graphics.Color);

        // And what the reference does clear, this clears too.
        Assert.Null(host.Graphics.Point_("Start"));
        Assert.Equal(0, host.Graphics.AnchorX);
        Assert.Equal(12, host.Graphics.LinefeedY);
        Assert.Equal(string.Empty, host.Graphics.Format);
    }
}
