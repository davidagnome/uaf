using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Covers writing <c>SPELL_DATA</c> — and, through it, <c>DICEPLUS</c> and
/// <c>SPELL_EFFECTS_DATA</c> — by reading every written record back.
/// </summary>
/// <remarks>
/// The reader is the specification, as it is for the other two record types: it was walked against
/// four real designs and against the C++ oracle, so agreeing with it is the strongest claim
/// available without a writing oracle. Records are built with named arguments throughout, because a
/// thirty-five-field positional constructor will happily put the cast cost where the level goes and
/// still compile.
/// </remarks>
public class SpellRecordWriterTests
{
    private static readonly DesignVersion Modern = SpellRecordWriter.WrittenVersion;

    private static MemoryStream Written(Action<IArchiveWriteCursor> write)
    {
        var stream = new MemoryStream();
        write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)));
        stream.Position = 0;
        return stream;
    }

    private static IArchiveCursor Reading(MemoryStream stream) =>
        ArchiveCursor.For(new MfcArchiveReader(stream));

    private static DicePlus Expression(string text = "2d6+1", string binary = "") =>
        new(DicePlusReader.TagText, text, binary, 0, 0, 0, 0, 0, 0, []);

    private static PicRecord Art(string file) => new(
        PicType: 3, FileName: file, TimeDelay: 120, NumFrames: 4,
        FrameWidth: 64, FrameHeight: 48, Flags: 0x11, MaxLoops: 7,
        Style: 2, UseAlpha: 1, AlphaValue: 0xBEEF, RestartFrame: 2);

    private static SpellEffect Effect(
        string key = "$CHAR_HITPOINTS",
        IReadOnlyList<string>? scripts = null,
        DicePlus? changeData = null) => new(
            IndexKey: key,
            Flags: 0x2004,
            ChangeResult: -1.2345678901234568e18,     // the "no change" sentinel
            String2: "activation source",
            SourceOfEffect: 0xDEADBEEF,
            Parent: 17,
            Scripts: scripts ?? ["s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10", "s11"],
            StopTime: 900,
            Data: 5,
            ChangeData: changeData ?? Expression("1d4"));

    private static SpellRecord Spell(
        string name = "Fireball",
        IReadOnlyList<DicePlus>? parameters = null,
        IReadOnlyList<SpellEffect>? effects = null,
        PicRecord? castArt = null,
        IReadOnlyList<PicRecord>? art = null,
        IReadOnlyList<string>? sounds = null,
        IReadOnlyList<SpellScript>? scripts = null,
        SpecabBlock? specialAbilities = null) => new(
            PreSpellNameKey: 42,
            Name: name,
            CastSound: "cast.wav",
            SchoolId: "Magic User",
            AllowedBaseclasses: ["magicUser", "cleric"],
            Level: 3,
            CastingTime: 5,
            CastingTimeType: 1,
            CanTargetFriend: 0,
            CanTargetEnemy: 1,
            IsCumulative: 1,
            Restrictions: 2,
            CanBeDispelled: 1,
            CanMemorize: 1,
            AllowScribe: 1,
            AutoScribe: 0,
            Lingers: 1,
            LingerOnceOnly: 0,
            SaveVersus: 3,
            SaveResult: 1,
            Targeting: 2,
            DurationRate: 4,
            CastCost: 250,
            CastPriority: 7,
            Parameters: parameters ?? [
                Expression("6d6"), Expression("1"), Expression("2"),
                Expression("3"), Expression("4"), Expression("5")],
            Effects: effects ?? [Effect()],
            CastArt: castArt ?? Art("cast.png"),
            Art: art ?? [Art("missile.png"), Art("coverage.png"), Art("hit.png"), Art("linger.png")],
            Sounds: sounds ?? ["missile.wav", "coverage.wav", "hit.wav", "linger.wav"],
            CastMessage: "/c hurls a fireball at /t.",
            Scripts: scripts ?? [.. Enumerable.Range(0, SpellRecordReader.SpellScriptCount)
                                            .Select(i => new SpellScript($"src{i}", $"bin{i}"))],
            EffectDuration: Expression("3d4"),
            SpecialAbilities: specialAbilities ?? new SpecabBlock([], [], []),
            Attributes: []);

    private static SpellRecord RoundTrip(SpellRecord spell)
    {
        var stream = Written(w => SpellRecordWriter.Write(w, spell));
        var read = SpellRecordReader.Read(Reading(stream), Modern, ArchiveRole.Editor);

        // Exact exhaustion: a field written at the wrong width leaves bytes over or runs off the
        // end, and either shows up here rather than as a puzzling value further in.
        Assert.Equal(stream.Length, stream.Position);
        return read;
    }

    // ---- DICEPLUS ------------------------------------------------------------------------------

    [Fact]
    public void An_expression_round_trips_as_DP2()
    {
        var read = DicePlusReader.Read(Reading(
            Written(w => DicePlusWriter.Write(w, Expression("2d6+1", "compiled")))));

        Assert.Equal(DicePlusReader.TagText, read.Tag);
        Assert.Equal("2d6+1", read.Text);
        Assert.Equal("compiled", read.Binary);
    }

    [Fact]
    public void An_empty_expression_goes_out_through_the_blank_convention()
    {
        // Both strings are written with AS, so empty becomes "*" and comes back empty.
        var read = DicePlusReader.Read(Reading(
            Written(w => DicePlusWriter.Write(w, DicePlusWriter.Empty))));

        Assert.Equal(DicePlusWriter.Empty, read);
    }

    [Theory]
    [InlineData("DP0")]
    [InlineData("DP1")]
    public void A_numeric_expression_is_refused_rather_than_written_with_no_text(string tag)
    {
        // The reference synthesises m_Text from the packed fields as it loads
        // (EncodeOldDicePlusText); this port does not, so writing one as DP2 would emit an empty
        // expression -- a file that reads back cleanly with the dice silently gone.
        var legacy = new DicePlus(tag, string.Empty, string.Empty, 2, 6, 1, -999, 999, 1, []);

        Assert.False(DicePlusWriter.CanWrite(legacy, out string reason));
        Assert.Contains(tag, reason);
        Assert.Throws<NotSupportedException>(
            () => Written(w => DicePlusWriter.Write(w, legacy)));
    }

    [Fact]
    public void Only_three_strings_go_out_and_the_numeric_fields_never_do()
    {
        // The reference's whole numeric path is commented out beneath the DP2 write
        // (class.cpp:2505), so an expression is a tag and two strings and nothing else. Writing
        // the fields as well would leave nine bytes the reader never consumes.
        var stream = Written(w => DicePlusWriter.Write(w, Expression("d", "b")));
        var cursor = Reading(stream);

        Assert.Equal("DP2", cursor.ReadString());
        Assert.Equal("d", cursor.ReadString());
        Assert.Equal("b", cursor.ReadString());
        Assert.Equal(stream.Length, stream.Position);
    }

    // ---- SPELL_EFFECTS_DATA --------------------------------------------------------------------

    [Fact]
    public void An_effect_round_trips_including_the_change_data_after_it()
    {
        // changeData sits OUTSIDE the storing branch (Spell.cpp:273), which is the easiest field in
        // the structure to leave out.
        var effect = Effect(changeData: Expression("7d8-2"));
        var stream = Written(w => SpellEffectsWriter.Write(w, effect));
        var read = SpellEffectsReader.Read(Reading(stream), Modern);

        Assert.Equal(effect.IndexKey, read.IndexKey);
        Assert.Equal(effect.Flags, read.Flags);
        Assert.Equal(effect.String2, read.String2);
        Assert.Equal(effect.SourceOfEffect, read.SourceOfEffect);
        Assert.Equal(effect.Parent, read.Parent);
        Assert.Equal(effect.Scripts, read.Scripts);
        Assert.Equal(effect.StopTime, read.StopTime);
        Assert.Equal(effect.Data, read.Data);
        Assert.Equal(effect.ChangeData, read.ChangeData);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void The_change_result_keeps_all_eight_bytes()
    {
        // A double between DWORD neighbours. Written as a float it would come back rounded and
        // shift everything after it.
        var effect = Effect() with { ChangeResult = -1.2345678901234568e18 };
        var read = SpellEffectsReader.Read(
            Reading(Written(w => SpellEffectsWriter.Write(w, effect))), Modern);

        Assert.Equal(effect.ChangeResult, read.ChangeResult);
    }

    [Fact]
    public void A_short_script_list_is_padded_to_nine_rather_than_shortening_the_record()
    {
        // The four waves are cumulative, so a list read below 0.910 is a prefix of the nine and the
        // missing tail is what the reference's own Clear() leaves for it to write.
        var effect = Effect(scripts: ["s3", "s4", "s5", "s6", "s7"]);
        var read = SpellEffectsReader.Read(
            Reading(Written(w => SpellEffectsWriter.Write(w, effect))), Modern);

        Assert.Equal(SpellEffectsWriter.ScriptCount, read.Scripts.Count);
        Assert.Equal(effect.Scripts, read.Scripts.Take(5));
        Assert.All(read.Scripts.Skip(5), s => Assert.Equal(string.Empty, s));
    }

    [Fact]
    public void An_effect_with_more_scripts_than_slots_is_refused()
    {
        var effect = Effect(scripts: [.. Enumerable.Repeat("x", 10)]);

        Assert.False(SpellEffectsWriter.CanWrite(effect, out string reason));
        Assert.Contains("10", reason);
    }

    [Fact]
    public void An_effect_whose_change_data_is_legacy_is_refused()
    {
        var effect = Effect(changeData: new DicePlus("DP1", "", "", 1, 6, 0, 0, 0, 1, []));

        Assert.False(SpellEffectsWriter.CanWrite(effect, out string reason));
        Assert.Contains("DP1", reason);
    }

    // ---- the record ----------------------------------------------------------------------------

    [Fact]
    public void A_whole_record_round_trips()
    {
        var spell = Spell();
        var read = RoundTrip(spell);

        Assert.Equal(spell.PreSpellNameKey, read.PreSpellNameKey);
        Assert.Equal(spell.Name, read.Name);
        Assert.Equal(spell.CastSound, read.CastSound);
        Assert.Equal(spell.SchoolId, read.SchoolId);
        Assert.Equal(spell.AllowedBaseclasses, read.AllowedBaseclasses);
        Assert.Equal(spell.Level, read.Level);
        Assert.Equal(spell.CastingTime, read.CastingTime);
        Assert.Equal(spell.CastingTimeType, read.CastingTimeType);
        Assert.Equal(spell.CanTargetFriend, read.CanTargetFriend);
        Assert.Equal(spell.CanTargetEnemy, read.CanTargetEnemy);
        Assert.Equal(spell.IsCumulative, read.IsCumulative);
        Assert.Equal(spell.Restrictions, read.Restrictions);
        Assert.Equal(spell.CanBeDispelled, read.CanBeDispelled);
        Assert.Equal(spell.CanMemorize, read.CanMemorize);
        Assert.Equal(spell.AllowScribe, read.AllowScribe);
        Assert.Equal(spell.AutoScribe, read.AutoScribe);
        Assert.Equal(spell.Lingers, read.Lingers);
        Assert.Equal(spell.LingerOnceOnly, read.LingerOnceOnly);
        Assert.Equal(spell.SaveVersus, read.SaveVersus);
        Assert.Equal(spell.SaveResult, read.SaveResult);
        Assert.Equal(spell.Targeting, read.Targeting);
        Assert.Equal(spell.DurationRate, read.DurationRate);
        Assert.Equal(spell.CastCost, read.CastCost);
        Assert.Equal(spell.CastPriority, read.CastPriority);
        Assert.Equal(spell.Parameters, read.Parameters);
        Assert.Equal(spell.CastArt, read.CastArt);
        Assert.Equal(spell.Art, read.Art);
        Assert.Equal(spell.Sounds, read.Sounds);
        Assert.Equal(spell.CastMessage, read.CastMessage);
        Assert.Equal(spell.Scripts, read.Scripts);
        Assert.Equal(spell.EffectDuration, read.EffectDuration);
        Assert.Single(read.Effects);
    }

    [Fact]
    public void The_written_version_is_the_one_the_embedded_art_sets()
    {
        // The same bound as the other two record types: the icon's RestartFrame arrives at 5.24.
        Assert.Equal(DesignVersion.V524, SpellRecordWriter.WrittenVersion);
    }

    [Fact]
    public void The_scripts_go_out_in_slot_order_with_both_halves_of_each_pair()
    {
        // Fourteen strings, source then binary. Writing only the sources would leave the reader
        // taking the next source as the previous one's binary -- readable, and wrong throughout.
        var read = RoundTrip(Spell());

        Assert.Equal(SpellRecordReader.SpellScriptCount, read.Scripts.Count);
        Assert.Equal("src0", read.Scripts[(int)SpellScriptSlot.Begin].Source);
        Assert.Equal("bin0", read.Scripts[(int)SpellScriptSlot.Begin].Binary);
        Assert.Equal("src6", read.Scripts[(int)SpellScriptSlot.SavingThrowFailed].Source);
        Assert.Equal("bin6", read.Scripts[(int)SpellScriptSlot.SavingThrowFailed].Binary);
    }

    [Fact]
    public void Three_parameters_are_padded_to_six()
    {
        // P3..P5 arrive at 0.999432. A record from below it has three, and the reference writes its
        // default-constructed members for the rest.
        var read = RoundTrip(Spell(parameters: [Expression("a"), Expression("b"), Expression("c")]));

        Assert.Equal(SpellRecordWriter.ParameterCount, read.Parameters.Count);
        Assert.Equal("c", read.Parameters[2].Text);
        Assert.All(read.Parameters.Skip(3), p => Assert.Equal(DicePlusWriter.Empty, p));
    }

    [Fact]
    public void A_record_with_no_sounds_writes_four_blanks()
    {
        // Below 0.840 the record has no sounds at all. Writing nothing would take four strings out
        // of the stream and misalign the cast message and everything after it.
        var read = RoundTrip(Spell(sounds: []));

        Assert.Equal(SpellRecordWriter.SoundCount, read.Sounds.Count);
        Assert.All(read.Sounds, s => Assert.Equal(string.Empty, s));
    }

    [Fact]
    public void A_record_with_no_cast_art_or_effect_duration_writes_the_empty_ones()
    {
        // Both are absent below their gates, and the reference has a default-constructed member to
        // write in each case -- which is not the same thing as the port having lost something.
        // `with`, not the helper's optional arguments: those cannot tell "not supplied" from
        // "explicitly absent", which is the distinction under test.
        var read = RoundTrip(Spell() with { CastArt = null, EffectDuration = null });

        Assert.Equal(PicDataWriter.Empty, read.CastArt);
        Assert.Equal(DicePlusWriter.Empty, read.EffectDuration);
    }

    [Fact]
    public void The_sound_paths_are_stripped_on_the_way_out()
    {
        // SPELL_DATA::PreSerialize runs StripFilenamePath over the cast sound and the four effect
        // sounds before a byte is written (Spell.cpp:4276).
        var read = RoundTrip(Spell() with
        {
            CastSound = @"sounds\cast.wav",
            Sounds = [@"a\b\missile.wav", "coverage.wav", @"deep\hit.wav", "linger.wav"],
        });

        Assert.Equal("cast.wav", read.CastSound);
        Assert.Equal("missile.wav", read.Sounds[0]);
        Assert.Equal("hit.wav", read.Sounds[2]);
    }

    [Fact]
    public void The_school_id_and_baseclasses_are_written_verbatim()
    {
        // Both are CString-derived ids, not DAS strings: a literal "*" has to survive as one.
        var read = RoundTrip(Spell() with { SchoolId = "*", AllowedBaseclasses = ["*", "druid"] });

        Assert.Equal("*", read.SchoolId);
        Assert.Equal(["*", "druid"], read.AllowedBaseclasses);
    }

    // ---- what cannot be written ----------------------------------------------------------------

    [Fact]
    public void A_short_script_list_is_refused_rather_than_padded()
    {
        // Unlike the parameters and the sounds, these slots are positional in a way padding cannot
        // recover: a five-entry list from a 1.0303 design holds the saving-throw group in the two
        // places the initiation pair belongs.
        Assert.False(SpellRecordWriter.CanWrite(
            Spell(scripts: [new SpellScript("a", "b")]), out string reason));
        Assert.Contains("SpellScriptSlot", reason);
    }

    [Fact]
    public void The_wrong_number_of_art_slots_is_refused()
    {
        Assert.False(SpellRecordWriter.CanWrite(Spell(art: [Art("only.png")]), out string reason));
        Assert.Contains("art slots", reason);
    }

    [Fact]
    public void A_legacy_special_ability_block_is_refused()
    {
        var legacy = new SpecabBlock([], [new LegacySpecabSlot("script", "bin", "", "", 0, 0, [])], []);

        Assert.False(SpellRecordWriter.CanWrite(Spell(specialAbilities: legacy), out string reason));
        Assert.Contains("pre-0.921", reason);
    }

    [Fact]
    public void A_legacy_expression_anywhere_in_the_record_is_refused()
    {
        var legacy = new DicePlus("DP0", "", "", 1, 6, 0, 0, 0, 1, []);

        Assert.False(SpellRecordWriter.CanWrite(
            Spell(parameters: [legacy, Expression(), Expression()]), out string parameter));
        Assert.Contains("parameter", parameter);

        Assert.False(SpellRecordWriter.CanWrite(
            Spell() with { EffectDuration = legacy }, out string duration));
        Assert.Contains("effect duration", duration);

        Assert.False(SpellRecordWriter.CanWrite(
            Spell(effects: [Effect(changeData: legacy)]), out string effect));
        Assert.Contains("changeData", effect);
    }

    // ---- the database ---------------------------------------------------------------------------

    [Fact]
    public void A_database_is_a_count_then_the_records_and_nothing_after_them()
    {
        // Unlike items.dat, which carries an ammo-type list after the records.
        var spells = new List<SpellRecord> { Spell("Fireball"), Spell("Magic Missile") };
        var stream = Written(w => SpellRecordWriter.WriteDatabase(w, spells));

        var read = SpellRecordReader.ReadDatabase(Reading(stream), Modern, ArchiveRole.Editor);

        Assert.Equal(["Fireball", "Magic Missile"], read.Select(s => s.Name));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void A_database_is_refused_whole_before_a_byte_goes_out()
    {
        // Checked up front, as the other two databases are: a caller finds out before it has
        // started a file rather than half way through one.
        var stream = new MemoryStream();
        var cursor = ArchiveWriteCursor.For(new MfcArchiveWriter(stream));

        Assert.Throws<NotSupportedException>(() => SpellRecordWriter.WriteDatabase(
            cursor, [Spell("fine"), Spell("bad", art: [])]));
        Assert.Equal(0, stream.Length);
    }
}
