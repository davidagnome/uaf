using UAF.Media.Sdl;
using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The six spell-book calls against a loaded design.
/// </summary>
/// <remarks>
/// These need a real spell database and a real character book, so unlike most of the GPDL families
/// there is nothing useful to test against a fake — the interesting behaviour is in how the
/// reference's own data comes back out.
/// </remarks>
public class GameScriptHostSpellbookTests
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

    /// <summary>
    /// A party member with a book built from the design's own spell database.
    /// </summary>
    /// <remarks>
    /// <b>The book has to be built, because neither corpus design ships a party that knows a single
    /// spell.</b> Both start six characters and every one of their books is empty — so a test
    /// looking for a shipped caster would find none and early-return, passing while proving
    /// nothing. That is what the first version of this file did.
    /// <para>
    /// What is real here is the part that matters: the 377-spell database, its schools and levels,
    /// and the live <c>SpellList</c> the host reads and writes. Only "who happens to know these"
    /// is arranged.
    /// </para>
    /// </remarks>
    private static (Game Game, GameScriptHost Host, Character Who)? Caster()
    {
        if (Load() is not { } game
            || game.Party.Members.Count == 0
            || game.Design.Spells is not { Count: > 0 } database)
        {
            return null;
        }

        var who = game.Party.Members[0];

        // Two schools and two levels within one of them, so the grouping has something to group.
        foreach (var record in database
                     .GroupBy(r => r.SchoolId)
                     .Take(2)
                     .SelectMany(g => g.OrderBy(r => r.Level).DistinctBy(r => r.Level).Take(2)))
        {
            who.Book.Add(record.Name, record.Level).Selected = 1;
        }

        return who.Book.Entries.Count == 0 ? null : (game, new GameScriptHost(game), who);
    }

    /// <summary>
    /// The premise the rest of the file rests on: a real database, and a book built from it.
    /// </summary>
    /// <remarks>
    /// Every test below early-returns without one, so without this they would all pass on a
    /// checkout with no <c>reference/</c> and prove nothing. It has already caught that once.
    /// </remarks>
    [Fact]
    public void The_corpus_has_a_spell_database_to_build_a_book_from()
    {
        if (Load() is not { } game)
        {
            return;
        }

        // The design ships hundreds of spells across several schools...
        Assert.NotNull(game.Design.Spells);
        Assert.True(game.Design.Spells!.Count > 100,
                    $"only {game.Design.Spells.Count} spells decoded");
        Assert.True(game.Design.Spells.Select(r => r.SchoolId).Distinct().Count() > 1);

        // ...and none of it is in anybody's book, which is why Caster() builds one.
        Assert.All(game.Party.Members, m => Assert.Empty(m.Book.Entries));

        var caster = Caster();
        Assert.NotNull(caster);
        Assert.True(caster!.Value.Who.Book.Entries.Count > 1);

        // Every spell in the built book is one the database really has.
        Assert.All(caster.Value.Who.Book.Entries,
                   e => Assert.NotNull(caster.Value.Game.Design.Spell(e.SpellId)));
    }

    /// <summary>A spell's level and dispellability come off the database, not off a caster.</summary>
    [Fact]
    public void A_spell_field_reads_the_database()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        foreach (var entry in caster.Who.Book.Entries)
        {
            if (caster.Game.Design.Spell(entry.SpellId) is not { } record)
            {
                continue;
            }

            Assert.Equal(record.Level,
                         caster.Host.SpellField(entry.SpellId, GpdlSpellField.Level));
            Assert.Equal(record.CanBeDispelled,
                         caster.Host.SpellField(entry.SpellId, GpdlSpellField.CanBeDispelled));
            return;
        }
    }

    /// <summary>A spell the design does not have is zero rather than an error.</summary>
    [Fact]
    public void An_unknown_spell_is_zero()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        Assert.Equal(0, caster.Host.SpellField("NoSuchSpell", GpdlSpellField.Level));
        Assert.Equal(0, caster.Host.SpellField("NoSuchSpell", GpdlSpellField.CanBeDispelled));
    }

    /// <summary>
    /// Separators that cannot appear in a spell name.
    /// </summary>
    /// <remarks>
    /// <b>Not "ABCD".</b> Spell names are ordinary words — "Cure Light Wounds", "Curse" — so
    /// letters used as separators also occur inside the content, and an assertion about where a
    /// separator appears would be measuring the names instead. Control characters cannot collide.
    /// </remarks>
    private const string Separators = "\u0001\u0002\u0003\u0004";

    private const char School = '\u0001';
    private const char Level = '\u0002';
    private const char Spell = '\u0003';
    private const char Field = '\u0004';

    /// <summary>How many times a two-character mark appears.</summary>
    private static int Marks(string book, char first, char second) =>
        book.Split($"{first}{second}").Length - 1;

    /// <summary>
    /// The book comes back with every spell the caster knows, separated as asked.
    /// </summary>
    [Fact]
    public void The_spellbook_lists_what_the_caster_knows()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        string book = caster.Host.Spellbook(caster.Who.CharacterId, Separators);

        Assert.NotEqual(string.Empty, book);

        // Every spell the design knows about appears, and each carries its two counts after it.
        foreach (var entry in caster.Who.Book.Entries)
        {
            if (caster.Game.Design.Spell(entry.SpellId) is null)
            {
                continue;
            }

            Assert.Contains(
                $"{Spell}{Field}{entry.SpellId}{Field}{entry.Selected}{Field}{entry.Memorized}",
                book, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A school is introduced once and a level once within it, not repeated per spell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The grouping is the whole shape of the string.</b> A version that emitted the school
    /// before every spell would contain all the same substrings and still be unparseable by a
    /// design's script.
    /// </para>
    /// <para>
    /// <b>Each mark is a two-character PAIR, and the pairs overlap.</b> A school is
    /// <c>[0][1]</c>, a level is <c>[1][2]</c>, a spell is <c>[2][3]</c> — so every mark shares a
    /// character with its neighbour and counting a single separator counts schools <i>and</i>
    /// levels together. That is why this counts sequences.
    /// </para>
    /// </remarks>
    [Fact]
    public void Schools_and_levels_are_introduced_once_each()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var known = caster.Who.Book.Entries
            .Select(e => (e, r: caster.Game.Design.Spell(e.SpellId)))
            .Where(x => x.r is not null)
            .ToList();

        if (known.Count == 0)
        {
            return;
        }

        string book = caster.Host.Spellbook(caster.Who.CharacterId, Separators);

        // The school mark appears once per DISTINCT school, not once per spell.
        int schools = Marks(book, School, Level);
        Assert.Equal(known.Select(x => x.r!.SchoolId).Distinct().Count(), schools);

        // Likewise a level is introduced once within its school.
        Assert.Equal(known.Select(x => (x.r!.SchoolId, x.e.Level)).Distinct().Count(),
                     Marks(book, Level, Spell));

        // And once per spell for the spell mark, which is what the other two are not.
        Assert.Equal(known.Count, Marks(book, Spell, Field));

        // And there are strictly more spells than school marks, so the two are not equal by
        // accident -- which is the whole risk in counting separators.
        Assert.True(known.Count > schools,
                    $"{known.Count} spells across {schools} schools proves nothing");
    }

    /// <summary>
    /// Fewer than four separators is not a buffer over-read here.
    /// </summary>
    /// <remarks>
    /// <b>A divergence.</b> The reference indexes <c>delimiters[0]</c> through <c>[3]</c> with no
    /// length check, so a design passing two reads past the end of its own string. Missing ones are
    /// simply absent here — the text is ambiguous, but it is the design's text rather than whatever
    /// followed it in memory.
    /// </remarks>
    [Fact]
    public void Fewer_than_four_separators_does_not_read_past_the_end()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        string book = caster.Host.Spellbook(caster.Who.CharacterId, Separators[..2]);

        // It produced something, and used only the two separators it was given.
        Assert.NotEqual(string.Empty, book);
        Assert.Contains(School, book);
        Assert.DoesNotContain(Spell, book);
        Assert.DoesNotContain(Field, book);

        // The spells are all still there, just no longer separable from their counts.
        Assert.All(caster.Who.Book.Entries,
                   e => Assert.Contains(e.SpellId, book, StringComparison.Ordinal));
    }

    /// <summary>
    /// A caster nobody recognises has an empty book rather than throwing.
    /// </summary>
    [Fact]
    public void An_unknown_caster_has_an_empty_book()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        Assert.Equal(string.Empty, caster.Host.Spellbook("NoSuchCharacter", Separators));
    }

    /// <summary>
    /// Selecting a spell increments its count and checks nothing.
    /// </summary>
    /// <remarks>
    /// <b>A bare <c>selected++</c>.</b> No test against what the caster may hold at that level, and
    /// no upper bound — a script calling it in a loop really does queue that many copies.
    /// </remarks>
    [Fact]
    public void Selecting_increments_without_checking()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var entry = caster.Who.Book.Entries[0];
        int before = entry.Selected;

        Assert.True(caster.Host.SelectSpell(caster.Who.CharacterId, entry.SpellId));
        Assert.Equal(before + 1, entry.Selected);

        // Ten more, unbounded.
        for (int i = 0; i < 10; i++)
        {
            caster.Host.SelectSpell(caster.Who.CharacterId, entry.SpellId);
        }

        Assert.Equal(before + 11, entry.Selected);

        // A spell the caster does not know is refused rather than added.
        int count = caster.Who.Book.Entries.Count;
        Assert.False(caster.Host.SelectSpell(caster.Who.CharacterId, "NoSuchSpell"));
        Assert.Equal(count, caster.Who.Book.Entries.Count);
    }

    /// <summary>
    /// An empty adjustment reads the ready count without changing it.
    /// </summary>
    /// <remarks>
    /// The only way a script can ask how many copies are ready — there is no getter.
    /// </remarks>
    [Fact]
    public void An_empty_adjustment_reads_without_writing()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var entry = caster.Who.Book.Entries[0];
        entry.Memorized = 4;

        Assert.Equal(4, caster.Host.SetMemorizeCount(
            caster.Who.CharacterId, entry.SpellId, string.Empty));
        Assert.Equal(4, entry.Memorized);
    }

    /// <summary>
    /// A leading sign makes the adjustment relative; anything else is absolute.
    /// </summary>
    [Theory]
    [InlineData("3", 3)]
    [InlineData("+2", 7)]
    [InlineData("-2", 3)]
    [InlineData("0", 0)]
    public void A_leading_sign_is_what_makes_it_relative(string adjustment, int expected)
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var entry = caster.Who.Book.Entries[0];
        entry.Memorized = 5;

        Assert.Equal(expected, caster.Host.SetMemorizeCount(
            caster.Who.CharacterId, entry.SpellId, adjustment));
        Assert.Equal(expected, entry.Memorized);
    }

    /// <summary>
    /// The count floors at zero, which is what makes -1 mean "no such spell".
    /// </summary>
    [Fact]
    public void The_count_floors_at_zero_so_minus_one_means_no_such_spell()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var entry = caster.Who.Book.Entries[0];
        entry.Memorized = 1;

        Assert.Equal(0, caster.Host.SetMemorizeCount(
            caster.Who.CharacterId, entry.SpellId, "-9"));

        // So the only way to see -1 is a spell the caster does not know.
        Assert.Equal(-1, caster.Host.SetMemorizeCount(
            caster.Who.CharacterId, "NoSuchSpell", "1"));
        Assert.Equal(-1, caster.Host.SetMemorizeCount(
            "NoSuchCharacter", entry.SpellId, "1"));
    }

    /// <summary>
    /// <c>$SetMemorizeCount</c> writes the ready count, not the wanted one.
    /// </summary>
    /// <remarks>
    /// <b>Despite the name.</b> "Memorize count" reads as the number queued for memorising, which
    /// is <c>selected</c>; the reference assigns <c>memorized</c> — the number already ready to
    /// cast. So it hands a caster loaded spells outright rather than asking for them to be studied.
    /// </remarks>
    [Fact]
    public void It_writes_the_ready_count_not_the_wanted_one()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var entry = caster.Who.Book.Entries[0];
        entry.Memorized = 0;
        entry.Selected = 1;

        caster.Host.SetMemorizeCount(caster.Who.CharacterId, entry.SpellId, "6");

        Assert.Equal(6, entry.Memorized);
        Assert.Equal(1, entry.Selected);
    }

    /// <summary>
    /// <c>$Memorize</c> finishes everything outstanding at once rather than spending a minute.
    /// </summary>
    /// <remarks>
    /// The call is <c>IncAllMemorizedTime(0, TRUE)</c> — zero minutes, and the flag does the work.
    /// </remarks>
    [Fact]
    public void Memorizing_finishes_what_was_wanted()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        var entry = caster.Who.Book.Entries[0];
        entry.Memorized = 0;
        entry.Selected = 2;

        caster.Host.Memorize(caster.Who.CharacterId);

        // No time passed, and yet it is ready.
        Assert.Equal(2, entry.Memorized);
    }
}
