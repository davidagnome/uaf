using System.Globalization;

namespace UAFcore;

/// <summary>
/// What one of a logic block's two actions does (<c>LOGIC_BLOCK_ACTION_TYPE</c>,
/// <c>GameEvent.h:3136</c>).
/// </summary>
/// <remarks>
/// <b>A second, shorter copy of this enum sits commented out at <c>RunEvent.cpp:14145</c> with
/// different ordinals</b> — it lists seven values where the live one has thirteen. It is stale, and
/// transcribing from it would repoint every action in every design.
/// </remarks>
public enum LogicAction : byte
{
    Nothing = 0,
    SetGlobalAsl = 1,
    SetLevelAsl = 2,
    RemoveGlobalAsl = 3,
    RemoveLevelAsl = 4,
    SetQuestStage = 5,
    TempAsl = 6,
    SetIconIndexByName = 7,
    SetCharAsl = 8,
    SetPartyAsl = 9,
    RemovePartyAsl = 10,
    SourceGpdl = 11,
    BinaryGpdl = 12,
    NotImplemented = 0xFF,
}

/// <summary>When an action runs, relative to the block's result (<c>m_IfTrue1</c>).</summary>
public enum LogicActionWhen : byte
{
    /// <summary>Only when the block came out true.</summary>
    IfTrue = 0,

    /// <summary>Only when it came out false.</summary>
    IfFalse = 1,

    /// <summary>Whatever the result.</summary>
    Always = 2,
}

/// <summary>The state a logic block's actions write, on top of what its inputs read.</summary>
public interface ILogicBlockActionHost : ILogicBlockHost
{
    /// <summary>Writes an attribute into one of the stores an action can reach.</summary>
    void SetAttribute(LogicAslScope scope, int character, string key, string value);

    /// <summary>Removes one.</summary>
    void RemoveAttribute(LogicAslScope scope, int character, string key);

    /// <summary>Writes an attribute into one level's store.</summary>
    void SetLevelAttribute(int level, string key, string value);

    /// <summary>Removes one.</summary>
    void RemoveLevelAttribute(int level, string key);

    /// <summary>Sets the stage a quest has reached.</summary>
    void SetQuestStage(string quest, int stage);

    /// <summary>Sets a character's icon index.</summary>
    void SetIconIndex(int character, int iconIndex);
}

/// <summary>
/// Runs a logic block's two actions (<c>ProcessLBAction</c>, <c>RunEvent.cpp:14157</c>).
/// </summary>
/// <remarks>
/// The write half of a logic block, and the smaller one: where an input reads six different kinds
/// of state, an action mostly inserts or deletes an attribute. What makes it worth its own file is
/// that <b>every one of its parameters is a packed string</b> — a key and a value around an
/// <c>=</c>, sometimes with a level or a character selector in front — and each layer of that
/// packing is a separate grammar.
/// </remarks>
public static class LogicBlockActions
{
    /// <summary>The character index <c>LBseparateCharacter</c> returns for "all of them".</summary>
    private const int AllCharacters = LogicBlockInputs.AllCharacters;

    /// <summary>
    /// Splits a parameter into a key and a value around the first <paramref name="token"/>
    /// (<c>SplitKeyValue</c>, <c>RunEvent.cpp:14127</c>).
    /// </summary>
    /// <remarks>
    /// <b>A parameter with no token is all key and an empty value</b>, which is meaningful rather
    /// than an error: inserting an attribute with an empty value is how a design creates a bare
    /// flag. Only the <i>first</i> token splits, so a value may contain more of them.
    /// </remarks>
    public static (string Key, string Value) SplitKeyValue(string parameter, char token = '=')
    {
        ArgumentNullException.ThrowIfNull(parameter);

        int at = parameter.IndexOf(token);
        return at < 0
            ? (parameter, string.Empty)
            : (parameter[..at], parameter[(at + 1)..]);
    }

    /// <summary>Whether an action runs, given when it asked to and how the block came out.</summary>
    /// <remarks>
    /// The reference logs a message on <i>both</i> skip paths — an author watching the debug output
    /// sees "not performed, result=0" rather than silence, which is the only way to tell a skipped
    /// action from a broken one.
    /// </remarks>
    public static bool Runs(LogicActionWhen when, bool result) => when switch
    {
        LogicActionWhen.IfTrue => result,
        LogicActionWhen.IfFalse => !result,
        _ => true,
    };

    /// <summary>
    /// Runs one action, if the result says it should.
    /// </summary>
    /// <param name="when">Whether it runs on true, on false, or always.</param>
    /// <param name="result">How the block's final gate came out.</param>
    /// <param name="type">The action.</param>
    /// <param name="parameter">Its parameter, before substitution.</param>
    /// <param name="values">The working slots, for <c>&amp;A</c>‥<c>&amp;L</c> substitution.</param>
    /// <param name="host">The state the action writes.</param>
    /// <param name="runScript">
    /// Runs a GPDL program. <b>With no arguments</b> — unlike the input side, which passes the six
    /// working slots. Null refuses the two GPDL actions.
    /// </param>
    /// <returns>True when the action ran, false when the result held it back.</returns>
    /// <exception cref="NotSupportedException">
    /// For an action whose machinery this port does not have — see the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b><see cref="LogicAction.SetIconIndexByName"/> does not substitute its parameter.</b> Every
    /// other action calls <c>LBsubst</c> first; this one reads <c>*param</c> raw
    /// (<c>RunEvent.cpp:14262</c>). Almost certainly an oversight, and reproduced — a design that
    /// worked around it by not using <c>&amp;</c> there would break if it were "fixed".
    /// </para>
    /// <para>
    /// <b>The GPDL action runs with no arguments.</b> <c>ExecuteScript(*param, 1, NULL, 0)</c>
    /// against the input side's <c>(*param, 1, w, 6)</c> — so an action script cannot see the
    /// terminals an input script can.
    /// </para>
    /// <para>
    /// <b>A bogus action type is logged and skipped</b>, as a bogus input terminal is. One bad
    /// action must not make a design unplayable.
    /// </para>
    /// </remarks>
    public static bool Run(LogicActionWhen when, bool result, LogicAction type, string parameter,
                           IReadOnlyList<string> values, ILogicBlockActionHost host,
                           Action<string>? runScript = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(host);

        if (!Runs(when, result))
        {
            return false;
        }

        switch (type)
        {
            case LogicAction.Nothing:
                break;

            case LogicAction.SetQuestStage:
            {
                (string quest, string stage) = SplitKeyValue(Substitute(parameter, values));
                host.SetQuestStage(quest, ParseInt(stage));
                break;
            }

            case LogicAction.SetGlobalAsl:
                Insert(host, LogicAslScope.Global, 0, Substitute(parameter, values));
                break;

            case LogicAction.RemoveGlobalAsl:
                host.RemoveAttribute(LogicAslScope.Global, 0, Substitute(parameter, values));
                break;

            case LogicAction.TempAsl:
                Insert(host, LogicAslScope.Temp, 0, Substitute(parameter, values));
                break;

            case LogicAction.SetPartyAsl:
                Insert(host, LogicAslScope.Party, 0, Substitute(parameter, values));
                break;

            case LogicAction.RemovePartyAsl:
                host.RemoveAttribute(LogicAslScope.Party, 0, Substitute(parameter, values));
                break;

            case LogicAction.SetLevelAsl:
            {
                (string levelKey, string value) = SplitKeyValue(Substitute(parameter, values));
                (int level, string key) = LogicBlockInputs.SplitLevelKey(levelKey, host.CurrentLevel);

                if (key.StartsWith(WallOverridePrefix, StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"a '{WallOverridePrefix}' key reroutes into Convert$Wall " +
                        "(Level.cpp:129), which sets a per-cell WALL_OVERRIDE_USER entry. Those " +
                        "tables are read but not threaded through the viewport or the combat map, " +
                        "so there is nothing for this to write to yet.");
                }

                host.SetLevelAttribute(level, key, value);
                break;
            }

            case LogicAction.RemoveLevelAsl:
            {
                (int level, string key) =
                    LogicBlockInputs.SplitLevelKey(Substitute(parameter, values),
                                                   host.CurrentLevel);
                host.RemoveLevelAttribute(level, key);
                break;
            }

            case LogicAction.SetCharAsl:
            {
                string sp = Substitute(parameter, values);
                int character = LogicBlockInputs.SeparateCharacter(ref sp, host);

                // The reference logs "Bogus character identifier in Set Character ASL" and stops
                // there; it does not fall back to the active character.
                //
                // NOTE the asymmetry with the input side, which tests
                // `(n < 0) || (n >= party.numCharacters)` and this one only `n < 0`
                // (:14237 against :13807). An index past the party would index out of bounds
                // there; it cannot arise from any selector form, which is presumably why it has
                // never been noticed.
                if (character < 0 || (character >= host.PartySize && character != AllCharacters))
                {
                    break;
                }

                (string key, string value) = SplitKeyValue(sp);

                if (character < AllCharacters)
                {
                    host.SetAttribute(LogicAslScope.Character, character, key, value);
                    break;
                }

                for (int i = 0; i < host.PartySize; i++)
                {
                    host.SetAttribute(LogicAslScope.Character, i, key, value);
                }
                break;
            }

            case LogicAction.SetIconIndexByName:
            {
                // NOT substituted -- see the remarks.
                (string name, string index) = SplitKeyValue(parameter);

                for (int i = 0; i < host.PartySize; i++)
                {
                    // Case-SENSITIVE, unlike the character selector's name form.
                    if (!string.Equals(host.CharacterName(i), name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Floored at 1: index 0 is not a usable icon.
                    host.SetIconIndex(i, Math.Max(ParseInt(index), 1));
                    break;
                }
                break;
            }

            case LogicAction.SourceGpdl:
            case LogicAction.BinaryGpdl:
                if (runScript is null)
                {
                    throw new NotSupportedException(
                        $"{type} needs a GPDL program run with no arguments " +
                        "(RunEvent.cpp:14318). Pass runScript to enable it.");
                }
                runScript(parameter);
                break;

            default:
                // Bogus type: logged and skipped in the reference, as a bogus input is.
                break;
        }

        return true;
    }

    /// <summary>The key prefix that reroutes a level attribute into the wall-override tables.</summary>
    public const string WallOverridePrefix = "$Wall,";

    private static string Substitute(string parameter, IReadOnlyList<string> values) =>
        LogicBlockInputs.Substitute(parameter, values);

    private static void Insert(ILogicBlockActionHost host, LogicAslScope scope, int character,
                               string parameter)
    {
        (string key, string value) = SplitKeyValue(parameter);
        host.SetAttribute(scope, character, key, value);
    }

    /// <summary>Parses an integer the way <c>atoi</c> does: no digits means zero.</summary>
    private static int ParseInt(string text)
    {
        int at = 0;
        bool negative = at < text.Length && text[at] is '-' or '+' && text[at++] == '-';

        int start = at;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at == start)
        {
            return 0;
        }

        int value = int.Parse(text[start..at], CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }
}
