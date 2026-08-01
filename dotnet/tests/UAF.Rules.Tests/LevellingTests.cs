using UAF.Rules;
using UAF.Serialization;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers <see cref="Levelling"/> — experience to level, and who may train.
/// </summary>
/// <remarks>
/// The arithmetic is unit-tested against a table stated inline, and then the whole thing is run
/// once against the real thresholds in a design's <c>baseclass.dat</c>, so a change to either the
/// reader or these rules that breaks the join shows up here.
/// </remarks>
public class LevellingTests
{
    /// <summary>
    /// The AD&amp;D assassin's published table, and what `SomethingWild` actually carries: a
    /// leading zero and then the thresholds proper.
    /// </summary>
    private static readonly uint[] Assassin =
        [0, 1501, 3001, 6001, 12001, 25001, 50001, 100001, 200001, 300001];

    [Theory]
    [InlineData(0u, 1)]         // the leading zero is why nobody is level 0
    [InlineData(1500u, 1)]
    [InlineData(1501u, 2)]      // exactly on a threshold advances
    [InlineData(2001u, 2)]
    [InlineData(8001u, 4)]
    [InlineData(35001u, 6)]
    [InlineData(299999u, 9)]
    [InlineData(300001u, 10)]   // the whole table met: the level is its length
    [InlineData(9999999u, 10)]  // and it does not keep climbing past the end
    public void Experience_maps_to_the_level_the_table_says(uint experience, int level) =>
        Assert.Equal(level, Levelling.GetLevel(Assassin, experience));

    [Fact]
    public void A_drained_baseclass_is_entitled_to_no_level_at_all()
    {
        // previousLevel is the drain marker. The entitlement is 0, not "the level the experience
        // would buy" -- and IncCurExperience refuses to add to it, so it cannot climb back out on
        // its own. The character's other baseclasses are unaffected, which is why this is asked per
        // baseclass rather than per character.
        Assert.Equal(6, Levelling.GetAllowedLevel(Assassin, 35001, previousLevel: 0));
        Assert.Equal(0, Levelling.GetAllowedLevel(Assassin, 35001, previousLevel: 3));
    }

    [Fact]
    public void Ready_to_train_means_entitled_to_more_than_you_have()
    {
        Assert.True(Levelling.IsReadyToTrain(Assassin, 35001, currentLevel: 5, previousLevel: 0));
        Assert.False(Levelling.IsReadyToTrain(Assassin, 35001, currentLevel: 6, previousLevel: 0));

        // Already past the entitlement -- possible after a design edit -- is not "ready".
        Assert.False(Levelling.IsReadyToTrain(Assassin, 35001, currentLevel: 8, previousLevel: 0));

        // A drained baseclass never trains, however much experience it holds.
        Assert.False(Levelling.IsReadyToTrain(Assassin, 300001, currentLevel: 1, previousLevel: 1));
    }

    [Fact]
    public void One_session_grants_at_most_the_allowed_number_of_levels()
    {
        // Eligible for level 6 from level 1, but a session that grants one level arrives at 2.
        Assert.Equal(2, Levelling.Train(Assassin, 35001, currentLevel: 1, previousLevel: 0,
                                        maxLevelGain: 1));

        // With enough allowance it goes all the way, and no further.
        Assert.Equal(6, Levelling.Train(Assassin, 35001, currentLevel: 1, previousLevel: 0,
                                        maxLevelGain: 9));

        // Training when not entitled leaves the level alone rather than reducing it.
        Assert.Equal(8, Levelling.Train(Assassin, 35001, currentLevel: 8, previousLevel: 0,
                                        maxLevelGain: 1));
    }

    [Fact]
    public void A_capped_character_forfeits_the_experience_it_cannot_use()
    {
        // The reference destroys experience on purpose here, so a character held at a level cannot
        // bank an arbitrary total and jump several levels the moment the cap lifts.
        Assert.Equal(3000u, Levelling.CapExperience(Assassin, 35001, limitLevel: 2,
                                                    previousLevel: 0));

        // Under the ceiling nothing is taken.
        Assert.Equal(2500u, Levelling.CapExperience(Assassin, 2500, limitLevel: 2,
                                                    previousLevel: 0));

        // A limit past the end of the table caps nothing -- there is no threshold to read.
        Assert.Equal(35001u, Levelling.CapExperience(Assassin, 35001, limitLevel: 99,
                                                     previousLevel: 0));

        // And a drained baseclass keeps what it has; it is already earning nothing.
        Assert.Equal(35001u, Levelling.CapExperience(Assassin, 35001, limitLevel: 2,
                                                     previousLevel: 1));
    }

    [Fact]
    public void A_level_cap_holds_the_entitlement_down()
    {
        Assert.Equal(6, Levelling.GetAllowedLevel(Assassin, 35001, previousLevel: 0));
        Assert.Equal(3, Levelling.GetAllowedLevel(Assassin, 35001, previousLevel: 0, levelCap: 3));

        // The sentinel is not a cap of zero, which is the mistake it exists to prevent.
        Assert.Equal(6, Levelling.GetAllowedLevel(Assassin, 35001, previousLevel: 0,
                                                  levelCap: Levelling.NoLevelCap));
    }

    [Fact]
    public void The_baseclass_level_cap_comes_from_a_named_skill()
    {
        Assert.Equal(12, Levelling.GetLevelCapFromSkills(
            [("Turn$SYS$", 3), (Levelling.MaxLevelSkill, 12)]));

        Assert.Equal(Levelling.NoLevelCap, Levelling.GetLevelCapFromSkills([("Turn$SYS$", 3)]));
    }

    [Fact]
    public void The_smaller_of_the_baseclass_and_race_caps_wins()
    {
        // GetLevelCap builds its SKILL_COMPUTATION with minimize = true, and GetSkillValue then
        // takes the lower of the two (class.cpp:5215). An elf-only class capped at 12 by its
        // baseclass and 8 by its race stops at 8.
        Assert.Equal(8, Levelling.CombineLevelCaps(12, 8));
        Assert.Equal(8, Levelling.CombineLevelCaps(8, 12));

        // An absent cap is not a cap of zero -- the other side wins outright.
        Assert.Equal(12, Levelling.CombineLevelCaps(12, Levelling.NoLevelCap));
        Assert.Equal(9, Levelling.CombineLevelCaps(Levelling.NoLevelCap, 9));

        // ...and two absences stay absent rather than collapsing to a number.
        Assert.Equal(Levelling.NoLevelCap,
                     Levelling.CombineLevelCaps(Levelling.NoLevelCap, Levelling.NoLevelCap));
    }

    [Fact]
    public void A_race_cap_holds_a_character_below_what_its_experience_buys()
    {
        // The end-to-end shape: entitled to 6 by experience, held to 4 by a race.
        int cap = Levelling.CombineLevelCaps(Levelling.NoLevelCap,
                                             Levelling.GetLevelCapFromSkills(
                                                 [(Levelling.MaxLevelSkill, 4)]));

        Assert.Equal(4, Levelling.GetAllowedLevel(Assassin, 35001, previousLevel: 0, levelCap: cap));
        Assert.False(Levelling.IsReadyToTrain(Assassin, 35001, currentLevel: 4, previousLevel: 0,
                                              levelCap: cap));
    }

    // ---- against a real design --------------------------------------------------------------

    private static string? SomethingWild()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string path = Path.Combine(dir!.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(path) ? path : null;
    }

    [Fact]
    public void Real_thresholds_read_from_a_design_produce_the_published_levels()
    {
        // The join between the two halves: thresholds read off disk rather than restated, and the
        // experience totals SomethingWild's own pre-generated fighters carry. Those sit just past
        // the classic AD&D fighter breakpoints (2000 / 8000 / 32000), which is independent evidence
        // that the reader, this table and these rules all agree.
        string? design = SomethingWild();
        if (design is null) return;

        string path = Path.Combine(design, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Baseclass));
        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Baseclass, out var body,
                                               out var stream);
        using (stream)
        {
            var records = BaseclassRecordReader.ReadAll(body, header.Count);
            var fighter = records.Single(r => r.Name == "fighter");

            Assert.NotEmpty(fighter.ExperienceLevels);
            Assert.Equal(2, Levelling.GetLevel(fighter.ExperienceLevels, 2001));
            Assert.Equal(4, Levelling.GetLevel(fighter.ExperienceLevels, 8001));
            Assert.Equal(6, Levelling.GetLevel(fighter.ExperienceLevels, 35001));

            // And the assassin's table is the one this file's unit tests are built on.
            var assassin = records.Single(r => r.Name == "assassin");
            Assert.Equal(Assassin, assassin.ExperienceLevels.Take(Assassin.Length));
        }
    }

    [Fact]
    public void Real_races_read_from_a_design_can_be_asked_for_a_cap()
    {
        string? design = SomethingWild();
        if (design is null) return;

        string path = Path.Combine(design, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Race));
        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Race, out var body,
                                               out var stream);
        using (stream)
        {
            using var game = File.OpenRead(Path.Combine(design, "Data", "game.dat"));
            var version = new UAF.Common.DesignVersion(
                DesignFileHeader.Read(game, DesignFileKind.GameData).Version.Value);

            var races = RaceRecordReader.ReadAll(body, header.Count, header.Tag, version);
            var elf = races.Single(r => r.Name == "Elf");

            // SomethingWild's Elf really does define MaxLevel$SYS$, and its value is 40 --
            // HIGHEST_CHARACTER_LEVEL, so "no practical cap" written out explicitly rather than
            // left absent. This was expected to come back NoLevelCap; the corpus says otherwise,
            // which means the race side of the cap is exercised by real data rather than only by
            // the unit tests above.
            int raceCap = Levelling.GetLevelCapFromSkills(
                elf.Skills.Select(s => (s.SkillId, s.Value)));

            Assert.Equal(40, raceCap);

            // Combined with a baseclass that caps lower, the baseclass still wins.
            Assert.Equal(12, Levelling.CombineLevelCaps(12, raceCap));
        }
    }
}
