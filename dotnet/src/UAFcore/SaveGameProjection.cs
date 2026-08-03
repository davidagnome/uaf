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
    /// <item><b>Visited cells</b> — <c>VisitedLevel</c>'s per-level bitmap, one bit per square.
    /// Nothing maps the party's movement onto it.</item>
    /// <item><s><b>Event trigger flags</b></s> — <b>done</b>. <see cref="EventTriggerFlags"/>
    /// tracks them live and projects both ways.</item>
    /// <item><b>The journal</b> — the design's entries are read and shown, but which ones the
    /// party has collected is not tracked.</item>
    /// <item><b>Blockages</b> — doors and walls opened during play.</item>
    /// <item><b>Global vaults</b> — what has been left in one.</item>
    /// </list>
    /// </remarks>
    public static bool CanSave(Game game, out string reason)
    {
        ArgumentNullException.ThrowIfNull(game);

        reason = "This port cannot save yet: it does not track " + string.Join(", ", Untracked) +
                 ". Saving now would lose all of it.";
        return false;
    }

    /// <summary>The state a savegame carries that this engine does not yet keep.</summary>
    /// <remarks>
    /// Kept as data rather than only prose so a test can assert the list shrinks as each is
    /// built, and so this stops being the one place that has to be remembered.
    /// </remarks>
    public static readonly string[] Untracked =
        ["visited squares", "the journal", "blockages", "vault contents"];
}
