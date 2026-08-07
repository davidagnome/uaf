namespace UAF.Scripting;

/// <summary>
/// A record's special abilities, as a script may read and write them
/// (<c>SPECIAL_ABILITIES</c>, <c>Specab.cpp:950</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A database record's list is read-only and a live one's is not.</b> Items, monsters, spells,
/// classes and abilities all construct theirs with <c>readOnly = true</c> (<c>Items.h:628</c>,
/// <c>Monster.h:342</c>, <c>Spell.h:419</c>, <c>class.cpp:2857</c>); characters and combatants do
/// not. So a script may give a character an ability and may not give one to the item it is holding
/// — the definition is shared by every copy of that item in the design.
/// </para>
/// <para>
/// <b>A refused write is logged and ignored.</b> Insert returns having done nothing; delete
/// returns <see cref="GpdlScriptContext.NoSuchAbility"/>. Neither reports failure to the script,
/// so a design writing to a database record sees the same answer as one writing to a name that was
/// not there.
/// </para>
/// </remarks>
public sealed class SpecabList
{
    private readonly Dictionary<string, string> abilities;

    /// <param name="readOnly">
    /// True for a database record — see the class remarks. Not serialized in the reference either;
    /// it is a property of the object, decided at construction.
    /// </param>
    public SpecabList(bool readOnly = false,
                      IEnumerable<KeyValuePair<string, string>>? initial = null)
    {
        ReadOnly = readOnly;
        abilities = initial is null
            ? []
            : new Dictionary<string, string>(initial, StringComparer.Ordinal);
    }

    /// <inheritdoc cref="SpecabList(bool, IEnumerable{KeyValuePair{string, string}})"/>
    public bool ReadOnly { get; }

    /// <summary>What the list holds, for a caller that wants to look without a name.</summary>
    public IReadOnlyDictionary<string, string> Abilities => abilities;

    /// <summary>
    /// One ability's value (<c>GetString</c>, <c>Specab.cpp:1105</c>).
    /// </summary>
    /// <returns>
    /// <see cref="GpdlScriptContext.NoSuchAbility"/> when there is no such ability — the sentinel,
    /// not an empty string, so a blank value stays distinguishable.
    /// </returns>
    public string Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return abilities.TryGetValue(name, out string? value)
            ? value
            : GpdlScriptContext.NoSuchAbility;
    }

    /// <summary>
    /// Adds or replaces an ability (<c>InsertAbility</c>, <c>Specab.cpp:975</c>).
    /// </summary>
    /// <returns>Whether it was written.</returns>
    public bool Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ReadOnly)
        {
            Refused++;
            return false;
        }

        abilities[name] = value ?? string.Empty;
        return true;
    }

    /// <summary>
    /// Removes an ability (<c>DeleteAbility</c>, <c>Specab.cpp:996</c>).
    /// </summary>
    /// <returns>
    /// Its value, or <see cref="GpdlScriptContext.NoSuchAbility"/> — which is also what a refused
    /// delete on a read-only list answers, so the two are indistinguishable from a script.
    /// </returns>
    public string Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ReadOnly)
        {
            Refused++;
            return GpdlScriptContext.NoSuchAbility;
        }

        if (!abilities.Remove(name, out string? value))
        {
            return GpdlScriptContext.NoSuchAbility;
        }

        return value;
    }

    /// <summary>
    /// How many writes this list has refused.
    /// </summary>
    /// <remarks>
    /// <b>The reference logs every one of them, despite trying not to.</b> The guard reads
    /// <c>if (!debugStrings.AlreadyNoted(…)) writeDebugDialog = …; WriteDebugString(…);</c> with
    /// <b>no braces</b> (<c>Specab.cpp:980</c>) — so only the dialog flag is conditional and the
    /// log line runs on every refusal. The indentation says otherwise. Counting here rather than
    /// logging, but the count is per refusal for the same reason.
    /// <para>
    /// The message itself says "Attempt to <i>Insert</i> SA in read-only structure" on the delete
    /// path too, copied from the insert one.
    /// </para>
    /// </remarks>
    public int Refused { get; private set; }
}
