namespace UAFcore;

/// <summary>
/// One entry in the turn queue (<c>QueuedCombatant</c>, <c>Combatant.h:658</c>).
/// </summary>
public sealed class QueuedCombatant
{
    public int Dude { get; set; } = CombatMap.NoDude;

    /// <summary>
    /// Whether this turn spends the combatant's resources. False for an interrupting attack,
    /// which is why <see cref="CombatRound.IsFreeAttacker"/> tests it.
    /// </summary>
    public bool AffectStats { get; set; } = true;

    public int FreeAttacks { get; set; }

    public int GuardAttacks { get; set; }

    /// <summary>Where a delayed combatant asked to act from, or −1.</summary>
    public int DelayedX { get; set; } = -1;

    /// <inheritdoc cref="DelayedX"/>
    public int DelayedY { get; set; } = -1;

    /// <summary>
    /// Set when this combatant first reaches the top of the queue, cleared once the round has been
    /// updated for it.
    /// </summary>
    public bool StartOfTurn { get; set; }

    /// <summary>
    /// Set when a combatant returns to the top after being interrupted, rather than arriving
    /// fresh. The distinction matters because a resumed turn must not re-run start-of-turn work.
    /// </summary>
    public bool RestartInterruptedTurn { get; set; }
}

/// <summary>
/// Whose turn it is (<c>QueuedCombatantData</c>, <c>Combatant.h:692</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A stack, not a queue, despite the name.</b> <see cref="Push"/> adds at the <i>head</i> and
/// <see cref="Top"/> reads it, so an interruption — a free attack, a guarding attack — goes in
/// front of whoever was acting, and the interrupted combatant resumes when it pops. That is the
/// whole mechanism for interrupts, and the reason the original is a list rather than an index.
/// <see cref="PushTail"/> is the ordinary "act later" path.
/// </para>
/// <para>
/// Every accessor is head-relative and tolerant of an empty queue, returning
/// <see cref="CombatMap.NoDude"/> or zero rather than throwing. Reproduced: the round asks these
/// questions constantly and the original never guards the call sites.
/// </para>
/// </remarks>
public sealed class TurnQueue
{
    private readonly LinkedList<QueuedCombatant> queue = new();

    /// <summary>The combatant acting now, or <see cref="CombatMap.NoDude"/>.</summary>
    public int Top => queue.First?.Value.Dude ?? CombatMap.NoDude;

    public int Count => queue.Count;

    public bool IsEmpty => queue.Count == 0;

    /// <summary>The head entry, or null when nothing is queued.</summary>
    public QueuedCombatant? Current => queue.First?.Value;

    /// <summary>Whether the head combatant's turn spends resources.</summary>
    /// <remarks>Returns false on an empty queue, matching <c>ChangeStats</c>.</remarks>
    public bool AffectsStats => queue.First?.Value.AffectStats ?? false;

    public int FreeAttacks => queue.First?.Value.FreeAttacks ?? 0;

    public int GuardAttacks => queue.First?.Value.GuardAttacks ?? 0;

    public int DelayedX => queue.First?.Value.DelayedX ?? -1;

    public int DelayedY => queue.First?.Value.DelayedY ?? -1;

    public bool StartOfTurn => queue.First?.Value.StartOfTurn ?? false;

    public bool RestartInterruptedTurn => queue.First?.Value.RestartInterruptedTurn ?? false;

    public void Clear() => queue.Clear();

    /// <summary>
    /// Puts a combatant at the front, interrupting whoever was there.
    /// </summary>
    /// <remarks>
    /// The combatant being displaced is marked <see cref="QueuedCombatant.RestartInterruptedTurn"/>
    /// — but <b>only if its own turn had already started</b>. The original writes
    /// <c>RestartInterruptedTurn = !StartOfTurn</c> on the old head
    /// (<c>Combatant.h:737</c>), so a combatant interrupted before it ever acted comes back as a
    /// fresh turn rather than a resumed one.
    /// </remarks>
    public void Push(int dude, bool affectStats, int freeAttacks, int guardAttacks)
    {
        if (queue.First is { } head)
        {
            head.Value.RestartInterruptedTurn = !head.Value.StartOfTurn;
        }

        queue.AddFirst(new QueuedCombatant
        {
            Dude = dude,
            AffectStats = affectStats,
            FreeAttacks = freeAttacks,
            GuardAttacks = guardAttacks,
            StartOfTurn = true,
        });
    }

    /// <summary>Queues a combatant to act after everyone already waiting.</summary>
    /// <remarks>
    /// Free and guard attacks are zero here, and <see cref="QueuedCombatant.StartOfTurn"/> is
    /// <i>not</i> set — it is only set by <see cref="Push"/>, so a combatant that arrives at the
    /// top by others popping off ahead of it never reports a start of turn. That asymmetry is in
    /// the original.
    /// </remarks>
    public void PushTail(int dude, bool affectStats) =>
        queue.AddLast(new QueuedCombatant { Dude = dude, AffectStats = affectStats });

    /// <summary>Removes the acting combatant.</summary>
    public void Pop()
    {
        if (queue.First is not null)
        {
            queue.RemoveFirst();
        }
    }

    /// <summary>Removes the first entry for a combatant, wherever it sits.</summary>
    public void Remove(int dude)
    {
        for (var node = queue.First; node is not null; node = node.Next)
        {
            if (node.Value.Dude == dude)
            {
                queue.Remove(node);
                return;
            }
        }
    }

    /// <summary>
    /// Marks the head as no longer starting its turn (<c>NotStartOfTurn</c>).
    /// </summary>
    /// <remarks>Clears both flags, so a resumed turn is only reported once as well.</remarks>
    public void NotStartOfTurn()
    {
        if (queue.First is { } head)
        {
            head.Value.StartOfTurn = false;
            head.Value.RestartInterruptedTurn = false;
        }
    }

    public void SetFreeAttacks(int n)
    {
        if (queue.First is { } head) { head.Value.FreeAttacks = n; }
    }

    public void SetGuardAttacks(int n)
    {
        if (queue.First is { } head) { head.Value.GuardAttacks = n; }
    }

    public void SetDelayedPosition(int x, int y)
    {
        if (queue.First is { } head)
        {
            head.Value.DelayedX = x;
            head.Value.DelayedY = y;
        }
    }

    /// <summary>Spends one free attack and returns what is left.</summary>
    public int DecrementFreeAttacks() =>
        queue.First is { } head ? --head.Value.FreeAttacks : 0;

    /// <summary>Spends one guarding attack and returns what is left.</summary>
    public int DecrementGuardAttacks() =>
        queue.First is { } head ? --head.Value.GuardAttacks : 0;

    /// <summary>The queued combatants, acting one first. For tests and diagnostics.</summary>
    public IReadOnlyList<int> Order => [.. queue.Select(q => q.Dude)];
}
