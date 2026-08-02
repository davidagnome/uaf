namespace UAFcore;

/// <summary>What an auto combatant decided to do this turn.</summary>
/// <remarks>
/// The reference expresses the outcome by assigning <c>State()</c> and returning a bare
/// <c>BOOL</c>; naming it makes the decision testable without inspecting the combatant afterwards.
/// The three the reference's own comment lists are moving, guarding and attacking — fleeing is the
/// fourth, reached before any of them.
/// </remarks>
public enum AiDecision
{
    /// <summary>Stay put. The fallback whenever nothing better is available.</summary>
    Guard,

    /// <summary>Walk the plan's path toward a target.</summary>
    Move,

    /// <summary>Attack the plan's target from where it stands.</summary>
    Attack,

    /// <summary>Run from the last attacker along the plan's path.</summary>
    Flee,

    /// <summary>Already at the map edge and leaving the fight entirely.</summary>
    LeaveMap,
}

/// <summary>An auto combatant's plan for its turn.</summary>
/// <param name="Decision">What it chose.</param>
/// <param name="Target">Who it is fighting, or <see cref="CombatMap.NoDude"/>.</param>
/// <param name="Path">The route to walk, for <see cref="AiDecision.Move"/> and
/// <see cref="AiDecision.Flee"/>.</param>
public sealed record AiPlan(AiDecision Decision, int Target = CombatMap.NoDude,
                            CombatPath? Path = null);

/// <summary>
/// What a computer-run combatant does on its turn (<c>COMBATANT::Think</c>,
/// <c>Combatant.cpp:2080</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the unscripted half only.</b> The reference forks on <c>LoadAI_Script()</c>: when a
/// design supplies an AI script, it builds a <c>COMBAT_SUMMARY</c> of every combatant, weapon,
/// attack and reachable cell, enumerates candidate actions, and ranks them by running a
/// <b>Forth</b> program (<c>RunTHINK</c>, <c>:2251</c>) — a partial-order insertion into a binary
/// tree, so the best action bubbles up. The Forth VM is not started (§11), so none of that is here.
/// What is here is the path the reference takes when no script is loaded, which is what every
/// design without a custom AI uses.
/// </para>
/// <para>
/// The decision order is the reference's and it matters: fleeing beats everything, then target
/// acquisition, then attack-from-here, then walk-toward, then guard.
/// </para>
/// </remarks>
public static class MonsterAi
{
    /// <summary>
    /// Decides what <paramref name="self"/> does this turn.
    /// </summary>
    /// <param name="canAttack">
    /// Whether a given target can be attacked from where <paramref name="self"/> stands. Supplied
    /// by the caller because <see cref="Targeting.CanAttack"/> needs weapons, which the combatant
    /// does not carry.
    /// </param>
    /// <remarks>
    /// Pure: it reads the map and the combatants and returns a plan. The reference assigns state,
    /// readies weapons and consumes wand charges as it goes; separating the decision from its
    /// execution is what lets a test assert the choice rather than its consequences.
    /// </remarks>
    public static AiPlan Think(Combatant self, IReadOnlyList<Combatant> all, CombatMap map,
                               Func<Combatant, int, bool> canAttack)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(canAttack);

        // -- fleeing beats everything ------------------------------------------------------
        if (self.IsFleeing || self.IsTurned)
        {
            return Flee(self, all, map);
        }

        // -- who are we fighting? ----------------------------------------------------------
        int target = AcquireTarget(self, all, map, canAttack);
        if (target == CombatMap.NoDude)
        {
            return new AiPlan(AiDecision.Guard);
        }

        // -- can we hit them from here? ----------------------------------------------------
        if (canAttack(self, target))
        {
            return new AiPlan(AiDecision.Attack, target);
        }

        // -- otherwise walk toward whichever target we can actually reach -------------------
        if (CanMove(self))
        {
            foreach (int candidate in Enemies(self, all))
            {
                var path = PathToward(self, Get(all, candidate), map);
                if (path is not null)
                {
                    return new AiPlan(AiDecision.Move, candidate, path);
                }
            }
        }

        return new AiPlan(AiDecision.Guard, target);
    }

    /// <summary>
    /// The flee branch (<c>Combatant.cpp:2100</c> and its near-identical twin at <c>:2155</c>).
    /// </summary>
    /// <remarks>
    /// <b>Standing on any map edge means leaving the fight</b>, not walking to the edge — the test
    /// is <c>x == 0 || x == width-1 || y == 0 || y == height-1</c> and it fires before any pathing.
    /// Otherwise the combatant paths away from whoever last attacked it, and guards if it cannot
    /// move or cannot find a way out.
    /// <para>
    /// The reference has this block twice, once for <c>iFleeingFlags</c> and once for
    /// <c>isTurned</c>, differing only in a trace message. Ported once.
    /// </para>
    /// </remarks>
    private static AiPlan Flee(Combatant self, IReadOnlyList<Combatant> all, CombatMap map)
    {
        bool onEdge = self.X == 0 || self.X == map.Width - 1
                      || self.Y == 0 || self.Y == map.Height - 1;

        if (onEdge)
        {
            return new AiPlan(AiDecision.LeaveMap);
        }

        if (!CanMove(self) || self.LastAttacker == CombatMap.NoDude)
        {
            return new AiPlan(AiDecision.Guard);
        }

        var away = PathAwayFrom(self, Get(all, self.LastAttacker), map);
        return away is null
            ? new AiPlan(AiDecision.Guard)
            : new AiPlan(AiDecision.Flee, self.LastAttacker, away);
    }

    /// <summary>
    /// Picks a target (the acquisition block at <c>Combatant.cpp:2586</c>).
    /// </summary>
    /// <remarks>
    /// <b>Line of sight is preferred but not required.</b> The reference first collects every
    /// enemy it can see, and only if that finds nothing falls back to every enemy at all — its own
    /// comment explains why: targets are ordered by distance, and the nearest one may be on the
    /// far side of a wall, so the shortest straight line is not the shortest walk. An existing
    /// target is kept if it is still attackable.
    /// </remarks>
    private static int AcquireTarget(Combatant self, IReadOnlyList<Combatant> all, CombatMap map,
                                     Func<Combatant, int, bool> canAttack)
    {
        if (self.Target != CombatMap.NoDude)
        {
            var current = Get(all, self.Target);
            if (current is not null && current.IsOnCombatMap(petrifiedOk: true)
                && canAttack(self, self.Target))
            {
                return self.Target;
            }
        }

        int nearestVisible = CombatMap.NoDude;
        int visibleDistance = int.MaxValue;
        int nearestAny = CombatMap.NoDude;
        int anyDistance = int.MaxValue;

        foreach (int index in Enemies(self, all))
        {
            var enemy = Get(all, index)!;
            int distance = CombatMap.Distance(self.X, self.Y, enemy.X, enemy.Y);

            if (distance < anyDistance)
            {
                anyDistance = distance;
                nearestAny = index;
            }

            if (distance < visibleDistance
                && LineOfSight.Between(map, self.CenterX, self.CenterY,
                                       enemy.CenterX, enemy.CenterY))
            {
                visibleDistance = distance;
                nearestVisible = index;
            }
        }

        return nearestVisible != CombatMap.NoDude ? nearestVisible : nearestAny;
    }

    /// <summary>Every enemy still on the map, nearest first.</summary>
    private static IEnumerable<int> Enemies(Combatant self, IReadOnlyList<Combatant> all) =>
        all.Where(c => c.Index != self.Index
                       && c.IsFriendly != self.IsFriendly
                       && c.IsOnCombatMap(petrifiedOk: true))
           .OrderBy(c => CombatMap.Distance(self.X, self.Y, c.X, c.Y))
           .Select(c => c.Index);

    /// <summary>
    /// Whether this combatant may move at all (<c>CanMove</c>, <c>Combatant.cpp:7187</c>).
    /// </summary>
    /// <remarks>
    /// Note it is <i>not</i> just "has movement left": a combatant whose turn is done cannot move
    /// either, and a monster can be pinned by the <c>Monster_NoMove</c> debug setting.
    /// </remarks>
    private static bool CanMove(Combatant self) =>
        !self.IsDone() && self.Movement < self.MaxMovement;

    /// <summary>
    /// A route to a square adjacent to the target (<c>FindPathTo</c> as <c>Think</c> calls it,
    /// <c>Combatant.cpp:2719</c>).
    /// </summary>
    /// <remarks>
    /// <b>The destination is the target's footprint expanded by one on every side</b> — the
    /// reference passes <c>x-1, y-1</c> through <c>x+width, y+height</c>, so the walk ends beside
    /// the target rather than on it. Asking for the target's own square would never path, since
    /// the target is standing in it.
    /// </remarks>
    private static CombatPath? PathToward(Combatant self, Combatant? target, CombatMap map)
    {
        if (target is null)
        {
            return null;
        }

        var finder = new CombatPathFinder(map)
        {
            PathWidth = self.Icon.Width,
            PathHeight = self.Icon.Height,
            IgnoreCombatant = self.Index,
        };

        return finder.To(self.X, self.Y,
                         target.X - 1, target.Y - 1,
                         target.X + target.Icon.Width, target.Y + target.Icon.Height);
    }

    /// <summary>
    /// A route away from a pursuer (<c>FindPathAwayFrom</c>, <c>Combatant.cpp:7244</c>).
    /// </summary>
    /// <remarks>
    /// The reference reverses the direction to the pursuer and looks for somewhere far away in
    /// that direction. This heads for the nearest map edge on the opposite side, which is the same
    /// intent — a fleeing combatant that reaches an edge leaves the fight on its next turn.
    /// </remarks>
    private static CombatPath? PathAwayFrom(Combatant self, Combatant? from, CombatMap map)
    {
        if (from is null)
        {
            return null;
        }

        var finder = new CombatPathFinder(map)
        {
            PathWidth = self.Icon.Width,
            PathHeight = self.Icon.Height,
            IgnoreCombatant = self.Index,
        };

        // The opposite of the direction the pursuer lies in.
        var away = CombatMovement.DirectionTo(from.X, from.Y, self.X, self.Y);

        (int X, int Y) edge = away switch
        {
            PathDirection.North or PathDirection.NorthEast or PathDirection.NorthWest =>
                (self.X, 0),
            PathDirection.South or PathDirection.SouthEast or PathDirection.SouthWest =>
                (self.X, map.Height - 1),
            PathDirection.West => (0, self.Y),
            PathDirection.East => (map.Width - 1, self.Y),
            _ => (self.X, 0),
        };

        return finder.To(self.X, self.Y, edge.X, edge.Y);
    }

    private static Combatant? Get(IReadOnlyList<Combatant> all, int index)
    {
        foreach (var c in all)
        {
            if (c.Index == index)
            {
                return c;
            }
        }
        return null;
    }
}
