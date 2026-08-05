namespace UAFcore;

/// <summary>
/// Whether a character may cast or memorise at all
/// (<c>CHARACTER::CanCastSpells</c>, <c>Char.cpp:7987</c>;
/// <c>CanMemorizeSpells</c>, <c>:8121</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Both are script gates that default to <i>yes</i>, and that is the opposite of
/// <see cref="ClassChange"/>.</b> Changing class starts from an empty answer that only a script
/// can fill in, so a design without the hooks can never change class. These two start from
/// permission — <c>CanMemorizeSpells</c> literally seeds <c>"YYYYY"</c> as its "innitial
/// assumption" — and a script can only take it away. A design with no scripts casts and memorises
/// freely.
/// </para>
/// <para>
/// <b>Monsters are excluded before any script runs</b>, in both.
/// </para>
/// </remarks>
public static class SpellPermissions
{
    /// <summary>The five answers <c>CanMemorizeSpells</c> can give, by circumstance.</summary>
    /// <remarks>
    /// <b>The result is five characters and the caller indexes into it.</b> 0 asks whether
    /// MEMORIZE should appear on the magic menu; 1 asks whether this character should memorise
    /// while resting. The other three are unused by the engine and reserved for scripts.
    /// </remarks>
    public const int ForTheMagicMenu = 0;

    /// <inheritdoc cref="ForTheMagicMenu"/>
    public const int WhileResting = 1;

    /// <summary>
    /// Whether this character can cast.
    /// </summary>
    /// <param name="deniedByScript">
    /// Whether any <c>CanCastSpells</c> hook answered "N". <b>False until the scripting phase
    /// lands</b> — and that default is the rule, not a stand-in for it: a character whose class
    /// and specabs carry no such script is permitted, which is every character in every shipped
    /// design.
    /// </param>
    /// <remarks>
    /// <b>The class-level hook is tested wrongly and never applies.</b> The combatant's and the
    /// character's answers are checked with <c>.IsEmpty()</c>; the class's is checked with
    /// <c>if (!pClass-&gt;RunClassScripts(...))</c> — a <c>CString</c> converted through
    /// <c>LPCTSTR</c>, whose buffer is never null, so the negation is always false and the branch
    /// is dead. A design that puts its casting rule on the class rather than the character finds
    /// it silently ignored.
    /// </remarks>
    public static bool CanCast(Character who, bool deniedByScript = false)
    {
        ArgumentNullException.ThrowIfNull(who);

        if (EventNpc.KindOf(who.Record) == ClassChange.MonsterType)
        {
            return false;
        }

        return !deniedByScript;
    }

    /// <summary>
    /// Whether this character can memorise, in the circumstance asked about.
    /// </summary>
    /// <param name="deniedByScript">
    /// Whether the scripts narrowed this circumstance's answer away from its initial yes.
    /// </param>
    public static bool CanMemorize(Character who, int circumstance,
                                   bool deniedByScript = false)
    {
        ArgumentNullException.ThrowIfNull(who);

        if (EventNpc.KindOf(who.Record) == ClassChange.MonsterType)
        {
            return false;
        }

        return !deniedByScript;
    }
}
