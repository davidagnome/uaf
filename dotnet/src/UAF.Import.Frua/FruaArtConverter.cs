using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// The art an imported design points at.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference does not import FRUA's art. It substitutes placeholders.</b> That is worth
/// stating plainly, because "art slots" sounds like a conversion and is not one: FRUA keeps its
/// pictures in <c>.DAX</c> archives, and <c>InitImportTables</c> (<c>UAImport.cpp:4405</c>) never
/// opens them. It fills every slot table with a path into the *editor's* template art instead, so
/// an imported design draws with the editor's default walls and a single stand-in portrait.
/// </para>
/// <para>
/// <b>And most of the tables do not even vary by slot.</b> The per-index <c>Format</c> calls for
/// walls and doors are commented out, so all sixteen wall slots get <c>wa_Wall1.png</c> and all
/// sixteen doors <c>dr_Door5.png</c>. The small-picture loop has no <c>%i</c> at all — every one
/// of its 241 slots names <c>prt_SPic1.png</c>. Only backdrops keep a per-index name, and they
/// fall back to the first when the file is missing.
/// </para>
/// <para>
/// <b>Sounds are dropped entirely.</b> <c>AssignSound</c> (<c>UAImport.cpp:776</c>) opens with an
/// unconditional <c>return</c> and its body is commented out, so every sound slot a design names
/// — a text event's, a combat's, the ten on a sounds event — imports as nothing. This port
/// reproduces that rather than inventing paths, and it is the reason
/// <see cref="FruaEventConverter"/>'s sound fields are empty strings.
/// </para>
/// <para>
/// <b>Extracting the real art would be a genuine improvement</b>, and a separate piece of work:
/// it means decoding <c>.DAX</c>, which is outside anything the reference importer does.
/// </para>
/// </remarks>
public static class FruaArtConverter
{
    /// <summary>Wall slots a level carries (<c>MAX_IMPORT_WALLS</c>).</summary>
    public const int MaxWalls = 16;

    /// <summary>Backdrop slots a level carries (<c>MAX_IMPORT_BACKDROPS</c>).</summary>
    public const int MaxBackdrops = 20;

    /// <summary>Small-picture slots an event can name (<c>MAX_IMPORT_PICS</c>).</summary>
    public const int MaxPictures = 240;

    /// <summary>The wall image every wall slot gets.</summary>
    public const string WallFile = "wa_Wall1.png";

    /// <summary>The door image every door slot gets.</summary>
    public const string DoorFile = "dr_Door5.png";

    /// <summary>The portrait every picture slot gets.</summary>
    public const string PictureFile = "prt_SPic1.png";

    /// <summary>The icon every imported monster gets (<c>PicSlot.cpp:128</c>).</summary>
    public const string MonsterIconFile = "cm_DefMI.png";

    /// <summary>The sound an imported creature makes on a hit.</summary>
    public const string HitSoundFile = "Hit.wav";

    /// <summary>The sound it makes on a miss.</summary>
    public const string MissSoundFile = "Miss.wav";

    /// <summary><c>IconDib</c> (<c>SurfaceMgr.h:25</c>) — another bit flag, not an ordinal.</summary>
    public const int IconDib = 64;

    /// <summary>
    /// The icon an imported monster gets.
    /// </summary>
    /// <remarks>
    /// <b>The one place the reference supplies real art rather than a stand-in for nothing.</b>
    /// <c>ProcessMonsterCchData</c> sets the editor's default monster icon and calls
    /// <c>SetDefaults</c>, so an imported monster draws something in combat — and the record
    /// cannot go out without it: a monster with no <c>PIC_DATA</c> is a pre-0.640 shape the writer
    /// refuses, because rebuilding one needs <c>PIC_DATA::SetDefaults</c>.
    /// </remarks>
    public static PicRecord MonsterIcon { get; } =
        new(PicType: IconDib,
            FileName: MonsterIconFile,
            TimeDelay: 0,
            NumFrames: 1,
            FrameWidth: 0,
            FrameHeight: 0,
            Flags: 0,
            MaxLoops: 0,
            Style: 0,
            UseAlpha: 0,
            AlphaValue: 0,
            RestartFrame: 0);

    /// <summary>The backdrop a slot falls back to when its own is missing.</summary>
    public const string DefaultBackdropFile = "bd_Background1.png";

    /// <summary>The backdrop image for a slot, which is the one table that varies.</summary>
    public static string BackdropFile(int slot) => $"bd_Background{slot}.png";

    /// <summary>
    /// The wall sets a level uses.
    /// </summary>
    /// <remarks>
    /// One per slot the level names, all pointing at the same two images — see the class remarks.
    /// The door comes second because <c>DoorFirst</c> is zero: FRUA draws the wall and then the
    /// door over it.
    /// </remarks>
    public static WallSetSlot[] WallSets(FruaLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);

        return level.WallSlots
            .Select(_ => new WallSetSlot(
                WallFile: WallFile,
                DoorFile: DoorFile,
                OverlayFile: string.Empty,
                SoundFile: string.Empty,
                AreaViewFile: string.Empty,
                Used: 1,
                DoorFirst: 0,
                DrawAreaView: 0,
                UnlockSpellId: string.Empty,
                BlendOverlay: 0,
                BlendAmount: 0))
            .ToArray();
    }

    /// <summary>
    /// The backdrops a level uses.
    /// </summary>
    /// <param name="level">The level, whose backdrop slots name the images.</param>
    /// <param name="exists">
    /// Whether a named file is present. The reference substitutes
    /// <see cref="DefaultBackdropFile"/> for one that is not, so a design naming art the editor
    /// does not ship still draws something. Null treats every file as present, which is what a
    /// caller converting without an editor installation gets.
    /// </param>
    public static BackgroundSlot[] Backgrounds(FruaLevel level, Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        return level.BackdropSlots
            .Select(slot =>
            {
                string file = BackdropFile(slot);

                if (exists is not null && !exists(file))
                {
                    file = DefaultBackdropFile;
                }

                return new BackgroundSlot(
                    BackgroundFile: file,
                    BackgroundFileAlt: string.Empty,
                    SoundFile: string.Empty,
                    SuppressStepSound: 0,
                    Used: 1,
                    StartTime: 0,
                    EndTime: 0,
                    UseAltBackground: 0,
                    UseAlphaBlend: 0,
                    AlphaBlendPercent: 0,
                    UseTransparency: 0);
            })
            .ToArray();
    }

    /// <summary>
    /// The picture an event's art slot names, or none.
    /// </summary>
    /// <remarks>
    /// <b>The flag bit decides whether there is art at all, not how big it is.</b>
    /// <c>AssignPic</c> returns an empty record when the bit is clear, whatever the slot says —
    /// its own comment reads "indicates pic, big pic, or no art". So the readers' <c>PictureIsLarge</c>
    /// is really "has a picture", and a slot number without the bit names nothing.
    /// </remarks>
    public static PicRecord? Picture(byte slot, bool hasPicture)
    {
        if (!hasPicture || slot < 1 || slot > MaxPictures)
        {
            return null;
        }

        return new PicRecord(
            PicType: SmallPicDib,
            FileName: PictureFile,
            TimeDelay: 0,
            NumFrames: 1,
            FrameWidth: 0,
            FrameHeight: 0,
            Flags: 0,
            MaxLoops: 0,
            Style: 0,
            UseAlpha: 0,
            AlphaValue: 0,
            RestartFrame: 0);
    }

    /// <summary>
    /// <c>SmallPicDib</c> — the picture type an imported event's art gets
    /// (<c>SurfaceMgr.h:25</c>).
    /// </summary>
    /// <remarks>
    /// <b>A bit flag, not an ordinal.</b> <c>SurfaceType</c> is a power-of-two set, so the small
    /// picture is 1024 and not the twelfth thing in a list — writing its position would name
    /// <c>CombatDib</c> instead.
    /// </remarks>
    public const int SmallPicDib = 1024;
}
