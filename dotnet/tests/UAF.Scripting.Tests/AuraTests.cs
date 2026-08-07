namespace UAF.Scripting.Tests;

/// <summary>
/// The aura family: the object model, the reference stack, the fourteen opcodes and the placement
/// check that makes any of it take effect.
/// </summary>
public class AuraTests
{
    // ---- a combat to place auras in -------------------------------------------------------------

    /// <summary>A fixed set of combatants, and a log of every aura script that ran.</summary>
    private sealed class World(params (int X, int Y, AuraFacing Facing)[] combatants) : IAuraWorld
    {
        public int MapWidth { get; init; } = 10;

        public int MapHeight { get; init; } = 10;

        public int CombatantCount => combatants.Length;

        public (int X, int Y, AuraFacing Facing) Combatant(int index) =>
            index >= 0 && index < combatants.Length
                ? combatants[index] : (-1, -1, AuraFacing.North);

        /// <summary>Every combatant is 1×1 unless a test says otherwise.</summary>
        public (int Width, int Height) Footprint { get; init; } = (1, 1);

        public (int Width, int Height) CombatantFootprint(int index) => Footprint;

        /// <summary>Squares the test has walled off, as "x,y".</summary>
        public HashSet<(int X, int Y)> Walls { get; } = [];

        /// <summary>Squares a combatant is standing on, for the shadow rules.</summary>
        public HashSet<(int X, int Y)> Occupied { get; } = [];

        public AuraObstacle Obstacle(int x, int y)
        {
            if (x < 0 || y < 0 || x >= MapWidth || y >= MapHeight)
            {
                return AuraObstacle.OffMap;
            }

            if (Walls.Contains((x, y))) { return AuraObstacle.Wall; }

            return Occupied.Contains((x, y)) ? AuraObstacle.Occupied : AuraObstacle.None;
        }

        /// <summary>What ran, as "script:combatant".</summary>
        public List<string> Ran { get; } = [];

        /// <summary>Called before each script, so a test can watch the aura from inside one.</summary>
        public Action<Aura, string>? OnScript { get; set; }

        public void RunAuraScript(Aura aura, string scriptName, int combatantIndex)
        {
            Ran.Add($"{scriptName}:{combatantIndex}");
            OnScript?.Invoke(aura, scriptName);
        }

        public void Move(int index, int x, int y) =>
            combatants[index] = (x, y, combatants[index].Facing);

        public void Turn(int index, AuraFacing facing) =>
            combatants[index] = (combatants[index].X, combatants[index].Y, facing);
    }

    private static AuraStore Store(int width = 10, int height = 10) => new(width * height);

    /// <summary>Covers one square, by writing the mask directly.</summary>
    private static void CoverOnly(Aura aura, int x, int y, int mapWidth)
    {
        Array.Clear(aura.Cells);
        aura.Cells[(y * mapWidth) + x] = 1;
    }

    // ---- the double buffer ----------------------------------------------------------------------

    [Fact]
    public void Every_setter_writes_the_pending_buffer_and_nothing_else()
    {
        var store = Store();
        var aura = store.Create("script", "param", "a", "b", "c");

        AuraOps.SetShape(aura, "AnnularSector");
        AuraOps.SetWavelength(aura, "Xray");
        aura.Pending.Size1 = 3;
        aura.Pending.SpellId = "fireball";

        // Nothing a script does is visible until a placement check commits it.
        Assert.Equal(AuraShape.Null, aura.Current.Shape);
        Assert.Equal(AuraWavelength.Visible, aura.Current.Wavelength);
        Assert.Equal(0, aura.Current.Size1);
        Assert.Equal(string.Empty, aura.Current.SpellId);
    }

    [Fact]
    public void A_placement_check_commits_the_pending_buffer()
    {
        var store = Store();
        var world = new World();
        var aura = store.Create("script", "param", "a", "b", "c");

        aura.Pending.Size1 = 3;
        aura.Pending.SpellId = "fireball";
        AuraOps.SetWavelength(aura, "Neutrino");

        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Equal(3, aura.Current.Size1);
        Assert.Equal("fireball", aura.Current.SpellId);
        Assert.Equal(AuraWavelength.Neutrino, aura.Current.Wavelength);
    }

    // ---- what create seeds -----------------------------------------------------------------------

    [Fact]
    public void Create_puts_the_first_two_arguments_in_the_ability_list_and_the_rest_in_user_data()
    {
        var store = Store();

        var aura = store.Create("Wall of Fire", "6", "first", "second", "third");

        Assert.Equal("6", aura.Abilities.Get("Wall of Fire"));
        Assert.Equal("first", aura.UserData[0]);
        Assert.Equal("second", aura.UserData[1]);
        Assert.Equal("third", aura.UserData[2]);

        // The other seven start empty and only $AURA_SetData could fill them -- and it is the one
        // opcode the reference never implemented.
        Assert.Equal(string.Empty, aura.UserData[3]);
    }

    [Fact]
    public void Ids_start_at_one_and_are_not_reused()
    {
        var store = Store();

        var first = store.Create("a", "", "", "", "");
        var second = store.Create("b", "", "", "", "");
        store.Delete(first);
        var third = store.Create("c", "", "", "", "");

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal(3, third.Id);
    }

    [Fact]
    public void The_create_script_runs_before_the_aura_is_placed()
    {
        var store = Store();
        var world = new World((2, 2, AuraFacing.North));
        AuraShape? shapeWhenTheScriptRan = null;

        world.OnScript = (aura, script) =>
        {
            if (script == AuraPlacement.CreateScript)
            {
                shapeWhenTheScriptRan = aura.Current.Shape;
                aura.Pending.Shape = AuraShape.Global;
            }
        };

        int id = AuraPlacement.Create(store, world, "s", "p", "0", "1", "2");

        Assert.Equal(["AURA_Create:-1"], world.Ran);
        Assert.Equal(AuraShape.Null, shapeWhenTheScriptRan);

        // And the shape the create script asked for is committed by the placement that follows it.
        Assert.Equal(AuraShape.Global, store.Auras.Single(a => a.Id == id).Current.Shape);
    }

    // ---- the reference stack ---------------------------------------------------------------------

    [Fact]
    public void There_is_no_current_aura_outside_an_aura_script()
    {
        Assert.Null(Store().Current);
    }

    [Fact]
    public void An_aura_that_destroys_itself_stops_being_current_although_its_id_is_still_pushed()
    {
        var store = Store();
        var aura = store.Create("a", "", "", "", "");
        store.Push(aura.Id);

        Assert.Same(aura, store.Current);

        store.Delete(aura);

        // The id is still on the stack -- the lookup is by id through the list, and the list no
        // longer holds it. Every remaining opcode in that script takes its error branch.
        Assert.Equal([aura.Id], store.ReferenceStack);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Popping_an_empty_reference_stack_is_refused_rather_than_underflowing()
    {
        var store = Store();

        store.Pop();

        Assert.Equal(1, store.RefusedPops);
        Assert.Empty(store.ReferenceStack);
    }

    [Fact]
    public void The_reference_stack_is_pushed_around_the_enter_and_exit_scripts()
    {
        var store = Store();
        var world = new World((1, 1, AuraFacing.North));
        var aura = store.Create("a", "", "", "", "");
        CoverOnly(aura, 1, 1, world.MapWidth);
        aura.Current.Shape = AuraShape.Global;      // so Determine leaves the mask alone
        aura.Pending.Shape = AuraShape.Global;

        Aura? currentInsideTheScript = null;
        world.OnScript = (_, _) => currentInsideTheScript = store.Current;

        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Same(aura, currentInsideTheScript);
        Assert.Empty(store.ReferenceStack);         // and popped again afterwards
    }

    // ---- the three name parsers, which disagree ---------------------------------------------------

    [Theory]
    [InlineData("Global", AuraShape.Global)]
    [InlineData("AnnularSector", AuraShape.AnnularSector)]
    public void Shape_takes_the_two_names_it_knows(string name, AuraShape expected)
    {
        var aura = Store().Create("a", "", "", "", "");

        Assert.True(AuraOps.SetShape(aura, name));
        Assert.Equal(expected, aura.Pending.Shape);
    }

    [Fact]
    public void An_unknown_shape_falls_back_to_null_and_is_reported()
    {
        var aura = Store().Create("a", "", "", "", "");
        AuraOps.SetShape(aura, "Global");

        Assert.False(AuraOps.SetShape(aura, "Spherical"));
        Assert.Equal(AuraShape.Null, aura.Pending.Shape);
    }

    [Fact]
    public void An_unknown_attachment_falls_back_to_none_and_is_reported()
    {
        var aura = Store().Create("a", "", "", "", "");
        AuraOps.SetAttachment(aura, "Combatant");

        Assert.False(AuraOps.SetAttachment(aura, "Party"));
        Assert.Equal(AuraAttachment.None, aura.Pending.Attachment);
    }

    [Fact]
    public void An_unknown_wavelength_is_silently_ignored_where_the_other_two_would_reset()
    {
        var aura = Store().Create("a", "", "", "", "");
        AuraOps.SetWavelength(aura, "Xray");

        // Three bare ifs with no else: the previous value survives a name nobody recognises. A
        // design that misspells this gets a working aura and no complaint.
        Assert.False(AuraOps.SetWavelength(aura, "Ultraviolet"));
        Assert.Equal(AuraWavelength.Xray, aura.Pending.Wavelength);
    }

    [Fact]
    public void Attaching_to_XY_blanks_the_pending_coordinate()
    {
        var aura = Store().Create("a", "", "", "", "");
        aura.Pending.X = 4;
        aura.Pending.Y = 7;

        AuraOps.SetAttachment(aura, "XY");

        // So attaching to XY and never calling $AURA_Location leaves the aura off the map rather
        // than at the origin.
        Assert.Equal((-1, -1), (aura.Pending.X, aura.Pending.Y));
    }

    // ---- when the placement check recomputes ------------------------------------------------------

    [Fact]
    public void A_settled_unattached_aura_does_not_recompute()
    {
        var store = Store();
        var world = new World();
        var aura = store.Create("a", "", "", "", "");
        AuraPlacement.Check(store, aura, world, moved: false);   // settle it

        aura.Cells[0] = 1;                                       // a mark only a recompute clears
        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Equal(1, aura.Cells[0]);
    }

    [Fact]
    public void An_XY_attached_aura_recomputes_every_single_time()
    {
        var store = Store();
        var world = new World();
        var aura = store.Create("a", "", "", "", "");
        AuraOps.SetAttachment(aura, "XY");
        AuraPlacement.Check(store, aura, world, moved: false);

        aura.Cells[0] = 1;
        AuraPlacement.Check(store, aura, world, moved: false);

        // AURA_ATTACH_XY is missing from the "nothing moved" test in the reference, so its early
        // exit can never be taken. Shape is Null here, so the recompute clears the mask.
        Assert.Equal(0, aura.Cells[0]);
    }

    [Fact]
    public void A_move_recomputes_a_visible_aura_and_not_an_xray_one()
    {
        foreach ((string wavelength, byte expected) in
                 new[] { ("Visible", (byte)0), ("Xray", (byte)1) })
        {
            var store = Store();
            var world = new World();
            var aura = store.Create("a", "", "", "", "");
            AuraOps.SetWavelength(aura, wavelength);
            AuraPlacement.Check(store, aura, world, moved: false);

            aura.Cells[0] = 1;
            AuraPlacement.Check(store, aura, world, moved: true);

            Assert.Equal(expected, aura.Cells[0]);
        }
    }

    [Fact]
    public void An_attached_aura_takes_its_position_and_facing_from_its_combatant()
    {
        var store = Store();
        var world = new World((3, 4, AuraFacing.SouthWest));
        var aura = store.Create("a", "", "", "", "");
        AuraOps.SetAttachment(aura, "CombatantFacing");
        aura.Pending.CombatantIndex = 0;
        aura.Pending.X = 99;                        // overridden by the combatant's square
        aura.Pending.Y = 99;

        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Equal((3, 4), (aura.Current.X, aura.Current.Y));
        Assert.Equal(AuraFacing.SouthWest, aura.Facing);
    }

    [Fact]
    public void A_facing_attached_aura_recomputes_when_its_combatant_turns_on_the_spot()
    {
        var store = Store();
        var world = new World((3, 4, AuraFacing.SouthWest));
        var aura = store.Create("a", "", "", "", "");
        AuraOps.SetAttachment(aura, "CombatantFacing");
        AuraPlacement.Check(store, aura, world, moved: false);

        aura.Cells[0] = 1;
        world.Turn(0, AuraFacing.South);
        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Equal(0, aura.Cells[0]);
        Assert.Equal(AuraFacing.South, aura.Facing);
    }

    [Fact]
    public void A_plain_combatant_attachment_ignores_the_turn()
    {
        var store = Store();
        var world = new World((3, 4, AuraFacing.SouthWest));
        var aura = store.Create("a", "", "", "", "");
        AuraOps.SetAttachment(aura, "Combatant");
        AuraPlacement.Check(store, aura, world, moved: false);

        aura.Cells[0] = 1;
        world.Turn(0, AuraFacing.South);
        AuraPlacement.Check(store, aura, world, moved: false);

        // Only CombatantFacing compares the facing, so this one takes the early exit.
        Assert.Equal(1, aura.Cells[0]);
    }

    // ---- coverage ---------------------------------------------------------------------------------

    [Fact]
    public void A_global_aura_covers_nothing_and_keeps_whatever_mask_it_had()
    {
        var store = Store();
        var aura = store.Create("a", "", "", "", "");
        aura.Current.Shape = AuraShape.Global;
        aura.Cells[7] = 1;

        AuraCoverage.Determine(aura, new World());

        // DetermineGlobalCoverage is NotImplemented and touches no cell -- so "Global" does not
        // mean everywhere, it means "leave it exactly as it was".
        Assert.Equal(1, aura.Cells[7]);
    }

    [Fact]
    public void A_null_aura_covers_nothing_by_clearing_the_mask()
    {
        var store = Store();
        var aura = store.Create("a", "", "", "", "");
        aura.Cells[7] = 1;

        AuraCoverage.Determine(aura, new World());

        Assert.Equal(0, aura.Cells[7]);
    }

    [Fact]
    public void The_annular_sector_goes_to_the_geometry()
    {
        var store = Store();
        var world = new World();
        var aura = store.Create("a", "", "", "", "");
        aura.Current.Shape = AuraShape.AnnularSector;
        aura.Current.Attachment = AuraAttachment.Xy;
        aura.Current.X = 5;
        aura.Current.Y = 5;
        aura.Current.Size2 = 3;
        aura.Current.Size4 = 360;

        AuraCoverage.Determine(aura, world);

        // The shape itself is AnnularCoverageTests' subject; this only checks the dispatch.
        Assert.Contains(aura.Cells, cell => (cell & 1) != 0);
    }

    // ---- enter and exit ---------------------------------------------------------------------------

    [Fact]
    public void A_combatant_inside_the_mask_gets_one_enter_script_and_not_a_second()
    {
        var store = Store();
        var world = new World((1, 1, AuraFacing.North), (5, 5, AuraFacing.North));
        var aura = store.Create("a", "", "", "", "");
        aura.Current.Shape = AuraShape.Global;
        aura.Pending.Shape = AuraShape.Global;
        CoverOnly(aura, 1, 1, world.MapWidth);

        AuraPlacement.Check(store, aura, world, moved: false);
        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Equal(["AURA_Enter:0"], world.Ran);
        Assert.Equal([0], aura.Combatants);
    }

    [Fact]
    public void Walking_out_runs_the_exit_script()
    {
        var store = Store();
        var world = new World((1, 1, AuraFacing.North));
        var aura = store.Create("a", "", "", "", "");
        aura.Current.Shape = AuraShape.Global;
        aura.Pending.Shape = AuraShape.Global;
        CoverOnly(aura, 1, 1, world.MapWidth);

        AuraPlacement.Check(store, aura, world, moved: false);
        world.Move(0, 8, 8);
        AuraPlacement.Check(store, aura, world, moved: true);

        Assert.Equal(["AURA_Enter:0", "AURA_Exit:0"], world.Ran);
        Assert.Empty(aura.Combatants);
    }

    [Fact]
    public void A_combatant_who_is_not_on_the_map_is_skipped_entirely()
    {
        var store = Store();
        var world = new World((-1, -1, AuraFacing.North));
        var aura = store.Create("a", "", "", "", "");
        aura.Current.Shape = AuraShape.Global;
        aura.Pending.Shape = AuraShape.Global;

        // Cell 0 is covered and an off-map combatant reads as (-1,-1) -- which would index the mask
        // out of range if the guard were not there.
        aura.Cells[0] = 1;

        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Empty(world.Ran);
    }

    // ---- the opcodes, through the VM ---------------------------------------------------------------

    private static string Run(string body, IGpdlHost host)
    {
        var compiler = new GpdlCompiler();
        string source = "$PUBLIC $FUNC f() { " + body + " } f;";
        Assert.True(compiler.Compile(source) == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>A host with one aura already current, so the opcodes take their success branch.</summary>
    private sealed class InsideAnAuraScript : GpdlUnhostedEnvironment
    {
        public InsideAnAuraScript()
        {
            Aura = Auras.Create("Wall of Fire", "6", "zero", "one", "two");
            Auras.Push(Aura.Id);
        }

        public Aura Aura { get; }
    }

    [Fact]
    public void The_setters_write_the_pending_buffer_through_the_vm()
    {
        var host = new InsideAnAuraScript();

        Run("""
            $AURA_Shape("AnnularSector");
            $AURA_Attach("Combatant");
            $AURA_Wavelength("Neutrino");
            $AURA_Spell("burninate");
            $AURA_Combatant(3);
            $AURA_Size(1,2,3,4);
            $AURA_Location(5,6);
            """, host);

        Assert.Equal(AuraShape.AnnularSector, host.Aura.Pending.Shape);
        Assert.Equal(AuraAttachment.Combatant, host.Aura.Pending.Attachment);
        Assert.Equal(AuraWavelength.Neutrino, host.Aura.Pending.Wavelength);
        Assert.Equal("burninate", host.Aura.Pending.SpellId);
        Assert.Equal(3, host.Aura.Pending.CombatantIndex);

        // $AURA_Size pops in reverse, so the last argument is size4.
        Assert.Equal((1, 2, 3, 4), (host.Aura.Pending.Size1, host.Aura.Pending.Size2,
                                    host.Aura.Pending.Size3, host.Aura.Pending.Size4));
        Assert.Equal((5, 6), (host.Aura.Pending.X, host.Aura.Pending.Y));
    }

    [Fact]
    public void The_ability_opcodes_read_and_write_the_auras_own_list()
    {
        var host = new InsideAnAuraScript();

        Assert.Equal("6", Run("$RETURN($AURA_GetSA(\"Wall of Fire\"));", host));

        Run("$AURA_AddSA(\"Chill\", \"9\");", host);
        Assert.Equal("9", host.Aura.Abilities.Get("Chill"));

        Assert.Equal("9", Run("$RETURN($AURA_RemoveSA(\"Chill\"));", host));
        Assert.Equal(GpdlScriptContext.NoSuchAbility, host.Aura.Abilities.Get("Chill"));
    }

    [Fact]
    public void Get_data_reads_the_slots_create_seeded_and_answers_empty_off_the_end()
    {
        var host = new InsideAnAuraScript();

        Assert.Equal("zero", Run("$RETURN($AURA_GetData(0));", host));
        Assert.Equal("two", Run("$RETURN($AURA_GetData(2));", host));

        // The reference has no bounds check in either direction and reads adjacent memory.
        Assert.Equal(string.Empty, Run("$RETURN($AURA_GetData(50));", host));
    }

    [Fact]
    public void Set_data_does_nothing_and_gives_back_its_own_second_argument()
    {
        var host = new InsideAnAuraScript();

        // NotImplemented(0x49a73b) pops neither argument and pushes no result, so the value the
        // caller reads is the argument it passed in -- and slot 0 is untouched.
        Assert.Equal("written", Run("$RETURN($AURA_SetData(0, \"written\"));", host));
        Assert.Equal("zero", host.Aura.UserData[0]);
    }

    [Fact]
    public void Create_pushes_no_result_and_a_caller_that_reads_one_gets_the_call_frame()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("$AURA_Create(\"a\",\"b\",\"c\",\"d\",\"e\");", host);

        Assert.Single(host.Auras.Auras);
        Assert.Equal("b", host.Auras.Auras[0].Abilities.Get("a"));

        // Declared to return STRING and pushing nothing, so a caller that uses the result pops
        // straight past its own arguments into the frame beneath them. "f(0)" is this script's
        // own call marker -- the worst of the four imbalances, and the only one that reads memory
        // the script never put there.
        Assert.Equal("f(0)",
                     Run("$RETURN($AURA_Create(\"a\",\"b\",\"c\",\"d\",\"e\"));",
                         new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void Destroy_answers_OK_only_when_it_fails()
    {
        // The error branch is the one that pushes a result. Backwards, and transcribed.
        Assert.Equal("OK", Run("$RETURN($AURA_Destroy());", new GpdlUnhostedEnvironment()));

        var host = new InsideAnAuraScript();
        Run("$AURA_Destroy();", host);

        Assert.Empty(host.Auras.Auras);
        Assert.Null(host.Auras.Current);
    }

    /// <summary>
    /// <b>Outside an aura script these calls hand the caller its own argument back.</b>
    /// </summary>
    /// <remarks>
    /// Each error branch pops fewer values than the call pushed and pushes no result, so whatever
    /// argument is left on top becomes the call's value. Nothing reports a failure — a design that
    /// asks a non-existent aura for an ability named "xyz" is told "xyz". These are the exact
    /// strings the reference leaves behind, and they are why the family needs testing at all.
    /// </remarks>
    [Theory]
    [InlineData("$AURA_GetSA(\"xyz\")", "xyz")]
    [InlineData("$AURA_RemoveSA(\"xyz\")", "xyz")]
    [InlineData("$AURA_Attach(\"XY\")", "XY")]
    [InlineData("$AURA_Wavelength(\"Xray\")", "Xray")]
    [InlineData("$AURA_Shape(\"Global\")", "Global")]
    [InlineData("$AURA_Spell(\"burninate\")", "burninate")]
    [InlineData("$AURA_GetData(4)", "4")]
    [InlineData("$AURA_Combatant(7)", "7")]
    // Four arguments pushed and three popped, so the value is the FIRST one, not the last.
    [InlineData("$AURA_Size(1,2,3,4)", "1")]
    public void An_aura_call_with_no_current_aura_echoes_an_argument_back(string call,
                                                                         string echoed)
    {
        Assert.Equal(echoed, Run($"$RETURN({call});", new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void The_rest_of_a_script_that_destroyed_its_own_aura_runs_in_the_error_branch()
    {
        var host = new InsideAnAuraScript();

        // Same script, two calls. The second one has no current aura -- the first took it off the
        // list while leaving its id on the reference stack -- so it echoes its argument back.
        Assert.Equal("Global",
                     Run("$AURA_Destroy(); $RETURN($AURA_Shape(\"Global\"));", host));

        // And the write it appears to have made went nowhere.
        Assert.Equal(AuraShape.Null, host.Aura.Pending.Shape);
    }

    [Fact]
    public void Destroying_an_aura_runs_the_exit_script_for_everybody_inside_it()
    {
        var store = Store();
        var world = new World((1, 1, AuraFacing.North));
        var aura = store.Create("a", "", "", "", "");
        aura.Current.Shape = AuraShape.Global;
        aura.Pending.Shape = AuraShape.Global;
        CoverOnly(aura, 1, 1, world.MapWidth);
        AuraPlacement.Check(store, aura, world, moved: false);   // he is inside
        world.Ran.Clear();

        // What $AURA_Destroy does: blank the pending shape, take it off the list, and place it one
        // more time. The placement is what runs the exit scripts -- on an object the list no longer
        // holds, which in the reference is a use-after-free that happens to work.
        aura.Pending.Shape = AuraShape.Null;
        store.Delete(aura);
        AuraPlacement.Check(store, aura, world, moved: false);

        Assert.Equal(["AURA_Exit:0"], world.Ran);
        Assert.Empty(aura.Combatants);
    }

    [Fact]
    public void A_create_script_may_destroy_the_aura_it_is_creating()
    {
        var store = Store();
        var world = new World((1, 1, AuraFacing.North));

        world.OnScript = (aura, script) =>
        {
            if (script == AuraPlacement.CreateScript)
            {
                store.Delete(aura);
            }
        };

        // The reference returns the new id with the comment "May be gone!!!" -- and it is. The
        // placement that follows still runs, on the detached object.
        int id = AuraPlacement.Create(store, world, "s", "p", "", "", "");

        Assert.Equal(1, id);
        Assert.Empty(store.Auras);
    }

    [Fact]
    public void Location_writes_nothing_outside_an_aura_script_but_still_answers()
    {
        // The only one of the fourteen with no error guard at all: the reference dereferences a
        // null aura here. Not reproducible, so the write is dropped and the stack shape kept --
        // which makes it the one call in the family that is balanced on both paths.
        Assert.Equal("OK", Run("$RETURN($AURA_Location(1,2));", new GpdlUnhostedEnvironment()));
    }
}
