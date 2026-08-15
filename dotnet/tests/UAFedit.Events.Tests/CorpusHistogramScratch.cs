using System.Text;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events.Tests;

public class CorpusHistogramScratch
{
    [Fact]
    public void Dump()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return;
        }

        var totals = new Dictionary<EventType, int>();
        var perDesign = new Dictionary<string, Dictionary<EventType, int>>();
        var report = new StringBuilder();

        foreach (string name in new[] { "SomethingWild.dsn", "Case.dsn", "RUNELORD.DSN" })
        {
            string root = Path.Combine(dir.FullName, "reference", name);
            if (!Directory.Exists(Path.Combine(root, "Data")))
            {
                continue;
            }

            using var design = LoadedDesign.Open(root);
            var counts = new Dictionary<EventType, int>();
            int levels = 0, unreadable = 0, nullBodies = 0;

            for (int i = 0; i < design.LevelFiles.Count; i++)
            {
                var level = design.Level(i);
                if (level is null)
                {
                    unreadable++;
                    continue;
                }

                levels++;
                foreach (var entry in level.Entries)
                {
                    counts[entry.Type] = counts.GetValueOrDefault(entry.Type) + 1;
                    totals[entry.Type] = totals.GetValueOrDefault(entry.Type) + 1;
                    if (entry.Body is null)
                    {
                        nullBodies++;
                    }
                }
            }

            perDesign[name] = counts;
            report.AppendLine($"## {name}: {levels} levels read, {unreadable} unreadable, "
                              + $"{counts.Values.Sum()} entries, {nullBodies} bodyless");
        }

        report.AppendLine();
        report.AppendLine("TYPE,TOTAL," + string.Join(",", perDesign.Keys));
        foreach (var pair in totals.OrderByDescending(p => p.Value))
        {
            report.AppendLine($"{pair.Key},{pair.Value},"
                + string.Join(",", perDesign.Values.Select(d => d.GetValueOrDefault(pair.Key))));
        }

        report.AppendLine();
        report.AppendLine("UNUSED: " + string.Join(", ",
            Enum.GetValues<EventType>().Where(t => !totals.ContainsKey(t))));

        File.WriteAllText("/private/tmp/claude-501/-Volumes-Data-Dev-uaf/5a14d13f-4d13-4969-967c-74146e539c75/scratchpad/histogram.txt", report.ToString());
    }
}
