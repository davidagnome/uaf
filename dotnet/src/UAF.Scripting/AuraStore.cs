namespace UAF.Scripting;

/// <summary>
/// The auras a combat is carrying, and the reference stack that says which one a script is talking
/// about (<c>COMBAT_DATA</c>'s <c>m_auras</c>, <c>m_auraReferenceStack</c> and <c>m_nextAuraID</c>,
/// <c>Combatants.h:722</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Thirteen of the fourteen aura opcodes take no aura argument.</b> They all operate on
/// whichever aura is on top of the reference stack, which is pushed only by the engine — around a
/// create, and around the enter/exit sweep of a placement check. So an aura script can talk about
/// itself and about nothing else, and there is no opcode that reaches a different aura at all.
/// </para>
/// <para>
/// <b>The stack holds ids, not auras, and the lookup can fail.</b> <c>GetAuraReference</c> takes the
/// id on top and linear-searches the list for it (<c>Combatants.cpp:7801</c>). An aura that has
/// destroyed itself is off the list while its id is still on the stack — so every opcode after
/// <c>$AURA_Destroy</c> in the same script takes the "outside of AURA script" branch. That branch is
/// not a no-op; see <see cref="AuraOps"/> for what each one does instead.
/// </para>
/// <para>
/// <b>Ids start at 1 and are never reused</b> — <c>m_nextAuraID = 1</c> at combat reset
/// (<c>Combatants.cpp:5687</c>), post-incremented per create.
/// </para>
/// </remarks>
public sealed class AuraStore
{
    private readonly List<Aura> auras = [];

    private readonly List<int> referenceStack = [];

    private int nextId = 1;

    /// <param name="cellCount">
    /// Squares on the combat map — <c>MAX_TERRAIN_WIDTH * MAX_TERRAIN_HEIGHT</c>. Every aura gets a
    /// mask this size, allocated once at create.
    /// </param>
    public AuraStore(int cellCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellCount);

        CellCount = cellCount;
    }

    /// <inheritdoc cref="AuraStore(int)"/>
    public int CellCount { get; }

    /// <summary>Every live aura, in creation order.</summary>
    public IReadOnlyList<Aura> Auras => auras;

    /// <summary>The ids on the reference stack, outermost first. Exposed for tests.</summary>
    public IReadOnlyList<int> ReferenceStack => referenceStack;

    /// <summary>
    /// How many pops have been refused for want of anything to pop.
    /// </summary>
    /// <remarks>
    /// The reference logs "Illegal AURA reference stack pop" and returns
    /// (<c>Combatants.cpp:7760</c>); it does not underflow. Counted rather than logged, as
    /// elsewhere in this port.
    /// </remarks>
    public int RefusedPops { get; private set; }

    /// <summary>
    /// The aura every opcode acts on, or null when there is none — which is the error branch.
    /// </summary>
    public Aura? Current
    {
        get
        {
            if (referenceStack.Count == 0)
            {
                return null;
            }

            int id = referenceStack[^1];

            // Searched by id rather than held as a reference, exactly as the reference does, so
            // that an aura which has removed itself from the list answers null here.
            foreach (var aura in auras)
            {
                if (aura.Id == id)
                {
                    return aura;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Adds an aura and seeds it (<c>COMBAT_DATA::CreateAura</c>, <c>Combatants.cpp:8843</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first two arguments become an ability, not user data.</b> <c>$AURA_Create(a,b,c,d,e)</c>
    /// inserts <c>a</c> → <c>b</c> into the new aura's ability list, and only then puts <c>c</c>,
    /// <c>d</c> and <c>e</c> in user slots 0, 1 and 2. So the ability named by the first argument is
    /// how the aura gets its behaviour at all: it is the script the create hook will find.
    /// </para>
    /// <para>
    /// <b>Argument 3 is used twice</b> — as user slot 0 and as the insert's debug string. The
    /// second use is logging only.
    /// </para>
    /// <para>
    /// This does not run the create script or place the aura. The reference does both from inside
    /// <c>CreateAura</c>, but doing so needs a script runner and a combat map; the caller owns that
    /// — see <c>UAFcore</c>'s <c>AuraPlacement</c>.
    /// </para>
    /// </remarks>
    public Aura Create(string abilityName, string abilityParameter,
                       string data0, string data1, string data2)
    {
        ArgumentNullException.ThrowIfNull(abilityName);

        var aura = new Aura(nextId++, CellCount);

        aura.Abilities.Set(abilityName, abilityParameter ?? string.Empty);
        aura.UserData[0] = data0 ?? string.Empty;
        aura.UserData[1] = data1 ?? string.Empty;
        aura.UserData[2] = data2 ?? string.Empty;

        auras.Add(aura);
        return aura;
    }

    /// <summary>
    /// Removes an aura from the list (<c>DeleteAura</c>, <c>Combatants.cpp:7766</c>).
    /// </summary>
    /// <remarks>
    /// <b>It does not touch the reference stack.</b> The id stays on it until whoever pushed it
    /// pops, and <see cref="Current"/> answers null for the whole of that window.
    /// </remarks>
    public void Delete(Aura? aura)
    {
        if (aura is not null)
        {
            auras.Remove(aura);
        }
    }

    /// <summary>Pushes an id (<c>PushAuraReference</c>).</summary>
    public void Push(int auraId) => referenceStack.Add(auraId);

    /// <summary>Pops one (<c>PopAuraReference</c>), or counts a refusal if there is none.</summary>
    public void Pop()
    {
        if (referenceStack.Count == 0)
        {
            RefusedPops++;
            return;
        }

        referenceStack.RemoveAt(referenceStack.Count - 1);
    }
}
