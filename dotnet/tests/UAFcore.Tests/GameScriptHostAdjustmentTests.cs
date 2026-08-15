using UAF.Media.Sdl;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// <c>$SpellAdj</c> and <c>$SkillAdj</c> against a real character.
/// </summary>
/// <remarks>
/// Both exist to change a list at run time, which is why the lists had to become mutable on
/// <see cref="Character"/> rather than staying on the record.
/// </remarks>
public class GameScriptHostAdjustmentTests
{
    private static (Game Game, GameScriptHost Host, Character Who)? Party()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");

        if (root is null || !Directory.Exists(root))
        {
            return null;
        }

        var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());
        var game = new Game(design, levelIndex: 1) { Dice = _ => 20 };

        return game.Party.Members.Count == 0
            ? null
            : (game, new GameScriptHost(game), game.Party.Members[0]);
    }

    /// <summary>The premise: a party member whose adjustment lists can be written.</summary>
    [Fact]
    public void The_corpus_has_a_character_with_writable_lists()
    {
        if (Party() is not { } party)
        {
            return;
        }

        // Copied off the record, so writing here does not rewrite the design.
        Assert.NotSame(party.Who.Record.SpellAdjustments, party.Who.SpellAdjustments);
        Assert.NotSame(party.Who.Record.SkillAdjustments, party.Who.SkillAdjustments);

        Assert.Equal(party.Who.Record.SpellAdjustments.Count, party.Who.SpellAdjustments.Count);
        Assert.Equal(party.Who.Record.SkillAdjustments.Count, party.Who.SkillAdjustments.Count);
    }

    /// <summary>An adjustment is added with every field it was given.</summary>
    [Fact]
    public void A_spell_adjustment_is_added_whole()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SpellAdjustments.Clear();
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "Blessing", 1, 9, 50, 2);

        var added = Assert.Single(party.Who.SpellAdjustments);

        Assert.Equal(new SpellAdjustment("Cleric", "Blessing", 1, 9, 50, 2), added);
    }

    /// <summary>
    /// The list stays sorted by adjustment id, and an insert does not destroy its neighbour.
    /// </summary>
    /// <remarks>
    /// <b>A divergence, and this is the assertion for it.</b> The reference walks back to the
    /// insertion point and then calls <c>SetAtGrow(i, …)</c>, which <i>assigns</i> index i rather
    /// than shifting — so adding an adjustment that sorts before an existing one overwrites it.
    /// Appending in order works, which is presumably why it went unnoticed.
    /// </remarks>
    [Fact]
    public void An_insert_keeps_the_order_without_destroying_a_neighbour()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SpellAdjustments.Clear();

        // Added out of order on purpose: B, then D, then C between them.
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "B", 1, 1, 1, 0);
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "D", 1, 1, 1, 0);
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "C", 1, 1, 1, 0);

        Assert.Equal(["B", "C", "D"],
                     party.Who.SpellAdjustments.Select(a => a.AdjustmentId));

        // And one that sorts before everything still keeps the rest.
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "A", 1, 1, 1, 0);
        Assert.Equal(["A", "B", "C", "D"],
                     party.Who.SpellAdjustments.Select(a => a.AdjustmentId));
    }

    /// <summary>An id already present is replaced rather than duplicated.</summary>
    [Fact]
    public void An_existing_id_is_replaced()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SpellAdjustments.Clear();

        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "Blessing", 1, 9, 50, 2);
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "Blessing", 2, 8, 75, 3);

        var only = Assert.Single(party.Who.SpellAdjustments);
        Assert.Equal(75, only.Percent);
    }

    /// <summary>
    /// 999999 is a command, not a percentage.
    /// </summary>
    /// <remarks>
    /// <b>The only way to take an adjustment away</b>, and the same argument that is a bonus when
    /// adding becomes a "skip this many matches" counter when removing.
    /// </remarks>
    [Fact]
    public void The_magic_percent_removes_and_the_bonus_becomes_a_skip_count()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SpellAdjustments.Clear();

        // Three that all match the same school and id.
        for (int i = 0; i < 3; i++)
        {
            party.Who.SpellAdjustments.Add(
                new SpellAdjustment("Cleric", "Blessing", i, i, i, i));
        }

        Assert.Equal(999999, GpdlSkillAdjustment.RemoveSpellAdjustment);

        // Skip one, remove the second.
        party.Host.SpellAdjustment(party.Who.CharacterId, "Cleric", "Blessing",
                                   0, 0, GpdlSkillAdjustment.RemoveSpellAdjustment, 1);

        Assert.Equal([0, 2], party.Who.SpellAdjustments.Select(a => a.Percent));

        // A school that does not match removes nothing.
        party.Host.SpellAdjustment(party.Who.CharacterId, "Wizard", "Blessing",
                                   0, 0, GpdlSkillAdjustment.RemoveSpellAdjustment, 0);

        Assert.Equal(2, party.Who.SpellAdjustments.Count);
    }

    /// <summary>
    /// A skill adjustment's type character is the arithmetic, and it is stored.
    /// </summary>
    /// <remarks>
    /// There is no separate "what operation" field: the same character that selects a write also
    /// says whether the value adds, multiplies or replaces.
    /// </remarks>
    [Theory]
    [InlineData("+")]
    [InlineData("%")]
    [InlineData("=")]
    [InlineData("-")]
    [InlineData("*")]
    public void A_skill_adjustment_stores_its_type_character(string type)
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SkillAdjustments.Clear();

        Assert.Equal(string.Empty,
                     party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots",
                                                type, 7));

        var added = Assert.Single(party.Who.SkillAdjustments);
        Assert.Equal((sbyte)type[0], added.Type);
        Assert.Equal(7, added.Value);
    }

    /// <summary>Only the first character is looked at.</summary>
    [Fact]
    public void Only_the_first_character_of_the_type_matters()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SkillAdjustments.Clear();
        party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", "+bonus", 4);

        Assert.Equal((sbyte)'+', Assert.Single(party.Who.SkillAdjustments).Type);
    }

    /// <summary>A write to an existing pair replaces it; D removes it; A reads it back.</summary>
    [Fact]
    public void Writing_reading_and_deleting_work_on_the_same_entry()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SkillAdjustments.Clear();

        party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", "+", 3);
        Assert.Equal("3", party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots",
                                                     "A", 0));

        // Written again, not added twice.
        party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", "+", 9);
        Assert.Single(party.Who.SkillAdjustments);
        Assert.Equal("9", party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots",
                                                     "A", 0));

        party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", "D", 0);
        Assert.Empty(party.Who.SkillAdjustments);
    }

    /// <summary>
    /// A stored read that finds nothing answers a word, not a number.
    /// </summary>
    /// <remarks>
    /// So arithmetic on the result reads it as zero, and a script cannot tell "no such skill" from
    /// a skill worth nothing without comparing the text.
    /// </remarks>
    [Fact]
    public void A_missing_adjustment_reads_as_a_word()
    {
        if (Party() is not { } party)
        {
            return;
        }

        party.Who.SkillAdjustments.Clear();

        Assert.Equal("NoSkill",
                     party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", "A", 0));
        Assert.Equal(GpdlSkillAdjustment.NoSkill,
                     party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", "A", 0));
    }

    /// <summary>
    /// The four computed reads answer null, which the VM turns into a loud refusal.
    /// </summary>
    /// <remarks>
    /// <b>They need <c>GetAdjSkillValue</c> and the whole skill computation behind it.</b>
    /// Answering a plausible number would be worse than refusing — a design would branch on it and
    /// nothing would say the branch was wrong.
    /// </remarks>
    [Theory]
    [InlineData("F")]
    [InlineData("f")]
    [InlineData("b")]
    [InlineData("B")]
    public void The_computed_reads_are_refused_rather_than_guessed(string type)
    {
        if (Party() is not { } party)
        {
            return;
        }

        Assert.Null(party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", type, 0));
    }

    /// <summary>And a type character that is none of the eight is refused too.</summary>
    [Theory]
    [InlineData("Z")]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("a")]
    public void An_unrecognised_type_is_refused(string type)
    {
        if (Party() is not { } party)
        {
            return;
        }

        Assert.Null(party.Host.SkillAdjustment(party.Who.CharacterId, "Climb", "Boots", type, 0));
    }
}
