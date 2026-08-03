using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Which squares of which levels the party has stood on (<c>VISIT_DATA</c>, <c>Party.h:302</c>).
/// </summary>
/// <remarks>
/// <para>
/// A bit per square, a bitmap per level, allocated only for levels the party has actually
/// entered. This is what an automap draws and what a savegame carries; nothing else reads it.
/// </para>
/// <para>
/// <b>The bounds are not the level's, they are the format's.</b> Every bitmap is
/// <c>MAX_AREA_WIDTH</c> × <c>MAX_AREA_HEIGHT</c> — 100 × 100 — whatever size the level actually
/// is, because <c>SetVisited</c> allocates a fixed <c>TAG_LIST_2D</c> without asking the level how
/// big it is.
/// </para>
/// </remarks>
public sealed class VisitedCells
{
    /// <summary><c>MAX_AREA_WIDTH</c> (<c>Externs.h:908</c>).</summary>
    public const int Width = 100;

    /// <summary><c>MAX_AREA_HEIGHT</c> (<c>Externs.h:909</c>).</summary>
    public const int Height = 100;

    /// <summary><c>MAX_LEVELS</c> — and unlike the trigger flags, a hard ceiling here.</summary>
    /// <remarks>
    /// <b>Level 255 is out of range for visited squares and in range for trigger flags.</b>
    /// <c>VISIT_DATA</c> is a fixed <c>TAG_LIST_2D*[MAX_LEVELS]</c> tested with
    /// <c>level &gt;= MAX_LEVELS</c>, while <c>EVENT_TRIGGER_DATA</c> is a <c>CArray</c> that
    /// grows — which is what lets global events record at <see cref="EventTriggerFlags.GlobalLevel"/>
    /// and means nothing can ever mark a square visited there.
    /// </remarks>
    public const int MaxLevels = 255;

    /// <summary>Bytes in one level's bitmap — <c>(w*h &gt;&gt; 3) + 1</c>, so 1251 not 1250.</summary>
    /// <remarks>
    /// The <c>+1</c> is unconditional in <c>TAG_LIST</c>'s constructor, so the last byte is spare
    /// whenever the cell count divides by eight — as 10,000 does not, but the byte is there either
    /// way and a writer that computed a tight size would be one short of what the reader expects.
    /// </remarks>
    public const int BitmapBytes = ((Width * Height) >> 3) + 1;

    private readonly Dictionary<int, byte[]> maps = [];

    /// <summary>
    /// Whether the party has stood on a square (<c>VISIT_DATA::IsVisited</c>,
    /// <c>Party.cpp:4459</c>).
    /// </summary>
    /// <remarks>
    /// <b>A square off the edge of the map reads as visited — but only on a level that has been
    /// entered.</b> <c>TAG_LIST_2D::Get</c> returns 1 outside its bounds ("outside boundaries is
    /// tagged", <c>Taglist.h:318</c>), which keeps the border from drawing as unexplored; but
    /// <c>IsVisited</c> checks for a missing bitmap <i>first</i> and returns false. So the same
    /// out-of-bounds query answers differently depending on whether the level has ever been
    /// walked, and both answers are the reference's.
    /// </remarks>
    public bool IsVisited(int level, int x, int y)
    {
        if (level < 0 || level >= MaxLevels)
        {
            return false;
        }

        if (!maps.TryGetValue(level, out byte[]? map))
        {
            return false;
        }

        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return true;                       // outside boundaries is tagged
        }

        int bit = (y * Width) + x;
        return (map[bit >> 3] & (1 << (bit & 7))) != 0;
    }

    /// <summary>Marks a square visited (<c>VISIT_DATA::SetVisited</c>, <c>Party.cpp:4474</c>).</summary>
    /// <remarks>
    /// <b>The bitmap is allocated by the first visit to the level, not by entering it.</b> A
    /// square outside the bounds is dropped — but only after the allocation, so a level whose
    /// first recorded step was off the map still ends up with an empty bitmap in the savegame
    /// rather than no entry at all.
    /// </remarks>
    public void SetVisited(int level, int x, int y)
    {
        if (level < 0 || level >= MaxLevels)
        {
            return;
        }

        if (!maps.TryGetValue(level, out byte[]? map))
        {
            maps[level] = map = new byte[BitmapBytes];
        }

        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        int bit = (y * Width) + x;
        map[bit >> 3] |= (byte)(1 << (bit & 7));
    }

    /// <summary>How many squares of a level have been seen — for an automap, and for tests.</summary>
    public int CountOn(int level)
    {
        if (!maps.TryGetValue(level, out byte[]? map))
        {
            return 0;
        }

        int count = 0;
        for (int bit = 0; bit < Width * Height; bit++)
        {
            if ((map[bit >> 3] & (1 << (bit & 7))) != 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>The levels with a bitmap, in order.</summary>
    public IEnumerable<int> Levels => maps.Keys.Order();

    /// <summary>
    /// The savegame's shape: one entry per level that has a bitmap.
    /// </summary>
    /// <remarks>
    /// <b>Sparse, unlike the trigger flags.</b> <c>VISIT_DATA::Serialize</c> writes 255 pairs of
    /// (level, count) and a bitmap only where the count is non-zero, so the reader already drops
    /// the empties and the record list carries its own level numbers. Nothing has to be dense
    /// because nothing is positional.
    /// </remarks>
    public List<VisitedLevel> ToRecords() =>
        [.. Levels.Select(level => new VisitedLevel(level, [.. maps[level]]))];

    /// <summary>Rebuilds from a savegame's records.</summary>
    /// <remarks>
    /// A bitmap of an unexpected length is taken as far as it goes rather than rejected — an
    /// older or hand-edited save should lose the squares it cannot describe, not the level.
    /// </remarks>
    public static VisitedCells FromRecords(IReadOnlyList<VisitedLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var visited = new VisitedCells();
        foreach (var record in levels)
        {
            if (record.Level < 0 || record.Level >= MaxLevels)
            {
                continue;
            }

            var map = new byte[BitmapBytes];
            record.Bitmap.AsSpan(0, Math.Min(record.Bitmap.Length, BitmapBytes)).CopyTo(map);
            visited.maps[record.Level] = map;
        }
        return visited;
    }
}
