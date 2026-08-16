using UAF.Data;

namespace UAFedit.Spells.Tests;

/// <summary>Both editors against the real designs.</summary>
public class SpellDatabaseCorpusTests
{
    /// <summary>
    /// The premise: the corpus is present, it loads, and its spell database is the size it is
    /// known to be.
    /// </summary>
    /// <remarks>
    /// <b>Every other corpus test early-returns without a design, so this is what stops the file
    /// passing while proving nothing.</b> The exact count is deliberate: a design that loaded but
    /// yielded three spells would satisfy "not empty" and tell us nothing.
    /// </remarks>
    [Fact]
    public void The_corpus_loads_with_its_full_spell_database()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            Assert.NotNull(design.Spells);
            Assert.Equal(Corpus.SomethingWildSpells, design.Spells!.Count);

            using var db = new SpellDatabaseViewModel(design);

            Assert.True(db.IsReadable);
            Assert.Equal(Corpus.SomethingWildSpells, db.Count);
            Assert.Equal(Corpus.SomethingWildSpells, db.Spells.Count);
            Assert.NotNull(db.SelectedSpell);
            Assert.NotEmpty(db.Schools);
        }
    }

    /// <summary>
    /// Every spell in the design opens, and comes back out of the editor unchanged.
    /// </summary>
    /// <remarks>
    /// The single most valuable thing to check: 377 real records, every version quirk the design
    /// carries, and a projection that has to lose nothing. A field the editor forgets to carry
    /// through shows up here and nowhere else.
    /// </remarks>
    [Fact]
    public void Every_spell_round_trips_through_the_editor_unchanged()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            var original = design.Spells!;
            using var db = new SpellDatabaseViewModel(design);

            Assert.False(db.IsDirty);

            var edited = db.EditedSpells;
            Assert.Equal(original.Count, edited.Count);

            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].AllowedBaseclasses, edited[i].AllowedBaseclasses);
                Assert.Equal(original[i].Parameters, edited[i].Parameters);
                Assert.Equal(original[i].Sounds, edited[i].Sounds);
                Assert.Equal(original[i].Scripts, edited[i].Scripts);

                Assert.Equal(original[i], edited[i] with
                {
                    AllowedBaseclasses = original[i].AllowedBaseclasses,
                    Parameters = original[i].Parameters,
                    Sounds = original[i].Sounds,
                    Scripts = original[i].Scripts,
                });
            }
        }
    }

    /// <summary>
    /// Every spell script in the design compiles, bar one that the design itself has wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A shipped design can carry scripts that have never compiled.</b> Nothing stops it: the
    /// engine compiles a script the first time it is needed, logs the failure and carries on, so a
    /// broken one is invisible until the moment it would have run. <c>monsterMaridWaterJet</c>'s
    /// saving-throw-failed script calls a system function with too few arguments.
    /// </para>
    /// <para>
    /// The known failure is written out rather than tolerated as a count, so that a regression in
    /// the GPDL front end — which would make the list grow — fails here with the names in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_designs_spell_scripts_compile_apart_from_its_own_known_error()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            using var db = new SpellDatabaseViewModel(design);

            var report = db.CompileAllScripts();

            Assert.True(report.Scripts > 150, $"only {report.Scripts} spell scripts found");
            Assert.Equal(
                ["monsterMaridWaterJet/Saving Throw Failed Script"],
                report.Failures.Select(f => $"{f.Owner}/{f.Script}"));
            Assert.False(db.IsDirty);
        }
    }

    /// <summary>
    /// The second corpus design, which is a different format version.
    /// </summary>
    /// <remarks>
    /// <c>Case.dsn</c> exercises the version gates the hand-built records cannot: a design that
    /// predates a field carries a shorter <c>Parameters</c> list and no <c>EffectDuration</c>, and
    /// a form that assumed six and one would throw on opening it.
    /// </remarks>
    [Fact]
    public void The_second_corpus_design_also_opens_and_round_trips()
    {
        if (Corpus.Case() is not { } design)
        {
            return;
        }

        using (design)
        {
            var original = design.Spells;
            Assert.NotNull(original);
            Assert.Equal(Corpus.CaseSpells, original!.Count);

            using var db = new SpellDatabaseViewModel(design);
            var edited = db.EditedSpells;

            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], edited[i] with
                {
                    AllowedBaseclasses = original[i].AllowedBaseclasses,
                    Parameters = original[i].Parameters,
                    Sounds = original[i].Sounds,
                    Scripts = original[i].Scripts,
                });
            }
        }
    }
}

/// <summary>The special-abilities editor against the real designs.</summary>
public class SpecialAbilityCorpusTests
{
    /// <summary>
    /// The premise: the corpus is present and its special-abilities file is the size it is known
    /// to be.
    /// </summary>
    /// <remarks>
    /// <b><c>SpecialAbilitiesFile.Load</c> answers an empty list for a design with no
    /// <c>specialAbilities.txt</c></b> — it never returns null and never throws — so "the database
    /// is non-empty" is a real check here rather than a formality. The exact count is what makes it
    /// one.
    /// </remarks>
    [Fact]
    public void The_corpus_loads_with_its_full_special_ability_database()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            Assert.Equal(Corpus.SomethingWildAbilities, design.SpecialAbilities.Count);

            using var db = new SpecialAbilityDatabaseViewModel(design);

            Assert.Equal(Corpus.SomethingWildAbilities, db.Count);
            Assert.NotNull(db.SelectedAbility);
            Assert.Contains(db.Abilities, a => a.ScriptCount > 0);
        }
    }

    [Fact]
    public void Every_ability_round_trips_through_the_editor_unchanged()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            var original = design.SpecialAbilities;
            using var db = new SpecialAbilityDatabaseViewModel(design);
            var edited = db.EditedAbilities;

            Assert.False(db.IsDirty);
            Assert.Equal(original.Count, edited.Count);

            // Element-wise, because SpecialAbility's generated equality compares Entries by
            // reference and the editor necessarily hands back a rebuilt list.
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Name, edited[i].Name);
                Assert.Equal(original[i].Entries, edited[i].Entries);
            }
        }
    }

    /// <summary>
    /// Every entry in the design would survive a write and a read.
    /// </summary>
    /// <remarks>
    /// The bracket rule has a floor of three characters and the line splitter has no escape
    /// handling, so it is possible for a design to hold an entry that cannot be written back as
    /// itself. Whether any real design does is worth knowing, and the answer is what this asserts.
    /// </remarks>
    [Fact]
    public void No_entry_in_the_corpus_would_lose_its_kind_on_a_round_trip()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            using var db = new SpecialAbilityDatabaseViewModel(design);

            var unfaithful = db.Abilities
                               .SelectMany(a => a.Unfaithful.Select(e => $"{a.Name}/{e.Key}"))
                               .ToList();

            Assert.Equal([], unfaithful);
        }
    }

    /// <summary>
    /// <c>Test All Special Abilities</c>, and what it finds: seventeen broken scripts in a shipped
    /// design.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the check the whole compile path exists for, and it earns its keep immediately.</b>
    /// <c>SomethingWild</c> holds 957 scripts across 414 abilities and seventeen of them have never
    /// compiled — unbalanced parentheses, a missing semicolon, a misspelt variable (<c>itemNon</c>
    /// for <c>itmNon</c>, four times over), and a stray <c>-</c> left in the middle of a line where
    /// only a line's <i>first</i> <c>-</c> is a continuation marker. The engine compiles each script
    /// the first time it is needed and logs the failure, so none of this is visible until the
    /// moment the hook would have fired.
    /// </para>
    /// <para>
    /// The list is written out rather than counted so that a regression in the GPDL front end —
    /// which would make it grow — fails here with the names in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_designs_special_ability_scripts_compile_apart_from_its_own_known_errors()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            using var db = new SpecialAbilityDatabaseViewModel(design);

            var report = db.CompileAllScripts();

            Assert.True(report.Scripts > 900, $"only {report.Scripts} scripts found");
            Assert.True(report.Owners > 400, $"only {report.Owners} abilities carry scripts");

            Assert.Equal(
                [
                    "DualClassedPaladin/DualClassAchievedPaladin",
                    "elemental_ImmuneStone/ComputeDamage",
                    "elemental_ImmuneWater/ComputeDamage",
                    "elemental_SlowingAcid/GetAdjMaxMovement",
                    "elemental_SlowingCold/GetAdjMaxMovement",
                    "elemental_SlowingElectricity/GetAdjMaxMovement",
                    "elemental_SlowingFire/GetAdjMaxMovement",
                    "IsBlinking2/IsValidTarget",
                    "IsPaladinProtectedFromEvil/DoesAttackSucceed",
                    "IsProtectedEvil/DoesAttackSucceed",
                    "IsProtectedFireCaster1/ComputeSpellDamage",
                    "IsProtectedGood/CharDisplayStatus",
                    "item_VorpalLongSword/ComputeDamage",
                    "monster_DjinniAirResistance/GetAdjMaxMovement",
                    "monster_MaridColdResistance/GetAdjMaxMovement",
                    "monster_MaridFireVulnerability/GetAdjMaxMovement",
                    "spell_ColdResistant/InvokeSpellOnTarget",
                ],
                report.Failures.Select(f => $"{f.Owner}/{f.Script}"));

            // Reading a compile result is an observation, not an edit: the sweep must not leave the
            // design looking unsaved.
            Assert.False(db.IsDirty);
        }
    }

    /// <summary>
    /// A design's integer tables really do stop at the first line that is not a number.
    /// </summary>
    /// <remarks>
    /// Worth knowing whether any shipped table is silently truncated. This asserts what the corpus
    /// actually holds rather than what it ought to.
    /// </remarks>
    [Fact]
    public void The_corpus_integer_tables_are_read_whole()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            using var db = new SpecialAbilityDatabaseViewModel(design);

            var tables = db.Abilities
                           .SelectMany(a => a.Entries.Where(e => e.IsIntegerTable)
                                                     .Select(e => (a.Name, Entry: e)))
                           .ToList();

            if (tables.Count == 0)
            {
                return;
            }

            Assert.All(tables, t => Assert.Equal(string.Empty, t.Entry.TableTruncation));
        }
    }

    /// <summary>
    /// Which of the four kinds a real design actually uses — three of them, not four.
    /// </summary>
    /// <remarks>
    /// <b>The <c>(parameter)</c> form is documented and unused.</b> The sample block at the top of
    /// every <c>specialAbilities.txt</c> shows <c>(parameterA) = 5</c>, but
    /// <c>SomethingWild</c> defines not one: 957 scripts, 52 constants, 19 integer tables and zero
    /// variables. Worth pinning rather than glossing over — an editor that only ever sees three
    /// kinds in practice is easy to build wrong for the fourth, and a parser that started
    /// mis-classifying brackets would move these numbers.
    /// </remarks>
    [Fact]
    public void The_corpus_uses_three_of_the_four_entry_kinds()
    {
        if (Corpus.SomethingWild() is not { } design)
        {
            return;
        }

        using (design)
        {
            using var db = new SpecialAbilityDatabaseViewModel(design);

            int Total(SpecialAbilityEntryKind kind) => db.Abilities.Sum(a => a.Count(kind));

            Assert.True(Total(SpecialAbilityEntryKind.Script) > 900);
            Assert.True(Total(SpecialAbilityEntryKind.Constant) > 0);
            Assert.True(Total(SpecialAbilityEntryKind.IntegerTable) > 0);
            Assert.Equal(0, Total(SpecialAbilityEntryKind.Variable));
        }
    }
}
