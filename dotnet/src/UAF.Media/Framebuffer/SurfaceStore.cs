namespace UAF.Media;

/// <summary>
/// The keyed surface table the engine addresses art through — a port of
/// <c>SurfaceMgr&lt;T&gt;</c> (<c>Shared/SurfaceMgr.h:46</c>) and the parts of
/// <c>Graphics</c> that manage its lifetime.
/// </summary>
/// <remarks>
/// <para>
/// The engine never holds a surface pointer; it holds a <c>long</c> key, stores that key in game
/// data (<c>PIC_DATA::key</c>, <c>WallSetSlotMemType::wallSurface</c>, …) and asks the graphics
/// manager to resolve it at draw time. That indirection is what lets art be freed and reloaded
/// between levels, so the port keeps it rather than handing out object references.
/// </para>
/// <para>
/// Key allocation is reproduced deliberately: keys start at 1, increment, and are never reused
/// until the counter would overflow, at which point the original scans upward from 1 for the first
/// free key (<c>SurfaceMgr.h:100</c>). A key of -1 means "no surface" throughout the C++ tree.
/// </para>
/// </remarks>
public sealed class SurfaceStore
{
    /// <summary>The value the C++ uses for "no surface". Not a valid key.</summary>
    public const long NoSurface = -1;

    /// <summary>
    /// Reserved keys from <c>Shared/Graphics.h:42</c>. The engine passes these as blit
    /// destinations, so they are part of the contract even though they name buffers the store does
    /// not own.
    /// </summary>
    public const long MouseBufferKey = -96;
    public const long MouseSaveKey = -97;
    public const long BackBufferKey = -98;
    public const long FrontBufferKey = -99;

    private readonly Dictionary<long, Surface> surfaces = [];
    private long nextKey;

    public int Count => surfaces.Count;

    /// <summary>Adds a surface and returns its key.</summary>
    public long Add(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        long key = AllocateKey();
        surfaces[key] = surface;
        return key;
    }

    public bool TryGet(long key, out Surface surface) => surfaces.TryGetValue(key, out surface!);

    /// <summary>Resolves a key, or null if it names nothing — the shape of <c>GetSurfacePtr</c>.</summary>
    public Surface? Get(long key) => surfaces.GetValueOrDefault(key);

    public bool IsValid(long key) => surfaces.ContainsKey(key);

    /// <summary>
    /// Releases a surface and sets <paramref name="key"/> to <see cref="NoSurface"/>, mirroring
    /// <c>Graphics::ReleaseSurface(long&amp; key)</c> — the by-reference clear is why callers in
    /// the C++ can safely release twice.
    /// </summary>
    public bool Remove(ref long key)
    {
        bool removed = surfaces.Remove(key);
        key = NoSurface;
        return removed;
    }

    /// <summary>
    /// Releases every surface whose kind is in <paramref name="kinds"/> —
    /// <c>Graphics::ReleaseSurfaceTypes(DWORD)</c>, which is how the engine drops a level's art
    /// while keeping the common and font surfaces.
    /// </summary>
    public int RemoveKinds(SurfaceKind kinds)
    {
        var doomed = surfaces.Where(pair => (pair.Value.Kind & kinds) != 0)
                             .Select(pair => pair.Key)
                             .ToList();

        foreach (long key in doomed)
        {
            surfaces.Remove(key);
        }

        return doomed.Count;
    }

    public void Clear()
    {
        surfaces.Clear();
        nextKey = 0;
    }

    /// <summary>Changes a surface's kind — <c>Graphics::ChangeSurfaceType</c>.</summary>
    public bool ChangeKind(long key, SurfaceKind kind)
    {
        if (!surfaces.TryGetValue(key, out var surface))
        {
            return false;
        }

        surface.Kind = kind;
        return true;
    }

    public IEnumerable<KeyValuePair<long, Surface>> Entries => surfaces;

    private long AllocateKey()
    {
        if (surfaces.Count == 0)
        {
            nextKey = 1;
            return nextKey;
        }

        if (nextKey >= int.MaxValue - 1)
        {
            // The wrap path from SurfaceMgr::GetNextSurfaceKey. A gap is guaranteed because the
            // key space is far larger than the surface limit, so this terminates.
            long candidate = 1;
            while (surfaces.ContainsKey(candidate))
            {
                candidate++;
            }
            nextKey = candidate;
        }
        else
        {
            nextKey++;
        }

        return nextKey;
    }
}
