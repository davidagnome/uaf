using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// A <c>PASSWORD_DATA</c> — a prompt the party must answer correctly.
/// </summary>
/// <param name="SuccessAction">A <c>passwordActionType</c>: what a right answer does.</param>
public sealed record PasswordEvent(
    GameEventBase Base, int NbrTries, uint SuccessChain, uint FailChain,
    int SuccessAction, int FailAction, string Password,
    TransferData SuccessTransfer, TransferData FailTransfer) : IGameEvent;

/// <summary>
/// A <c>WHO_TRIES_EVENT_DATA</c> — one character attempts an ability check.
/// </summary>
/// <param name="StrengthBonus">A <c>BYTE</c>: added to the roll when strength is checked.</param>
/// <param name="CompareToDie">
/// When false, <see cref="CompareDie"/> is the target number outright rather than a die to roll.
/// </param>
public sealed record WhoTriesEvent(
    GameEventBase Base, int AlwaysSucceeds, int AlwaysFails,
    IReadOnlyList<int> AbilityChecks, IReadOnlyList<int> ThiefSkillChecks,
    byte StrengthBonus, int CompareToDie, int CompareDie, int NbrTries,
    uint SuccessChain, int SuccessAction, int FailAction, uint FailChain,
    TransferData SuccessTransfer, TransferData FailTransfer) : IGameEvent;

/// <summary>
/// The two events that put a question to the party and branch on the answer.
/// </summary>
/// <remarks>
/// They share a tail — a success chain, a fail chain, two <c>passwordActionType</c>s and two
/// <c>TRANSFER_DATA</c> blocks — because the second was written from the first. The transfer blocks
/// sit <b>outside</b> the storing/loading branch in both, so they are present at every version.
/// </remarks>
public static class TrialEventReaders
{
    /// <summary>
    /// Reads a <c>PASSWORD_DATA</c> (<c>GameEvent.cpp:7501</c>).
    /// </summary>
    /// <remarks>
    /// <b><c>matchCase</c> is declared and never serialized.</b> It sits between <c>password</c>
    /// and <c>nbrTries</c> in the class, so a reader written from the declaration inserts four
    /// bytes that are not there. <c>currTry</c> and <c>Unused</c> are absent for the same reason —
    /// runtime state and a hole.
    /// </remarks>
    public static PasswordEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int nbrTries = ar.ReadInt32();
        uint successChain = ar.ReadUInt32();
        uint failChain = ar.ReadUInt32();
        int successAction = ar.ReadInt32();
        int failAction = ar.ReadInt32();

        // The password itself goes through the blank convention, so an empty one is "*" on disk.
        string password = ArchiveStringConventions.Decode(ar.ReadString());

        var successTransfer = SimpleEventReaders.ReadTransferData(ar);
        var failTransfer = SimpleEventReaders.ReadTransferData(ar);

        return new PasswordEvent(baseEvent, nbrTries, successChain, failChain,
                                 successAction, failAction, password,
                                 successTransfer, failTransfer);
    }

    /// <summary>The six ability scores, in the order they are written.</summary>
    public static readonly IReadOnlyList<string> AbilityNames =
        ["STR", "INT", "WIS", "DEX", "CON", "CHA"];

    /// <summary>
    /// The eight thief skills, in the order they are written — pick pockets, open locks, find
    /// traps, move silently, hide in shadows, hear noise, climb walls, read languages.
    /// </summary>
    public static readonly IReadOnlyList<string> ThiefSkillNames =
        ["PP", "OL", "FT", "MS", "HS", "HN", "CW", "RL"];

    /// <summary>
    /// Reads a <c>WHO_TRIES_EVENT_DATA</c> (<c>GameEvent.cpp:9062</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The eight thief-skill flags are read and can never be set.</b> The storing branch writes
    /// a literal <c>FALSE</c> for every one of them, and <c>compareToDie</c> and <c>compareDie</c>
    /// alongside — so the loading branch reads eleven fields the writer has already flattened.
    /// They are kept here rather than skipped because the bytes are real and a design saved by an
    /// older build may still have them set; the editor's <c>CheckOldSkills</c> exists to migrate
    /// exactly that case.
    /// </para>
    /// <para>
    /// <b><c>strBonus</c> is a <c>BYTE</c></b> sitting immediately after sixteen 4-byte
    /// <c>BOOL</c>s, which is the easiest place in the record to lose alignment.
    /// </para>
    /// </remarks>
    public static WhoTriesEvent ReadWhoTries(IArchiveCursor ar, DesignVersion version,
                                             ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int alwaysSucceeds = ar.ReadInt32();
        int alwaysFails = ar.ReadInt32();

        var abilities = new List<int>(AbilityNames.Count);
        for (int i = 0; i < AbilityNames.Count; i++)
        {
            abilities.Add(ar.ReadInt32());
        }

        var thiefSkills = new List<int>(ThiefSkillNames.Count);
        for (int i = 0; i < ThiefSkillNames.Count; i++)
        {
            thiefSkills.Add(ar.ReadInt32());
        }

        byte strengthBonus = ar.ReadByte();              // BYTE, after sixteen BOOLs
        int compareToDie = ar.ReadInt32();
        int compareDie = ar.ReadInt32();
        int nbrTries = ar.ReadInt32();
        uint successChain = ar.ReadUInt32();
        int successAction = ar.ReadInt32();
        int failAction = ar.ReadInt32();
        uint failChain = ar.ReadUInt32();

        var successTransfer = SimpleEventReaders.ReadTransferData(ar);
        var failTransfer = SimpleEventReaders.ReadTransferData(ar);

        return new WhoTriesEvent(baseEvent, alwaysSucceeds, alwaysFails, abilities, thiefSkills,
                                 strengthBonus, compareToDie, compareDie, nbrTries,
                                 successChain, successAction, failAction, failChain,
                                 successTransfer, failTransfer);
    }
}
