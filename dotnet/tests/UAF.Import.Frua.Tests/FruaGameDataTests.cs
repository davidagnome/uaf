using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Reading a DOS FRUA design's <c>game001.dat</c> (<c>ImportGameDat</c>,
/// <c>UAFWinEd/UAImport.cpp:4397</c>).
/// </summary>
/// <remarks>
/// <b>The corpus is gitignored and never reaches CI</b>, so every test that touches
/// <c>reference/</c> returns early when it is absent, and <c>dotnet.yml</c> warns so the skip is
/// not mistaken for a pass. The synthetic tests below carry the layout on CI; the corpus tests are
/// what prove the layout is right, because a fixture written from my own reading could only pin my
/// reading.
/// </remarks>
public class FruaGameDataTests
{
    /// <summary>The DOS designs that ship with FRUA, or null when the corpus is absent.</summary>
    private static string? FruaDesign(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", name);
        return Directory.Exists(design) ? design : null;
    }

    // ---- the layout, without the corpus ----------------------------------------------------

    /// <summary>A minimal well-formed header, built field by field.</summary>
    private static byte[] Synthetic(string name = "Test Design")
    {
        var bytes = new byte[FruaGameData.Length];
        FruaGameData.TextEncoding.GetBytes(name).CopyTo(bytes, 0);

        void Dword(int at, uint v)
        {
            bytes[at] = (byte)v;
            bytes[at + 1] = (byte)(v >> 8);
            bytes[at + 2] = (byte)(v >> 16);
            bytes[at + 3] = (byte)(v >> 24);
        }

        Dword(32, 50_000);
        Dword(36, 100);
        Dword(40, 7);
        Dword(44, 9);
        bytes[48] = 5;      // start level, one-based
        bytes[49] = 1;      // start experience profile, one-based
        bytes[50] = 3;      // start equipment
        FruaGameData.TextEncoding.GetBytes("Brass Key").CopyTo(bytes, 52);
        FruaGameData.TextEncoding.GetBytes("Odd Lamp").CopyTo(bytes, 180);
        return bytes;
    }

    [Fact]
    public void The_header_reads_field_by_field()
    {
        var game = FruaGameData.Read(Synthetic());

        Assert.Equal("Test Design", game.DesignName);
        Assert.Equal(50_000u, game.StartExperience);
        Assert.Equal(100u, game.StartPlatinum);
        Assert.Equal(7u, game.StartGems);
        Assert.Equal(9u, game.StartJewelry);
        Assert.Equal(3, game.StartEquipment);
    }

    /// <summary>Both counters are stored one-based and arrive zero-based.</summary>
    [Fact]
    public void The_level_and_profile_are_made_zero_based()
    {
        var game = FruaGameData.Read(Synthetic());

        Assert.Equal(4, game.StartLevel);              // stored 5
        Assert.Equal(0, game.StartExperienceProfile);  // stored 1
    }

    /// <summary>
    /// A blank slot gets a generated name rather than an empty string, one-based in the text.
    /// </summary>
    [Fact]
    public void Empty_slots_are_named_after_their_position()
    {
        var game = FruaGameData.Read(Synthetic());

        Assert.Equal("Brass Key", game.SpecialKeys[0]);
        Assert.Equal("Key 2", game.SpecialKeys[1]);
        Assert.Equal(8, game.SpecialKeys.Count);

        Assert.Equal("Odd Lamp", game.SpecialItems[0]);
        Assert.Equal("Item 12", game.SpecialItems[11]);
        Assert.Equal(12, game.SpecialItems.Count);
    }

    [Fact]
    public void A_nameless_design_gets_the_references_substitute()
    {
        var bytes = Synthetic(name: "");

        Assert.Equal("NoName FRUA Design", FruaGameData.Read(bytes).DesignName);
    }

    /// <summary>
    /// A text field ends at its first NUL, not at its last non-NUL byte.
    /// </summary>
    /// <remarks>
    /// This is the shape <c>TUTORIAL.DSN</c> actually stores, and the reason the reader uses
    /// <c>IndexOf(0)</c> rather than trimming: the reference hands the buffer to <c>CString</c>,
    /// where <c>strlen</c> stops at the first NUL and everything past it is invisible.
    /// </remarks>
    [Fact]
    public void A_field_ends_at_its_first_NUL()
    {
        var bytes = Synthetic(name: "");
        FruaGameData.TextEncoding.GetBytes("tutorial design").CopyTo(bytes, 0);
        bytes[18] = (byte)'g';   // junk after the terminator, as the real file has

        Assert.Equal("tutorial design", FruaGameData.Read(bytes).DesignName);
    }

    [Fact]
    public void A_short_file_is_refused_rather_than_read_past()
    {
        var thrown = Assert.Throws<InvalidDataException>(
            () => FruaGameData.Read(new byte[100]));

        Assert.Contains("388", thrown.Message, StringComparison.Ordinal);
    }

    // ---- the real DOS designs --------------------------------------------------------------

    /// <summary>
    /// <c>HEIRS.DSN</c> is the design Phase 6's exit criterion names, and this is its real header.
    /// </summary>
    [Fact]
    public void Heirs_to_skull_crag_reads()
    {
        if (FruaDesign("HEIRS.DSN") is not { } design)
        {
            return;
        }

        var game = FruaGameData.ReadFile(design);

        Assert.Equal("Heirs to skull crag", game.DesignName);
        Assert.Equal(50_000u, game.StartExperience);
        Assert.Equal(100u, game.StartPlatinum);
        Assert.Equal(0u, game.StartGems);
        Assert.Equal(4, game.StartLevel);
        Assert.Equal("key of wrath", game.SpecialKeys[0]);
        Assert.Equal("the Sword", game.SpecialItems[0]);
    }

    /// <summary>
    /// <c>TUTORIAL.DSN</c>, whose name field carries junk past its terminator.
    /// </summary>
    [Fact]
    public void The_tutorial_design_stops_at_its_terminator()
    {
        if (FruaDesign("TUTORIAL.DSN") is not { } design)
        {
            return;
        }

        var game = FruaGameData.ReadFile(design);

        // The field holds "tutorial design\0\0\0g" -- see A_field_ends_at_its_first_NUL.
        Assert.Equal("tutorial design", game.DesignName);
        Assert.Equal(10_001u, game.StartExperience);
        Assert.Equal(0, game.StartLevel);
        Assert.Equal("bronze Key", game.SpecialKeys[0]);
    }

    /// <summary>
    /// Every DOS design in the corpus reads, and each header is exactly 388 bytes.
    /// </summary>
    /// <remarks>
    /// <b>A <c>.DSN</c> directory is not necessarily a design.</b> The shipped
    /// <c>AAAAAAAA.DSN</c> holds nothing but an empty <c>SAVE</c> folder, and <c>DISK1</c> is
    /// empty outright — so an importer offering the user a list of <c>.DSN</c> folders would offer
    /// two that cannot be opened. <c>game001.dat</c> is what makes a directory a design, which is
    /// why this enumerates by that file rather than by a hard-coded list of names. The first draft
    /// hard-coded three and failed on the stub.
    /// </remarks>
    [Fact]
    public void Every_shipped_DOS_design_reads()
    {
        if (FruaDesign("HEIRS.DSN") is not { } known)
        {
            return;
        }

        string root = Path.GetDirectoryName(known)!;
        int read = 0;

        foreach (string design in Directory.EnumerateDirectories(root))
        {
            if (FruaFiles.Resolve(design, "game001.dat") is not { } path)
            {
                continue;
            }

            Assert.Equal(FruaGameData.Length, new FileInfo(path).Length);

            var game = FruaGameData.ReadFile(design);
            Assert.NotEmpty(game.DesignName);
            Assert.Equal(8, game.SpecialKeys.Count);
            Assert.Equal(12, game.SpecialItems.Count);
            read++;
        }

        // HEIRS and TUTORIAL carry one; AAAAAAAA.DSN and DISK1 do not.
        Assert.Equal(2, read);
    }

    /// <summary>
    /// The lower-case name the reference builds finds the upper-case file the design ships.
    /// </summary>
    [Fact]
    public void A_lower_case_name_finds_an_upper_case_file()
    {
        if (FruaDesign("HEIRS.DSN") is not { } design)
        {
            return;
        }

        // What the reference asks for, against what DOS stored.
        Assert.NotNull(FruaFiles.Resolve(design, "game001.dat"));
        Assert.NotNull(FruaFiles.Resolve(design, "GAME001.DAT"));
        Assert.NotNull(FruaFiles.Resolve(design, "GeO001.dAt"));
        Assert.Null(FruaFiles.Resolve(design, "nosuchfile.dat"));
    }
}
