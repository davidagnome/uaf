namespace UAF.Media;

/// <summary>
/// What a surface holds. A direct port of <c>SurfaceType</c> (<c>Shared/SurfaceMgr.h:24</c>),
/// values included.
/// </summary>
/// <remarks>
/// <para>
/// The numeric values are load-bearing twice over. They are a bit set, and
/// <c>Graphics::ReleaseSurfaceTypes(DWORD types)</c> frees whole categories by mask, so the values
/// must survive the port for that call to mean the same thing.
/// </para>
/// <para>
/// The kind also decides transparency: see <see cref="SurfaceKindExtensions.UsesTransparency"/>.
/// That is why a surface carries its kind at all rather than just pixels.
/// </para>
/// </remarks>
[Flags]
public enum SurfaceKind : uint
{
    Bogus = 0,
    Common = 1,
    Combat = 2,
    Wall = 4,
    Door = 8,
    Background = 16,
    Overlay = 32,
    Icon = 64,
    OutdoorCombat = 128,
    BigPic = 256,
    Map = 512,
    SmallPic = 1024,
    Sprite = 2048,
    Title = 4096,
    Buffer = 8192,
    Font = 16384,
    Mouse = 32768,
    TransBuffer = 65536,
    SpecialGraphicsOpaque = 0x20000,
    SpecialGraphicsTransparent = 0x40000,

    /// <summary><c>AllSurfTypes</c> (<c>SurfaceMgr.h:32</c>) — the mask that frees everything.</summary>
    All = 0xFFFFFFFF,
}

public static class SurfaceKindExtensions
{
    /// <summary>
    /// Whether blits from a surface of this kind honour the source colour key. Ported from
    /// <c>Graphics::UseTransparency</c> (<c>Shared/Graphics.cpp:131</c>).
    /// </summary>
    /// <remarks>
    /// The original's switch has no default-transparent case: anything unlisted is opaque. Kept
    /// that way, because a wall or icon that silently stops being keyed is a visual bug that only
    /// shows up in one design's art.
    /// </remarks>
    public static bool UsesTransparency(this SurfaceKind kind) => kind switch
    {
        SurfaceKind.Sprite or SurfaceKind.Wall or SurfaceKind.Door or SurfaceKind.Overlay or
        SurfaceKind.Icon or SurfaceKind.Font or SurfaceKind.Mouse or SurfaceKind.TransBuffer or
        SurfaceKind.SpecialGraphicsTransparent => true,
        _ => false,
    };
}
