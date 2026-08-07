namespace UAF.Scripting;

/// <summary>
/// What an aura needs to know about the combat it sits in, so that placement can be tested without
/// one.
/// </summary>
public interface IAuraWorld
{
    /// <summary>
    /// <c>MAX_TERRAIN_WIDTH</c>. The cell mask is indexed <c>y * MapWidth + x</c>, so this has to
    /// be the same number the mask was sized from.
    /// </summary>
    int MapWidth { get; }

    /// <summary>How many combatants the placement sweep walks (<c>m_iNumCombatants</c>).</summary>
    int CombatantCount { get; }

    /// <summary>
    /// One combatant's square and facing. <b>A negative X means "not on the map"</b> and the sweep
    /// skips them, which is how the dead and the not-yet-placed stay out of every aura.
    /// </summary>
    (int X, int Y, int Facing) Combatant(int index);

    /// <summary>
    /// Runs one of the aura's own scripts — <c>AURA_Create</c>, <c>AURA_Enter</c> or
    /// <c>AURA_Exit</c>.
    /// </summary>
    /// <param name="combatantIndex">
    /// Who the script is about, or -1 for the create hook, which has no combatant. The reference
    /// sets this on the script context before the run.
    /// </param>
    void RunAuraScript(Aura aura, string scriptName, int combatantIndex);
}

/// <summary>
/// Committing an aura's pending properties and telling combatants they have crossed its edge
/// (<c>COMBAT_DATA::CheckAuraPlacement</c>, <c>Combatants.cpp:8664</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only thing that makes a script's writes real.</b> Every setter opcode writes
/// <see cref="Aura.Pending"/>; this compares the two buffers, decides whether anything that
/// matters changed, and copies pending over current if so. A script that sets a shape and never
/// causes a placement check has changed nothing.
/// </para>
/// <para>
/// <b>An XY-attached aura recomputes on every single check.</b> The "nothing moved" test lists
/// <c>COMBATANT</c>, <c>COMBATANT_FACING</c> and <c>NONE</c> and simply omits
/// <see cref="AuraAttachment.Xy"/>, so that branch can never be taken for one — no early exit, ever.
/// Reads like an oversight; it is load-bearing, because nothing else would notice a
/// <c>$AURA_Location</c> that moved the aura but changed no other property.
/// </para>
/// <para>
/// <b>Facing is compared but not committed.</b> The equality test reads <c>facing</c> against the
/// combatant's, and the commit block never assigns it from the pending buffer — because there is no
/// pending buffer for it. It is written only by the attach-to-combatant fixup a few lines later.
/// </para>
/// <para>
/// <b>The return value is dead.</b> It is <c>false</c> on every path, and both callers loop on it:
/// <c>while (CheckAuraPlacement(pAURA, NULL)){}</c> in <c>CreateAura</c> and a
/// <c>do…while(redraw)</c> in <c>CheckAllAuraPlacements</c>, the latter carrying the comment "Why do
/// we need to call 'CheckAuraPlacement' more than once?". The answer is that you do not. Both loops
/// run exactly once, and this returns nothing.
/// </para>
/// <para>
/// <b>The sprite is not ported.</b> <c>RemoveSprite</c> / <c>AddSprite</c> paint the aura's spell
/// art over every covered cell; that needs the combat renderer's animation list, and no rule
/// depends on it.
/// </para>
/// </remarks>
public static class AuraPlacement
{
    /// <summary>The script an aura runs when it is created.</summary>
    public const string CreateScript = "AURA_Create";

    /// <summary>The script it runs for a combatant who has just come inside.</summary>
    public const string EnterScript = "AURA_Enter";

    /// <summary>And for one who has just left.</summary>
    public const string ExitScript = "AURA_Exit";

    /// <summary>
    /// The whole of <c>COMBAT_DATA::CreateAura</c> (<c>Combatants.cpp:8843</c>): add the aura, run
    /// its create script with itself on the reference stack, and place it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The create script runs before the aura has been placed</b>, which is what lets it set the
    /// shape, size and attachment that the placement immediately afterwards will use. An aura whose
    /// create script does nothing is a <see cref="AuraShape.Null"/> aura covering no squares.
    /// </para>
    /// <para>
    /// <b>The reference returns the new id with the comment "May be gone!!!"</b> — because the
    /// create script may have called <c>$AURA_Destroy</c> on it. The id is returned here for the
    /// same reason it is there: it is the only handle, and it may already be stale.
    /// </para>
    /// </remarks>
    public static int Create(AuraStore store, IAuraWorld world,
                             string abilityName, string abilityParameter,
                             string data0, string data1, string data2)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);

        var aura = store.Create(abilityName, abilityParameter, data0, data1, data2);

        store.Push(aura.Id);

        try
        {
            world.RunAuraScript(aura, CreateScript, -1);

            // `while (CheckAuraPlacement(pAURA, NULL)){}` -- once, because it always returns false.
            Check(store, aura, world, moved: false);
        }
        finally
        {
            store.Pop();
        }

        return aura.Id;
    }

    /// <summary>
    /// Commits, recovers the mask if needed, then runs enter/exit scripts.
    /// </summary>
    /// <param name="moved">
    /// Whether a combatant moved — the reference's <c>pMoveData</c>, which it reads only for
    /// null-ness here. <b>A move forces a recompute only for a visible aura</b>
    /// (<c>Combatants.cpp:8696</c>): an X-ray or neutrino one takes the early exit and keeps its
    /// mask, because nothing about it is drawn.
    /// </param>
    public static void Check(AuraStore store, Aura aura, IAuraWorld world, bool moved)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(world);

        if (ShouldRecompute(aura, world, moved))
        {
            Commit(aura, world);
            AuraCoverage.Determine(aura);
        }

        Reconcile(store, aura, world);
    }

    private static bool ShouldRecompute(Aura aura, IAuraWorld world, bool moved)
    {
        var current = aura.Current;
        var pending = aura.Pending;

        if (current.Attachment != pending.Attachment)
        {
            return true;
        }

        // Xy is absent from this list in the reference, so an XY-attached aura always recomputes.
        bool stillPlaced = current.Attachment switch
        {
            AuraAttachment.Combatant =>
                current.CombatantIndex == pending.CombatantIndex && AtCombatant(aura, world, false),
            AuraAttachment.CombatantFacing =>
                current.CombatantIndex == pending.CombatantIndex && AtCombatant(aura, world, true),
            AuraAttachment.None => true,
            _ => false,
        };

        if (!stillPlaced)
        {
            return true;
        }

        bool sameShape = current.Shape == pending.Shape
                         && current.Size1 == pending.Size1
                         && current.Size2 == pending.Size2
                         && current.Size3 == pending.Size3
                         && current.Size4 == pending.Size4
                         && string.Equals(current.SpellId, pending.SpellId, StringComparison.Ordinal)
                         && current.Wavelength == pending.Wavelength;

        if (!sameShape)
        {
            return true;
        }

        return moved && current.Wavelength == AuraWavelength.Visible;
    }

    private static bool AtCombatant(Aura aura, IAuraWorld world, bool alsoFacing)
    {
        var (x, y, facing) = world.Combatant(aura.Current.CombatantIndex);

        return aura.Current.X == x
               && aura.Current.Y == y
               && (!alsoFacing || aura.Facing == facing);
    }

    private static void Commit(Aura aura, IAuraWorld world)
    {
        aura.Current.CopyFrom(aura.Pending);

        // And then the attachment overrides what was just committed: an aura following a combatant
        // takes their square whatever $AURA_Location may have said.
        if (aura.Current.Attachment is AuraAttachment.Combatant or AuraAttachment.CombatantFacing)
        {
            var (x, y, facing) = world.Combatant(aura.Current.CombatantIndex);
            aura.Current.X = x;
            aura.Current.Y = y;
            aura.Facing = facing;
        }
    }

    /// <summary>
    /// Walks the combatants and runs a script for each one who crossed the edge either way.
    /// </summary>
    /// <remarks>
    /// <b>This runs whether or not anything was recomputed</b>, because a combatant can walk into a
    /// stationary aura. It is also the only place besides create that pushes the reference stack,
    /// which is what lets the enter and exit scripts use the other thirteen opcodes.
    /// </remarks>
    private static void Reconcile(AuraStore store, Aura aura, IAuraWorld world)
    {
        store.Push(aura.Id);

        try
        {
            for (int i = 0; i < world.CombatantCount; i++)
            {
                var (x, y, _) = world.Combatant(i);

                if (x < 0)
                {
                    continue;
                }

                bool inside = AuraCoverage.Covers(aura, x, y, world.MapWidth);
                bool listed = aura.Combatants.Contains(i);

                if (inside && !listed)
                {
                    world.RunAuraScript(aura, EnterScript, i);
                    aura.Combatants.Add(i);
                }
                else if (!inside && listed)
                {
                    world.RunAuraScript(aura, ExitScript, i);
                    aura.Combatants.Remove(i);
                }
            }
        }
        finally
        {
            store.Pop();
        }
    }
}
