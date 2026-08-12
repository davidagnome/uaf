namespace UAF.Import.Frua;

/// <summary>What pressing an encounter button does.</summary>
/// <remarks>The values are the low three bits of a button's byte; 7 has no case.</remarks>
public enum FruaEncounterResult
{
    DecreaseRange = 0,
    CombatSlowPartySurprised = 1,
    CombatSlowMonsterSurprised = 2,
    CombatNoSurprise = 3,
    Talk = 4,
    EscapeIfFastPartyElseCombat = 5,
    NoResult = 6,

    /// <summary>7, which the reference's switch does not cover.</summary>
    Unmapped = 7,
}

/// <summary>One of an encounter's five buttons.</summary>
/// <param name="Present">Bit 3 of its byte. A button with the bit clear is not offered.</param>
/// <param name="AllowedUpClose">Whether it may be pressed at close range.</param>
/// <param name="OnlyUpClose">Whether it may be pressed <i>only</i> at close range.</param>
public readonly record struct FruaEncounterButton(
    bool Present, FruaEncounterResult Result, bool AllowedUpClose, bool OnlyUpClose);

/// <summary>
/// A <see cref="FruaEventType.Encounter"/>'s payload
/// (<c>addEncounterEvent</c>, <c>UAFWinEd/UAImport.cpp:2296</c>).
/// </summary>
/// <remarks>
/// Five buttons — Approach, Retreat, Fight, Wait, Talk — at offsets 10 to 14, each with its
/// presence in bit 3, a range flag in bit 4 and a result in the low three bits.
/// </remarks>
public sealed record FruaEncounterEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaCombatDistance Distance, byte MonsterSpeed,
    IReadOnlyList<FruaEncounterButton> Buttons)
{
    /// <summary>The buttons, in the order the reference labels them.</summary>
    public static readonly string[] Labels =
        ["Approach", "Retreat", "Fight", "Wait", "Talk"];

    /// <summary>
    /// <b>Two of the reference's five button blocks write to the wrong button. Fixed here.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blocks are five near-copies of one another and two were mis-edited: button 2's range
    /// handling sets <c>buttons[0].onlyUpClose</c> instead of its own, in both branches, and button
    /// 4's sets <c>buttons[2].allowedUpClose</c> <i>and</i> <c>buttons[0].onlyUpClose</c> — so in
    /// the reference the Talk button never configures itself and quietly reconfigures two others.
    /// Buttons 1 and 3 have their range handling commented out entirely.
    /// </para>
    /// <para>
    /// <b>Each button is given its own flags here.</b> The goal is a design that loads correctly,
    /// not a defect reproduced for its own sake, and the intent of the five near-identical blocks
    /// is unambiguous. Recorded because it is a real divergence from the reference: a design that
    /// sets bit 4 on button 2 or 4 imports differently here, and deliberately so.
    /// </para>
    /// </remarks>
    public const string ButtonFlagsFixed =
        "addEncounterEvent's buttons 2 and 4 write range flags to buttons 0 and 2; corrected here";

    /// <summary>Reads the payload.</summary>
    public static FruaEncounterEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        byte distance = (byte)(flags & 127);

        // Start from the reference's cleared state: nothing present, both range flags false.
        var buttons = new FruaEncounterButton[5];

        for (int i = 0; i < 5; i++)
        {
            byte b = e.Byte(10 + i);
            if ((b & 8) != 8)
            {
                continue;
            }

            buttons[i] = buttons[i] with
            {
                Present = true,
                Result = (FruaEncounterResult)(b & 0x07),
            };

            // Bit 4 restricts the button's range. Button 0 reads it as "not allowed up close";
            // buttons 2 and 4 as "only up close" -- that difference IS in the reference and is
            // kept, since the two groups mean different things. What is corrected is only WHICH
            // button each block writes to: its own. See ButtonFlagsFixed.
            bool restricted = (b & 16) == 16;

            buttons[i] = i switch
            {
                0 => buttons[0] with { AllowedUpClose = !restricted, OnlyUpClose = false },
                2 or 4 => buttons[i] with { AllowedUpClose = true, OnlyUpClose = restricted },

                // Buttons 1 and 3 have no range handling at all in the reference -- theirs is
                // commented out rather than mis-targeted, so there is no intent to recover.
                _ => buttons[i],
            };
        }

        return new FruaEncounterEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,
            Distance: (distance & 1) == 1 ? FruaCombatDistance.Nearby
                    : (distance & 2) == 2 ? FruaCombatDistance.FarAway
                    : FruaCombatDistance.UpClose,
            MonsterSpeed: e.Byte(9),
            Buttons: buttons);
    }

    /// <summary>How many buttons are offered.</summary>
    public int ButtonCount => Buttons.Count(b => b.Present);
}
