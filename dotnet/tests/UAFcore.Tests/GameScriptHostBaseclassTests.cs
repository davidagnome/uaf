using UAF.Media.Sdl;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The baseclass and readied-item calls against a loaded design.
/// </summary>
/// <remarks>
/// The fakes in <c>GpdlBaseclassTests</c> pin what the VM does with a host's answers; this pins
/// that the live host answers from real party members.
/// </remarks>
public class GameScriptHostBaseclassTests
{
    private static Game? Load()
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
        return new Game(design, levelIndex: 1) { Dice = _ => 20 };
    }

    /// <summary>The first party member that has at least one baseclass.</summary>
    private static (Game Game, GameScriptHost Host, Character Who)? Party()
    {
        if (Load() is not { } game)
        {
            return null;
        }

        foreach (var member in game.Party.Members)
        {
            if (member.Baseclasses.Count > 0)
            {
                return (game, new GameScriptHost(game), member);
            }
        }

        return null;
    }

    /// <summary>
    /// The party really does have a character with a baseclass to ask about.
    /// </summary>
    /// <remarks>
    /// Every test below early-returns when the corpus is absent, so without this one they would all
    /// pass on a checkout with no <c>reference/</c> and prove nothing. This fails loudly if the
    /// design stops carrying a party.
    /// </remarks>
    [Fact]
    public void The_corpus_design_has_a_party_to_ask_about()
    {
        if (Load() is null)
        {
            return;
        }

        Assert.NotNull(Party());
    }

    /// <summary>Experience and level come off the character's own baseclass list.</summary>
    [Fact]
    public void A_baseclass_answers_its_own_experience_and_level()
    {
        if (Party() is not { } party)
        {
            return;
        }

        var progress = party.Who.Baseclasses[0];
        string id = progress.BaseclassId;

        Assert.Equal(progress.Experience,
                     party.Host.BaseclassProgress(party.Who.CharacterId, id, level: false));
        Assert.Equal(progress.CurrentLevel,
                     party.Host.BaseclassProgress(party.Who.CharacterId, id, level: true));
    }

    /// <summary>A class the character does not have answers zero rather than failing.</summary>
    [Fact]
    public void A_class_the_character_lacks_answers_zero()
    {
        if (Party() is not { } party)
        {
            return;
        }

        Assert.Equal(0, party.Host.BaseclassProgress(
            party.Who.CharacterId, "NoSuchBaseclass", level: true));

        // And so does a character nobody recognises.
        Assert.Equal(0, party.Host.BaseclassProgress(
            "NoSuchCharacter", party.Who.Baseclasses[0].BaseclassId, level: true));
    }

    /// <summary>A write lands on the character and reads back.</summary>
    [Fact]
    public void A_written_level_reads_back()
    {
        if (Party() is not { } party)
        {
            return;
        }

        string id = party.Who.Baseclasses[0].BaseclassId;

        party.Host.SetBaseclassProgress(party.Who.CharacterId, id, level: true, value: 9);
        Assert.Equal(9, party.Host.BaseclassProgress(party.Who.CharacterId, id, level: true));

        party.Host.SetBaseclassProgress(party.Who.CharacterId, id, level: false, value: 54321);
        Assert.Equal(54321,
                     party.Host.BaseclassProgress(party.Who.CharacterId, id, level: false));

        // The level did not move when the experience was written.
        Assert.Equal(9, party.Host.BaseclassProgress(party.Who.CharacterId, id, level: true));
    }

    /// <summary>
    /// Writing a class the character does not have adds nothing.
    /// </summary>
    /// <remarks>
    /// <b>The setter does not multi-class anybody.</b> Gaining a class is <c>AddBaseclass</c>, a
    /// different operation with its own rules — a setter that quietly conjured one would be a
    /// surprising way to do it.
    /// </remarks>
    [Fact]
    public void Writing_a_class_the_character_lacks_adds_nothing()
    {
        if (Party() is not { } party)
        {
            return;
        }

        int before = party.Who.Baseclasses.Count;

        party.Host.SetBaseclassProgress(
            party.Who.CharacterId, "NoSuchBaseclass", level: true, value: 5);

        Assert.Equal(before, party.Who.Baseclasses.Count);
    }

    /// <summary>The highest-level class is one the character actually has.</summary>
    [Fact]
    public void The_highest_level_class_is_one_the_character_has()
    {
        if (Party() is not { } party)
        {
            return;
        }

        string highest = party.Host.HighestLevelBaseclass(party.Who.CharacterId);

        Assert.Contains(party.Who.Baseclasses, b => b.BaseclassId == highest);

        // And nothing it has is further along.
        int level = party.Who.Baseclasses.First(b => b.BaseclassId == highest).CurrentLevel;
        Assert.All(party.Who.Baseclasses, b => Assert.True(b.CurrentLevel <= level));
    }

    /// <summary>
    /// Readying moves a possession rather than acquiring one, and it reads back.
    /// </summary>
    /// <remarks>
    /// <b>Only an item the character already carries can be readied.</b> The reference readies out
    /// of the character's own list — so this changes where a possession is worn, and an item
    /// nobody carries is not conjured.
    /// </remarks>
    [Fact]
    public void Readying_moves_a_possession_and_conjures_nothing()
    {
        if (Party() is not { } party || party.Who.Items.Count == 0)
        {
            return;
        }

        int before = party.Who.Items.Count;
        string carried = party.Who.Items[0].ItemId;

        party.Host.Ready(party.Who.CharacterId, carried, "WEAPON");

        Assert.Equal(carried, party.Host.ReadiedItem(party.Who.CharacterId, "WEAPON", 0));
        Assert.Equal(before, party.Who.Items.Count);

        // An item nobody carries changes nothing -- including whatever the design already had in
        // that slot. The party member ships with a shield readied, which is why this reads the
        // slot first rather than assuming it is empty.
        string shielded = party.Host.ReadiedItem(party.Who.CharacterId, "SHIELD", 0);

        party.Host.Ready(party.Who.CharacterId, "NoSuchItem", "SHIELD");

        Assert.Equal(shielded, party.Host.ReadiedItem(party.Who.CharacterId, "SHIELD", 0));
        Assert.Equal(before, party.Who.Items.Count);
    }

    /// <summary>
    /// An empty location means "not readied", not "anywhere".
    /// </summary>
    /// <remarks>
    /// The reference substitutes <c>Cannot</c> for a blank location, which is the code an
    /// unequipped item carries — so asking with no location finds what is in the backpack.
    /// </remarks>
    [Fact]
    public void An_empty_location_finds_what_is_not_readied()
    {
        if (Party() is not { } party || party.Who.Items.Count == 0)
        {
            return;
        }

        // Put one item away and ready another, then ask both ways.
        string first = party.Who.Items[0].ItemId;
        party.Host.Ready(party.Who.CharacterId, first, string.Empty);

        Assert.Equal(first, party.Host.ReadiedItem(party.Who.CharacterId, string.Empty, 0));

        party.Host.Ready(party.Who.CharacterId, first, "WEAPON");
        Assert.NotEqual(first, party.Host.ReadiedItem(party.Who.CharacterId, string.Empty, 0));
    }

    /// <summary>
    /// The effective armour class counts readied equipment, where the base one does not.
    /// </summary>
    /// <remarks>
    /// <b>Three armour classes, and this is what separates two of them.</b> Readying a piece of
    /// armour has to move <c>$GET_CHAR_EFFAC</c> and leave <c>$GET_CHAR_AC</c> alone — a port that
    /// answered the same number for both would pass every other test here.
    /// </remarks>
    [Fact]
    public void Readying_armour_moves_only_the_effective_armour_class()
    {
        if (Party() is not { } party)
        {
            return;
        }

        // Find something the character carries that actually protects.
        foreach (var carried in party.Who.Items)
        {
            if (party.Game.Design.Item(carried.ItemId) is not { } record
                || record.Combat.ProtectionBase + record.Combat.ProtectionBonus == 0)
            {
                continue;
            }

            party.Host.Ready(party.Who.CharacterId, carried.ItemId, string.Empty);
            string bare = party.Host.GetCharStat(party.Who.CharacterId,
                                                 GpdlCharStat.EffectiveArmorClass);

            party.Host.Ready(party.Who.CharacterId, carried.ItemId, "ARMOR ");
            string worn = party.Host.GetCharStat(party.Who.CharacterId,
                                                 GpdlCharStat.EffectiveArmorClass);

            Assert.NotEqual(bare, worn);

            // The base class is unmoved by any of it.
            Assert.Equal(party.Who.ArmorClass.ToString(),
                         party.Host.GetCharStat(party.Who.CharacterId, GpdlCharStat.ArmorClass));
            return;
        }
    }
}
