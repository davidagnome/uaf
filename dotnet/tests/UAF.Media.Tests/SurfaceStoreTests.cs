namespace UAF.Media.Tests;

/// <summary>
/// Covers the keyed surface table, whose behaviour the engine depends on in ways that are easy to
/// "improve" by accident — keys start at 1, are not reused, and -1 means nothing.
/// </summary>
public class SurfaceStoreTests
{
    [Fact]
    public void KeysStartAtOneAndIncrement()
    {
        var store = new SurfaceStore();

        Assert.Equal(1, store.Add(new Surface(1, 1)));
        Assert.Equal(2, store.Add(new Surface(1, 1)));
        Assert.Equal(3, store.Add(new Surface(1, 1)));
    }

    /// <summary>
    /// A released key is not handed out again while other surfaces exist. The engine keeps stale keys
    /// in game data, so reuse would resolve one art record's key to another's pixels.
    /// </summary>
    [Fact]
    public void ReleasedKeysAreNotReused()
    {
        var store = new SurfaceStore();
        long first = store.Add(new Surface(1, 1));
        long second = store.Add(new Surface(1, 1));

        Assert.True(store.Remove(ref first));

        long third = store.Add(new Surface(1, 1));
        Assert.NotEqual(second, third);
        Assert.Equal(3, third);
    }

    [Fact]
    public void RemoveClearsTheCallersKey()
    {
        // Graphics::ReleaseSurface takes the key by reference and sets it to -1, which is why the C++
        // can release the same handle twice without harm.
        var store = new SurfaceStore();
        long key = store.Add(new Surface(1, 1));

        store.Remove(ref key);

        Assert.Equal(SurfaceStore.NoSurface, key);
        Assert.False(store.Remove(ref key));
    }

    [Fact]
    public void RemoveKindsFreesByMask()
    {
        var store = new SurfaceStore();
        store.Add(new Surface(1, 1, SurfaceKind.Wall));
        store.Add(new Surface(1, 1, SurfaceKind.Door));
        store.Add(new Surface(1, 1, SurfaceKind.Font));

        int removed = store.RemoveKinds(SurfaceKind.Wall | SurfaceKind.Door);

        Assert.Equal(2, removed);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void UnknownKeyResolvesToNull()
    {
        var store = new SurfaceStore();

        Assert.Null(store.Get(SurfaceStore.NoSurface));
        Assert.Null(store.Get(42));
        Assert.False(store.IsValid(42));
    }

    [Fact]
    public void ReservedBufferKeysKeepTheOriginalValues()
    {
        // Shared/Graphics.h:42. The engine passes these as blit destinations.
        Assert.Equal(-96, SurfaceStore.MouseBufferKey);
        Assert.Equal(-97, SurfaceStore.MouseSaveKey);
        Assert.Equal(-98, SurfaceStore.BackBufferKey);
        Assert.Equal(-99, SurfaceStore.FrontBufferKey);
    }

    [Fact]
    public void ChangeKindRetargetsTransparency()
    {
        // Graphics::ChangeSurfaceType is how the engine turns a loaded picture into a keyed one.
        var store = new SurfaceStore();
        var surface = new Surface(1, 1, SurfaceKind.Common) { ColorKey = 0 };
        long key = store.Add(surface);

        Assert.False(surface.IsKeyed);
        Assert.True(store.ChangeKind(key, SurfaceKind.Sprite));
        Assert.True(surface.IsKeyed);
    }
}
