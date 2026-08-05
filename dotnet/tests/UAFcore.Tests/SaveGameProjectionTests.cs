using UAF.Media;
using UAF.Media.Sdl;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Saves a running game to disk and loads it back — the whole path, through a real file.
/// </summary>
/// <remarks>
/// <para>
/// Reading and writing a <c>.pty</c> were finished in Phase 1 and tested against the corpus; what
/// these cover is the piece between, which is where a port loses things quietly. A field that is
/// never projected reads back as whatever the design said, which looks like a working save right
/// up until the thing the player changed is the thing that reverted.
/// </para>
/// <para>
/// The design is gitignored, so these return early without <c>reference/</c>.
/// </para>
/// </remarks>
public class SaveGameProjectionTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), $"uaf-save-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static string? DesignRoot()
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

        string design = Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(design) ? design : null;
    }

    private static LoadedDesign? Open() =>
        DesignRoot() is { } root
            ? LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer())
            : null;

    // ---- the projection itself -----------------------------------------------------------------

    [Fact]
    public void A_saved_game_carries_the_partys_position_and_clock()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        game.Update(InputEvent.KeyDown(VirtualKey.Up));

        var save = SaveGameProjection.From(game);

        Assert.Equal(game.X, save.Party.PosX);
        Assert.Equal(game.Y, save.Party.PosY);
        Assert.Equal((byte)game.Facing, save.Party.Facing);
        Assert.Equal(game.Minutes, SaveGameProjection.MinutesOf(save.Party));
        Assert.Equal(game.Party.Count, save.Party.CharacterCount);
    }

    [Fact]
    public void The_clock_splits_and_rejoins()
    {
        // Days are 1-based and the split has to be reversible, or every save shifts the calendar
        // by a day.
        var party = new PartyState([], Days: 3, Hours: 7, Minutes: 42, 0, "", 1, 0, 0,
                                   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal((2 * 1440) + (7 * 60) + 42, SaveGameProjection.MinutesOf(party));
    }

    [Fact]
    public void A_character_keeps_the_forty_fields_nothing_touched()
    {
        // The whole reason a Character wraps its record rather than replacing it: alignment,
        // ability scores, the icon and the spell book survive because nothing projected them.
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        if (game.Party.Count == 0)
        {
            return;
        }

        var before = game.Party.Members[0].Record;
        var after = SaveGameProjection.From(game).Characters[0];

        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Alignment, after.Alignment);
        Assert.Equal(before.Abilities, after.Abilities);
        Assert.Equal(before.Thac0, after.Thac0);
        Assert.Equal(before.SpellBook, after.SpellBook);
    }

    [Fact]
    public void A_character_carries_what_play_changed()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        if (game.Party.Count == 0)
        {
            return;
        }

        var member = game.Party.Members[0];
        member.HitPoints = 3;
        member.MaxHitPoints = 40;
        member.Purse.Add(game.Money.BaseType, 175);

        var record = SaveGameProjection.From(game).Characters[0];

        Assert.Equal(3, record.HitPoints);
        Assert.Equal(40, record.MaxHitPoints);
        Assert.Equal(175, record.Money!.Coins[MoneyRules.IndexOf(game.Money.BaseType)]);
    }

    [Fact]
    public void A_baseclass_keeps_its_pre_drain_level()
    {
        // The engine owns three of the five fields; PreDrainLevel belongs to level drain, which is
        // not ported. Defaulting it would restore a drained character on the first save.
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        var member = game.Party.Members.FirstOrDefault(m => m.Baseclasses.Count > 0);
        if (member is null)
        {
            return;
        }

        int index = game.Party.Members.ToList().IndexOf(member);
        string id = member.Baseclasses[0].BaseclassId;
        int preDrain = member.Record.BaseclassStats.First(s => s.BaseclassId == id).PreDrainLevel;

        member.Baseclasses[0].CurrentLevel = 9;

        var stats = SaveGameProjection.From(game).Characters[index]
                        .BaseclassStats.First(s => s.BaseclassId == id);

        Assert.Equal(9, stats.CurrentLevel);
        Assert.Equal(preDrain, stats.PreDrainLevel);
    }

    [Fact]
    public void A_quest_keeps_its_name_and_carries_its_stage()
    {
        // WorldState holds only id -> state, so a projection built from it alone would save a
        // quest with no name.
        using var design = Open();
        if (design is null || design.Globals.Quests.Count == 0)
        {
            return;
        }

        var game = new Game(design);
        int id = design.Globals.Quests[0].Id;
        game.World.SetQuest(id, QuestState.Complete, stage: 4);

        var saved = SaveGameProjection.From(game).Quests.First(q => q.Id == id);

        Assert.Equal(design.Globals.Quests[0].Name, saved.Name);
        Assert.Equal((int)QuestState.Complete, saved.State);
        Assert.Equal(4, saved.Stage);
    }

    // ---- through a real file -------------------------------------------------------------------

    [Fact]
    public void A_game_saves_to_a_slot_and_loads_back()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        Directory.CreateDirectory(scratch);

        game.Update(InputEvent.KeyDown(VirtualKey.Up));
        game.TriggerFlags.MarkHappened(0, 42);
        game.Visited.SetVisited(0, 11, 12);
        game.Clearances.Clear(0, 3, 4, Facing.East, Clearable.Secret);
        game.Vaults.Deposit(2, new ItemInstance(0, "Long Sword", 0, Inventory.NotReady,
                                                1, 1, 0, 0, 0));

        (int x, int y, int minutes) = (game.X, game.Y, game.Minutes);

        Assert.Null(SaveInto(game, 0));

        // A second game over the same design, loading what the first wrote.
        var loaded = new Game(design);
        Assert.Null(LoadFrom(loaded, 0));

        Assert.Equal(x, loaded.X);
        Assert.Equal(y, loaded.Y);
        Assert.Equal(minutes, loaded.Minutes);
        Assert.True(loaded.TriggerFlags.HasHappened(0, 42));
        Assert.True(loaded.Visited.IsVisited(0, 11, 12));
        Assert.False(loaded.Clearances.IsBlocked(0, 3, 4, Facing.East, Clearable.Secret));
        Assert.Equal("Long Sword", loaded.Vaults.ItemsIn(2)[0].ItemId);
    }

    [Fact]
    public void A_wounded_party_is_still_wounded_after_a_load()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        if (game.Party.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(scratch);

        game.Party.Members[0].HitPoints = 2;
        string name = game.Party.Members[0].Name;
        int count = game.Party.Count;

        Assert.Null(SaveInto(game, 3));

        var loaded = new Game(design);
        Assert.Null(LoadFrom(loaded, 3));

        Assert.Equal(count, loaded.Party.Count);
        Assert.Equal(name, loaded.Party.Members[0].Name);
        Assert.Equal(2, loaded.Party.Members[0].HitPoints);
    }

    [Fact]
    public void Loading_an_empty_slot_says_so_rather_than_throwing()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        Directory.CreateDirectory(scratch);

        string? reason = LoadFrom(game, 9);

        Assert.NotNull(reason);
        Assert.Contains("no saved game", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_slot_that_is_not_a_slot_is_refused()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);

        Assert.NotNull(SaveInto(game, 99));
        Assert.NotNull(LoadFrom(game, -1));
    }

    [Fact]
    public void Saving_creates_the_directory_a_new_design_does_not_have()
    {
        // A design that has never been played has no Saves folder; CreateSaveDirectory exists in
        // the reference for the same reason.
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        Assert.False(Directory.Exists(scratch));

        Assert.Null(SaveInto(game, 0));

        Assert.True(File.Exists(Path.Combine(scratch, SaveSlots.FileName(0))));
    }

    [Fact]
    public void The_save_screen_sees_the_slot_that_was_just_written()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var game = new Game(design);
        Assert.Null(SaveInto(game, 5));

        var slots = SaveSlots.Under(scratch);

        Assert.True(slots[5].Exists);
        Assert.True(SaveSlots.Any(slots));
        Assert.False(slots[4].Exists);
    }

    [Fact]
    public void A_game_saved_on_one_level_loads_back_onto_it()
    {
        // The loose end saving left behind: LoadFrom restores the party but the map was whatever
        // was already open, so a cross-level load put everyone on the wrong grid.
        using var design = Open();
        if (design is null || design.LevelFiles.Count < 2)
        {
            return;
        }

        var game = new Game(design, levelIndex: 1);
        int level = game.LevelIndex;
        var (x, y) = (game.X, game.Y);

        Assert.Null(SaveInto(game, 0));

        // A fresh game on a different level, which is the state a load has to correct.
        var reopened = new Game(design, levelIndex: 0);
        Assert.NotEqual(level, reopened.LevelIndex);

        Assert.Null(LoadFrom(reopened, 0));

        Assert.Equal(level, reopened.LevelIndex);
        Assert.Equal((x, y), (reopened.X, reopened.Y));
    }

    [Fact]
    public void The_level_load_does_not_move_the_party()
    {
        // The reference stashes the square before LoadLevel and puts it back after, so where the
        // party stands comes from the save and never from the level.
        using var design = Open();
        if (design is null || design.LevelFiles.Count < 2)
        {
            return;
        }

        var game = new Game(design, levelIndex: 1);
        var start = (game.X, game.Y);

        // Walk somewhere the level's own defaults would not have put anyone.
        for (int i = 0; i < 12 && (game.X, game.Y) == start; i++)
        {
            game.Update(InputEvent.KeyDown(i % 4 == 3 ? VirtualKey.Right : VirtualKey.Up));
        }

        if ((game.X, game.Y) == start)
        {
            return;             // walled in; the other test still covers the level itself
        }

        var moved = (game.X, game.Y);
        Assert.Null(SaveInto(game, 0));

        var reopened = new Game(design, levelIndex: 0);
        Assert.Null(LoadFrom(reopened, 0));

        Assert.Equal(moved, (reopened.X, reopened.Y));
    }

    [Fact]
    public void Saving_is_no_longer_refused()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        Assert.Empty(SaveGameProjection.Untracked);
        Assert.True(SaveGameProjection.CanSave(new Game(design), out string reason), reason);
    }

    /// <summary>
    /// Saves into the scratch directory rather than beside the design, which is read-only here
    /// and shared with every other test.
    /// </summary>
    private string? SaveInto(Game game, int slot) => Redirected(game, () => game.SaveToSlot(slot));

    private string? LoadFrom(Game game, int slot) => Redirected(game, () => game.LoadFromSlot(slot));

    /// <remarks>
    /// <see cref="Game.SaveDirectory"/> is derived from the design's root, so a test that wrote
    /// through it would litter the corpus. The scratch copy is swapped in for the call.
    /// </remarks>
    private string? Redirected(Game game, Func<string?> call)
    {
        string original = game.SaveDirectory;
        game.SaveDirectory = scratch;
        try
        {
            return call();
        }
        finally
        {
            game.SaveDirectory = original;
        }
    }
}
