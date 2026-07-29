using Xunit.Abstractions;
using UAF.Common;
namespace UAF.Serialization.Tests;
public class _Scratch(ITestOutputHelper o)
{
    [Fact]
    public void Walk()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared"))) dir = dir.Parent;
        using var fs = File.OpenRead(Path.Combine(dir!.FullName, "reference/Case.dsn/Data/Level001.lvl"));
        var h = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        var ar = new MfcArchiveReader(fs);
        var cur = ArchiveCursor.For(ar);
        var (w, hh) = LevelReader.ReadDimensions(ar);
        for (int i = 0; i < w * hh; i++) LevelReader.ReadCell(ar, h.Version);
        cur.ReadInt32();
        int count = cur.ReadInt32();
        o.WriteLine($"count={count} v={h.Version}");
        var seen = new Dictionary<EventType,int>();
        for (int i = 0; i < count; i++)
        {
            var t = (EventType)cur.ReadInt32();
            seen[t] = seen.GetValueOrDefault(t) + 1;
            if (EventDispatch.ReadsNothing(t)) continue;
            try
            {
                if (t == EventType.Combat || t == EventType.PickOneCombat) CombatEventReader.Read(cur, h.Version, ArchiveRole.Editor);
                else if (t == EventType.TextStatement) TextEventReader.Read(cur, h.Version, ArchiveRole.Editor);
                else if (t == EventType.GuidedTour) GuidedTourReader.Read(cur, h.Version, ArchiveRole.Editor);
                else if (t == EventType.SpecialItem) SpecialItemEventReader.Read(cur, h.Version, ArchiveRole.Editor);
                else { o.WriteLine($"  stopped at event {i}: {t} not ported"); break; }
            }
            catch (Exception e) { o.WriteLine($"  FAILED at event {i} ({t}): {e.Message}"); break; }
        }
        o.WriteLine("types seen: " + string.Join(", ", seen.OrderByDescending(k=>k.Value).Select(k=>$"{k.Key}={k.Value}")));
        o.WriteLine($"pos={fs.Position} len={fs.Length}");
    }
}
