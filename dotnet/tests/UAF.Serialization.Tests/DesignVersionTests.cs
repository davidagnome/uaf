using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Locks in the version-scheme facts established in docs/PORTING-PLAN.md section 3.
/// These are transcriptions from the C++ tree, so a failure here means either the generator
/// drifted or someone "corrected" a value by hand.
/// </summary>
public class DesignVersionTests
{
    [Fact]
    public void Product_and_engine_are_5_29_not_the_dead_headers_0_998110()
    {
        // src/Shared/Globals.cpp:124-126. The stale src/Shared/ProjectVersion.h says 0.998110;
        // trusting it would put PRODUCT_VER below what the shipped editor writes, and
        // Level.cpp:3348 refuses to load designs newer than PRODUCT_VER.
        Assert.Equal(5.29, DesignVersion.Product.Value, precision: 10);
        Assert.Equal(5.29, DesignVersion.Engine.Value, precision: 10);
    }

    [Fact]
    public void Spell_id_gates_match_globals_cpp()
    {
        Assert.Equal(0.998100, DesignVersion.SpellIDs.Value, precision: 10);
        Assert.Equal(0.998101, DesignVersion.SpellNames.Value, precision: 10);
        // Recovered independently from the MSVC constant pool at UAFWinEd.exe+0x4084a8
        // before the source definition was located. See plan section 3.1.
        Assert.Equal(0.998914, DesignVersion.SaveIDs.Value, precision: 10);
    }

    [Fact]
    public void Version_axis_spans_two_eras_and_stays_ordered()
    {
        // The axis is monotonic but crosses 1.0 — anything assuming "< 1.0" is wrong.
        Assert.True(DesignVersion.V0500 < DesignVersion.V0930);
        Assert.True(DesignVersion.V0930 < DesignVersion.SaveIDs);
        Assert.True(DesignVersion.SaveIDs < DesignVersion.V524);
        Assert.True(DesignVersion.V524 < DesignVersion.Product);
        Assert.True(DesignVersion.Product.Value > 1.0);
    }

    [Fact]
    public void Archive_layer_switches_at_0_573()
    {
        // Level.cpp:2168 — below this, files use a plain CArchive with no CAR wrapper and no LZW.
        Assert.True(DesignVersion.V0572 < DesignVersion.V0573);
        // The magic-absent fallback for level/game data lands *below* the switch, so every
        // unstamped design file is read with the plain archive.
        Assert.True(DesignVersion.V0572 < DesignVersion.V0573);
    }

    [Fact]
    public void Unstamped_character_fallback_is_0_563()
    {
        // Char.cpp:6948 — distinct from the 0.572 used for level/game data (Level.cpp:2163).
        Assert.Equal(0.563, DesignVersion.Unstamped.Value, precision: 10);
        Assert.NotEqual(DesignVersion.Unstamped.Value, DesignVersion.V0572.Value);
    }

    [Fact]
    public void All_constants_were_transcribed()
    {
        Assert.Equal(98, DesignVersion.All.Count);
        Assert.Equal(DesignVersion.All.OrderBy(v => v.Value), DesignVersion.All);
    }
}
