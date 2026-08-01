namespace UAF.Media;

/// <summary>
/// The named font colours a <c>/</c> tag can select (<c>FONT_COLOR_NUM</c>,
/// <c>Shared/GlobalData.h:683</c>).
/// </summary>
/// <remarks>
/// <para>
/// Ordinals are the header's. <c>zeroColor</c> and <c>whiteColor</c> are both 0 — white is the
/// default, and the two names are used interchangeably at different call sites.
/// </para>
/// <para>
/// Only the first eleven are selectable from text. The header's <c>combat*</c> duplicates,
/// <c>customColorNum</c> and <c>BACKGROUND_FILL_COLOR_NUM</c> follow <c>Silver</c> and are marked
/// "for internal use only"; the scanner never produces them, so they are omitted rather than
/// carried as unreachable members.
/// </para>
/// </remarks>
public enum FontColor
{
    White = 0,
    Yellow = 1,
    Orange = 2,
    BrightOrange = 3,
    Red = 4,
    Green = 5,
    Blue = 6,
    Cyan = 7,
    Black = 8,
    Magenta = 9,
    Silver = 10,
}
