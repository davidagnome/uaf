using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Running a script over everything a character is carrying
/// (<c>CHARACTER::ForEachPossession</c>, <c>Char.cpp:11594</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It restarts the scan after every item rather than continuing.</b> The inner loop finds the
/// first unprocessed item, runs its scripts, marks it, and <c>break</c>s — then the outer loop
/// scans again from the head. That is what makes the walk safe against a script that adds or
/// removes items: the iterator is never carried across a mutation, because there is no iterator to
/// carry.
/// </para>
/// <para>
/// <b>An item added during the walk is not visited.</b> Everything present at the start is marked
/// unprocessed; anything inserted afterwards arrives already marked, so the restart is for
/// iterator safety and not to pick up new work. The reference says so in a comment on the marking
/// loop.
/// </para>
/// <para>
/// <b>The answers are concatenated, not overwritten.</b> <c>result +=</c> on every item, where
/// <see cref="GameScriptHost.ForEachPartyMember"/>'s sibling walk assigns and keeps only the last.
/// Two walks in the same engine, two conventions.
/// </para>
/// <para>
/// <b>It runs each item's own scripts, not a global one</b> — <c>RunItemScripts</c> over the item
/// <i>record</i>'s special abilities, so every copy of a sword runs the sword's script and a
/// character carrying three of them runs it three times.
/// </para>
/// </remarks>
public static class PossessionWalk
{
    /// <summary>
    /// Runs one named script over each carried item.
    /// </summary>
    /// <param name="carried">
    /// The character's inventory. Read live rather than copied, so a script that changes it during
    /// the walk is seen — which is the whole reason for the restart.
    /// </param>
    /// <param name="database">Resolves an item id to its record.</param>
    /// <returns>Every item's answer, concatenated in the order they were run.</returns>
    public static string Run(IReadOnlyList<ItemInstance> carried, string scriptName,
                             Func<string, ItemRecord?> database, GlobalScripts scripts,
                             GpdlUnhostedEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(scriptName);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(host);

        // The reference keeps this flag on the item itself and marks everything present *now* as
        // unprocessed; anything a script inserts later arrives already marked. So the set to visit
        // is fixed here, by identity, and an item added during the walk is simply not in it.
        var pending = new HashSet<ItemInstance>(carried, ReferenceEqualityComparer.Instance);

        var result = new System.Text.StringBuilder();

        bool ranSomething = true;
        while (ranSomething)
        {
            ranSomething = false;

            foreach (var item in carried)
            {
                if (!pending.Remove(item))
                {
                    continue;
                }

                // Marked before the script runs, not after -- so a script that throws or reaches
                // back into this walk cannot make the same item run twice.
                var record = database(item.ItemId);

                host.Context.SetAbilities(GpdlSaRecord.Item, record?.Tail.SpecialAbilities.Pairs
                                                                   .ToDictionary(p => p.Key,
                                                                                 p => p.Value));

                if (record is not null)
                {
                    result.Append(SpecabScripts.Run(record.Tail.SpecialAbilities, scriptName,
                                                    scripts, host, ScriptCallbacks.RunAll));
                }

                ranSomething = true;
                break;                          // and scan again from the head
            }
        }

        return result.ToString();
    }
}
