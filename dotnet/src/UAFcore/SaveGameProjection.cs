namespace UAFcore;

/// <summary>
/// Turning a game in progress back into the <c>.pty</c> record the writer takes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both halves of the file work and the middle does not.</b> <c>SaveGameReader</c> reads a
/// savegame whole and <c>SaveGameWriter</c> writes one back byte for byte — that was Phase 1's
/// exit criterion and it is met. What is missing is the projection: several things a
/// <c>SaveGame</c> holds have no counterpart in this engine's live state yet, because nothing has
/// needed them while walking around a level.
/// </para>
/// <para>
/// So saving is <b>refused</b> rather than done lossily. A <c>.pty</c> with an empty visited map
/// and no trigger flags is a valid file that reads back cleanly into a party that has forgotten
/// where it has been and will re-fire every event it already resolved. That is the worst kind of
/// wrong — invisible until much later, and indistinguishable from a design bug when it surfaces.
/// </para>
/// </remarks>
public static class SaveGameProjection
{
    /// <summary>
    /// Whether a game in progress can be written out, and what stops it.
    /// </summary>
    /// <param name="reason">
    /// The state that would be lost, listed. Written for a player to read, since this is what the
    /// screen shows when a slot is chosen.
    /// </param>
    /// <remarks>
    /// Each entry is a whole subsystem's worth of live state, not a field:
    /// <list type="bullet">
    /// <item><s><b>Visited cells</b></s> — <b>done</b>. <see cref="VisitedCells"/> tracks them
    /// live and projects both ways.</item>
    /// <item><s><b>Event trigger flags</b></s> — <b>done</b>. <see cref="EventTriggerFlags"/>
    /// tracks them live and projects both ways.</item>
    /// <item><s><b>Blockages</b></s> — <b>done</b>. <see cref="BlockageClearances"/>.</item>
    /// <item><s><b>Global vaults</b></s> — <b>done</b>. <see cref="GlobalVaults"/>.</item>
    /// <item><s><b>The journal</b></s> — <b>it was never missing</b>. <c>Party.Journal</c> has
    /// been live since the journal event was ported; this list was wrong to name it.</item>
    /// </list>
    /// <para>
    /// With all five tracked, what is left is the projection itself — assembling a
    /// <c>SaveGame</c> from live state, which is a different job from keeping the state.
    /// </para>
    /// </remarks>
    public static bool CanSave(Game game, out string reason)
    {
        ArgumentNullException.ThrowIfNull(game);

        // Every piece of live state a savegame carries is now tracked (Untracked is empty). What
        // is missing is the assembly: turning the party, the world and the flags back into a
        // SaveGame record. Until that exists this still refuses -- but for a different reason,
        // and one that costs no gameplay to fix.
        reason = Untracked.Length > 0
            ? "This port cannot save yet: it does not track " + string.Join(", ", Untracked) + "."
            : "This port cannot save yet: it tracks everything a save needs but cannot yet "
              + "assemble the file.";
        return false;
    }

    /// <summary>The state a savegame carries that this engine does not yet keep.</summary>
    /// <remarks>
    /// Kept as data rather than only prose so a test can assert the list shrinks as each is
    /// built, and so this stops being the one place that has to be remembered.
    /// </remarks>
    public static readonly string[] Untracked =
        [];
}
