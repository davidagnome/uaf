namespace UAF.Import.Frua;

/// <summary>
/// What a who-pays or password event does when the party gets it right or wrong
/// (<c>passwordActionType</c>, <c>GameEvent.h:327</c>).
/// </summary>
/// <remarks>
/// These are the engine's own values, which is what the reference assigns — unlike most of the
/// enums the readers carry, there is no separate FRUA numbering here, only the flag bits that
/// select between them.
/// </remarks>
public enum FruaTrialAction
{
    NoAction = 0,
    ChainEvent = 1,
    Teleport = 2,
    BackupOneStep = 3,
}

/// <summary>What kind of payment a who-pays event demands.</summary>
public enum FruaPaymentKind
{
    /// <summary>The party cannot pay at all — the flags byte is zero.</summary>
    Impossible,
    Gems,
    Jewels,
    Platinum,
}

/// <summary>
/// A <see cref="FruaEventType.WhoPays"/>'s payload
/// (<c>addWhoPaysEvent</c>, <c>UAFWinEd/UAImport.cpp:3740</c>).
/// </summary>
/// <remarks>
/// <b>One amount, not three.</b> The flags select which currency the word at offset 9 is, so a
/// who-pays event asks for gems <i>or</i> jewels <i>or</i> platinum — never a mixture. The
/// reference tests 12 before 8 before 4, so both bits set means platinum.
/// </remarks>
public sealed record FruaWhoPaysEvent(
    ushort TextSlot, ushort SuccessTextSlot, ushort FailTextSlot,
    byte PictureSlot, bool PictureIsLarge,
    FruaPaymentKind Payment, ushort Amount,
    FruaTrialAction SuccessAction, FruaTrialAction FailAction,
    bool ExecuteDestinationEvent, FruaFacing Facing)
{
    /// <summary>Reads the payload.</summary>
    public static FruaWhoPaysEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        // Zero means impossible; otherwise 12 before 8 before 4, so 4|8 is platinum.
        var payment = flags == 0 ? FruaPaymentKind.Impossible
            : (flags & 12) == 12 ? FruaPaymentKind.Platinum
            : (flags & 8) == 8 ? FruaPaymentKind.Jewels
            : (flags & 4) == 4 ? FruaPaymentKind.Gems
            : FruaPaymentKind.Impossible;

        var actions = FruaTrialActions.Read(e);

        return new FruaWhoPaysEvent(
            TextSlot: e.Word(5),

            // The success and failure messages are at 12 and 14, in that order -- text3 then
            // text2, which is how the reference names them.
            SuccessTextSlot: e.Word(12),
            FailTextSlot: e.Word(14),
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            Payment: payment,
            Amount: payment == FruaPaymentKind.Impossible ? (ushort)0 : e.Word(9),
            SuccessAction: actions.Success,
            FailAction: actions.Fail,
            ExecuteDestinationEvent: actions.ExecuteEvent,
            Facing: actions.Facing);
    }
}

/// <summary>
/// A <see cref="FruaEventType.EnterPassword"/>'s payload
/// (<c>addPasswordEvent</c>, <c>UAFWinEd/UAImport.cpp:2689</c>).
/// </summary>
/// <remarks>
/// <b>Case is deliberately not compared.</b> The reference sets <c>matchCase = FALSE</c> with the
/// comment that FRUA is all upper case and the engine is not — a consequence of the six-bit string
/// encoding, which cannot represent lower case at all. Matching case-sensitively would make every
/// imported password unanswerable.
/// </remarks>
public sealed record FruaPasswordEvent(
    ushort TextSlot, ushort PasswordSlot, ushort SuccessTextSlot, ushort FailTextSlot,
    byte PictureSlot, bool PictureIsLarge,
    FruaTrialAction SuccessAction, FruaTrialAction FailAction,
    bool ExecuteDestinationEvent, FruaFacing Facing)
{
    /// <summary><c>ExactMatch</c> — the only criterion the reference ever assigns.</summary>
    public const int ExactMatch = 0;

    /// <summary>Reads the payload.</summary>
    public static FruaPasswordEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        var actions = FruaTrialActions.Read(e);

        return new FruaPasswordEvent(
            TextSlot: e.Word(5),
            PasswordSlot: e.Word(9),
            SuccessTextSlot: e.Word(12),
            FailTextSlot: e.Word(14),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,
            SuccessAction: actions.Success,
            FailAction: actions.Fail,
            ExecuteDestinationEvent: actions.ExecuteEvent,
            Facing: actions.Facing);
    }
}

/// <summary>
/// The success/failure block both trial events share, at offset 11.
/// </summary>
/// <remarks>
/// <b>One byte carries four unrelated things.</b> Bits 4/8/12 pick the success action, bits 1/2/3
/// the failure action, bit 64 whether the destination's event runs, and bits 16/32/48 the facing
/// on arrival — and each group is tested high-mask-first, so a value with several bits set takes
/// the largest. <c>addPasswordEvent</c> and <c>addWhoPaysEvent</c> contain this block twice,
/// identically; it is read once here.
/// </remarks>
internal static class FruaTrialActions
{
    internal static (FruaTrialAction Success, FruaTrialAction Fail,
                     bool ExecuteEvent, FruaFacing Facing) Read(FruaEvent e)
    {
        byte temp = e.Byte(11);

        var success = (temp & 12) == 12 ? FruaTrialAction.BackupOneStep
            : (temp & 8) == 8 ? FruaTrialAction.Teleport
            : (temp & 4) == 4 ? FruaTrialAction.ChainEvent
            : FruaTrialAction.NoAction;

        var fail = (temp & 3) == 3 ? FruaTrialAction.BackupOneStep
            : (temp & 2) == 2 ? FruaTrialAction.Teleport
            : (temp & 1) == 1 ? FruaTrialAction.ChainEvent
            : FruaTrialAction.NoAction;

        var facing = (temp & 48) == 48 ? FruaFacing.West
            : (temp & 32) == 32 ? FruaFacing.South
            : (temp & 16) == 16 ? FruaFacing.East
            : FruaFacing.North;

        return (success, fail, (temp & 64) == 64, facing);
    }

    /// <summary>
    /// <c>destEP = -1</c> — the reference sets both transfers' entry point to this.
    /// </summary>
    /// <remarks>
    /// Not a slot number: it means the destination is given by coordinates rather than by one of
    /// the level's named entry points.
    /// </remarks>
    internal const int NoEntryPoint = -1;
}
