using System.Globalization;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What one of a logic block's five input terminals reads (<c>LOGIC_BLOCK_INPUT_TYPE</c>,
/// <c>GameEvent.h:3116</c>).
/// </summary>
/// <remarks>
/// <b>The ordinals are not the order the header lists them in a second time.</b> A commented-out
/// copy of this enum sits at <c>RunEvent.cpp:13714</c> in a <i>different</i> order — the live one
/// is the header's, and it is serialized as a <c>BYTE</c>, so the numbering is load-bearing.
/// </remarks>
public enum LogicInput : byte
{
    Literal = 0,
    GlobalAsl = 1,
    PartySize = 2,
    CharInfo = 3,
    DirFacing = 4,
    LevelAsl = 5,
    QuestStage = 6,
    ItemList = 7,
    NpcList = 8,
    RunTimeIf = 9,
    CharAsl = 10,
    PartyAsl = 11,
    Wiggle = 12,
    SourceGpdl = 13,
    BinaryGpdl = 14,
    TempAsl = 15,
    NotImplemented = 0xFF,
}

/// <summary>
/// The state a logic block's inputs read. Everything a terminal can reach, and nothing else.
/// </summary>
/// <remarks>
/// Narrow on purpose: <c>ProcessLBInput</c> reaches into six globals, and naming what it actually
/// needs is what lets the sixteen cases be tested without a running game.
/// </remarks>
public interface ILogicBlockHost
{
    /// <summary>How many characters are in the party.</summary>
    int PartySize { get; }

    /// <summary>The index the party's selectors default to.</summary>
    int ActiveCharacter { get; }

    /// <summary>The party's facing, as its ordinal.</summary>
    int Facing { get; }

    /// <summary>
    /// The level being played — where an unqualified <see cref="LogicInput.LevelAsl"/> key looks.
    /// </summary>
    int CurrentLevel { get; }

    /// <summary>A character's name, for the <c>(name)</c> selector form.</summary>
    string CharacterName(int index);

    /// <summary>An attribute from one of the four stores a terminal can read.</summary>
    string Attribute(LogicAslScope scope, int character, string key);

    /// <summary>An attribute from one level's store, for <see cref="LogicInput.LevelAsl"/>.</summary>
    string LevelAttribute(int level, string key);

    /// <summary>The stage a quest has reached, as a number.</summary>
    int QuestStage(string quest);

    /// <summary>Every party member's items, concatenated (<c>getItemList</c>).</summary>
    string ItemList();

    /// <summary>The NPCs in the party, as <c>/name/index/</c> runs.</summary>
    string NpcList();

    /// <summary>The player characters in the party, as <c>/info/</c> runs.</summary>
    string CharInfo();

    /// <summary>
    /// A capture group from the most recent <see cref="LogicGate.Grep"/>, or empty when there is
    /// no such group.
    /// </summary>
    string GrepCapture(int group);
}

/// <summary>Which attribute store a terminal reads.</summary>
public enum LogicAslScope
{
    Global,
    Temp,
    Party,
    Character,
}

/// <summary>
/// Reads a logic block's input terminals (<c>ProcessLBInput</c>, <c>RunEvent.cpp:13777</c>).
/// </summary>
/// <remarks>
/// <para>
/// The half of <c>LOGIC_BLOCK_DATA</c> that <see cref="LogicBlock"/> was deliberately left without:
/// the gate network was ported and tested first, unwired, because all-false inputs would have made
/// every block take a branch rather than fail visibly.
/// </para>
/// <para>
/// <b>Every input is a string and truth is "not empty"</b> — see <see cref="LogicBlock"/>. So
/// <see cref="LogicInput.PartySize"/> yielding <c>"0"</c> is <b>true</b>, and an absent attribute
/// yielding <c>""</c> is false. Formatting matters as much as the value.
/// </para>
/// </remarks>
public static class LogicBlockInputs
{
    /// <summary>
    /// Substitutes <c>&amp;A</c>‥<c>&amp;L</c> in a parameter with the working values behind them
    /// (<c>LBsubst</c>, <c>RunEvent.cpp:13645</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what lets one terminal's parameter name another terminal's result — a logic block's
    /// only means of composition.
    /// </para>
    /// <para>
    /// <b>The reference hangs on an <c>&amp;</c> that names no slot.</b> Its loop advances
    /// <c>col</c> in the <c>else</c> of <c>if (p[col]=='&amp;')</c> and in the substitution branch,
    /// and <i>nowhere else</i> — so a <c>&amp;</c> whose successor is outside <c>A</c>‥<c>L</c>,
    /// including a trailing one, spins forever on the same character. A logic-block parameter
    /// containing an ordinary ampersand — <c>"Bell &amp; Dragon"</c> — locks the game up.
    /// </para>
    /// <para>
    /// This port <b>advances past it and keeps the character</b>, which is the only non-hanging
    /// reading and matches what the code plainly intends. The divergence is deliberate and is the
    /// one place in this file where the reference's behaviour is not reproduced, because
    /// reproducing it means reproducing a freeze.
    /// </para>
    /// </remarks>
    public static string Substitute(string parameter, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(values);

        if (!parameter.Contains('&', StringComparison.Ordinal))
        {
            return parameter;
        }

        var result = new System.Text.StringBuilder(parameter.Length);
        int start = 0;
        int col = 0;

        while (col < parameter.Length)
        {
            if (parameter[col] != '&')
            {
                col++;
                continue;
            }

            // Only A..L name a slot. Anything else -- including a trailing '&' -- is ordinary
            // text, and the reference does not advance past it either.
            if (col < parameter.Length - 1 && parameter[col + 1] is >= 'A' and <= 'L')
            {
                result.Append(parameter, start, col - start);
                col++;
                int slot = parameter[col] - 'A';
                result.Append(slot < values.Count ? values[slot] : string.Empty);
                col++;
                start = col;
            }
            else
            {
                // The reference does NOT advance here, and hangs. See the remarks: advancing is
                // the deliberate divergence.
                col++;
            }
        }

        if (col != start)
        {
            result.Append(parameter, start, col - start);
        }

        return result.ToString();
    }

    /// <summary>Every character, rather than one (<c>LBseparateCharacter</c>'s <c>(*)</c>).</summary>
    public const int AllCharacters = 999;

    /// <summary>No character matched the selector.</summary>
    public const int NoCharacter = -1;

    /// <summary>
    /// Strips a leading character selector from a parameter and says which character it names
    /// (<c>LBseparateCharacter</c>, <c>RunEvent.cpp:13673</c>).
    /// </summary>
    /// <param name="parameter">
    /// On return, the parameter with the selector removed — an attribute key, usually.
    /// </param>
    /// <returns>
    /// A party index, <see cref="AllCharacters"/> for <c>(*)</c>, or <see cref="NoCharacter"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Four forms, and the grammar is unforgiving: no selector at all means the active character;
    /// <c>(*)</c> means every character; <c>(^)</c> means the active one and <c>(^n)</c> the
    /// <i>n</i>th, one-based and single-digit; and anything else in parentheses is a name, matched
    /// case-insensitively.
    /// </para>
    /// <para>
    /// <b><c>(^n)</c> is one-based and caps at nine.</b> The reference tests
    /// <c>'1' &lt;= c &lt;= '9'</c> and subtracts one, so <c>(^0)</c> is invalid and there is no
    /// two-digit form — which matters only because a party can hold twelve.
    /// </para>
    /// </remarks>
    public static int SeparateCharacter(ref string parameter, ILogicBlockHost host)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(host);

        int length = parameter.Length;
        if (length == 0 || parameter[0] != '(')
        {
            return host.ActiveCharacter;
        }

        // The shortest possible selector is three characters: (^) or (a) or (*).
        if (length < 3)
        {
            return NoCharacter;
        }

        if (parameter.StartsWith("(*)", StringComparison.Ordinal))
        {
            parameter = parameter[3..];
            return AllCharacters;
        }

        if (parameter[1] == '^')
        {
            if (parameter == "(^)")
            {
                return host.ActiveCharacter;
            }

            if (length < 4 || parameter[3] != ')' || parameter[2] is < '1' or > '9')
            {
                return NoCharacter;
            }

            int numbered = parameter[2] - '0' - 1;
            if (numbered >= host.PartySize)
            {
                return NoCharacter;
            }

            parameter = parameter[4..];
            return numbered;
        }

        int close = parameter.IndexOf(')', 1);
        if (close < 0)
        {
            return NoCharacter;
        }

        string name = parameter[1..close];
        for (int i = 0; i < host.PartySize; i++)
        {
            if (string.Equals(name, host.CharacterName(i), StringComparison.OrdinalIgnoreCase))
            {
                parameter = parameter[(close + 1)..];
                return i;
            }
        }

        return NoCharacter;
    }

    /// <summary>
    /// Reads one input terminal.
    /// </summary>
    /// <param name="type">The terminal's <see cref="LogicInput"/>.</param>
    /// <param name="parameter">Its parameter, before substitution.</param>
    /// <param name="values">The working slots, for <c>&amp;A</c>‥<c>&amp;L</c> substitution.</param>
    /// <param name="host">The state the terminal reads.</param>
    /// <param name="runScript">
    /// Runs a GPDL program with the working slots as its six arguments, for the two GPDL terminals.
    /// Null refuses them.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// For a terminal whose state this port does not have — see the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>An unrecognised type is not an error that stops the block.</b> The reference logs
    /// "Bogus Logic Input-&lt;letter&gt; Type" and leaves the result as it found it — empty — so
    /// the terminal reads false and the block carries on. Throwing here would make a design with
    /// one bad terminal unplayable where the original merely misbehaves.
    /// </para>
    /// <para>
    /// <b><see cref="LogicInput.SourceGpdl"/> falls through into
    /// <see cref="LogicInput.BinaryGpdl"/></b>, and the reference marks it with a three-line
    /// comment because it is a deliberate fallthrough in a 200-line switch. It also *rewrites the
    /// event*: on a compile error it sets the terminal's type to <see cref="LogicInput.Literal"/>
    /// and blanks the parameter, so the block never tries again.
    /// </para>
    /// </remarks>
    public static string Read(LogicInput type, string parameter, IReadOnlyList<string> values,
                              ILogicBlockHost host, Func<string, IReadOnlyList<string>, string>? runScript = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(host);

        switch (type)
        {
            case LogicInput.Literal:
                return Substitute(parameter, values);

            case LogicInput.QuestStage:
                return host.QuestStage(Substitute(parameter, values))
                           .ToString(CultureInfo.InvariantCulture);

            case LogicInput.PartySize:
                return host.PartySize.ToString(CultureInfo.InvariantCulture);

            case LogicInput.DirFacing:
                return host.Facing.ToString(CultureInfo.InvariantCulture);

            case LogicInput.GlobalAsl:
                return host.Attribute(LogicAslScope.Global, 0, Substitute(parameter, values));

            case LogicInput.TempAsl:
                return host.Attribute(LogicAslScope.Temp, 0, Substitute(parameter, values));

            case LogicInput.PartyAsl:
                return host.Attribute(LogicAslScope.Party, 0, Substitute(parameter, values));

            case LogicInput.CharAsl:
            {
                string key = Substitute(parameter, values);
                int character = SeparateCharacter(ref key, host);

                // A selector naming nobody reads empty rather than throwing -- the reference logs
                // "Bogus character selector in Input" and moves on.
                return character < 0 || character >= host.PartySize
                    ? string.Empty
                    : host.Attribute(LogicAslScope.Character, character, key);
            }

            case LogicInput.ItemList:
                return host.ItemList();

            case LogicInput.NpcList:
                return host.NpcList();

            case LogicInput.CharInfo:
                return host.CharInfo();

            case LogicInput.LevelAsl:
            {
                (int level, string key) =
                    SplitLevelKey(Substitute(parameter, values), host.CurrentLevel);
                return host.LevelAttribute(level, key);
            }

            case LogicInput.Wiggle:
            {
                // A capture group from the last grep gate. A non-numeric parameter parses as 0,
                // matching atoi -- and group 0 is the whole match, which is meaningful.
                string text = Substitute(parameter, values);
                return host.GrepCapture(ParseLeadingInt(text));
            }

            case LogicInput.SourceGpdl:
            case LogicInput.BinaryGpdl:
                return runScript is null
                    ? throw new NotSupportedException(
                        $"{type} needs a GPDL program run with the working slots as its six " +
                        "arguments (RunEvent.cpp:13953). Pass runScript to enable it.")
                    : runScript(parameter, values);

            case LogicInput.RunTimeIf:
                throw new NotSupportedException(
                    "LBIT_RunTimeIf reads a runtime IF keyword through GetDataSTRING and its " +
                    "seven width-specific siblings (RunEvent.cpp:13836), which need the keyword " +
                    "table this port does not have.");

            default:
                // Bogus type: the reference logs and leaves the result empty, so the terminal
                // reads false and the block still runs.
                return string.Empty;
        }
    }

    /// <summary>
    /// Splits a <c>LevelAsl</c> parameter into its level number and key
    /// (<c>SplitLevelKey</c>, <c>RunEvent.cpp:13747</c>).
    /// </summary>
    /// <param name="currentLevel">Where an unqualified key addresses — the level being played.</param>
    /// <remarks>
    /// <para>
    /// The form is <c>/&lt;digits&gt;/&lt;key&gt;</c>. <b>Anything else is the current level and
    /// the whole string as the key</b>, including a leading slash with no closing one — the loop
    /// simply runs out and falls through. A parameter that merely starts with digits addresses the
    /// current level, not level 0.
    /// </para>
    /// <para>
    /// <b>Non-digits inside the number are skipped, not terminal.</b> The loop <c>continue</c>s on
    /// a digit and returns on a slash, and does neither for anything else — so <c>/1a2/key</c>
    /// reads as level <b>12</b>. Almost certainly unintended, and reproduced because a design
    /// written against it would break otherwise.
    /// </para>
    /// </remarks>
    public static (int Level, string Key) SplitLevelKey(string parameter, int currentLevel)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter.Length > 1 && parameter[0] == '/')
        {
            int level = 0;
            for (int i = 1; i < parameter.Length; i++)
            {
                if (char.IsAsciiDigit(parameter[i]))
                {
                    level = (10 * level) + (parameter[i] - '0');
                    continue;
                }

                if (parameter[i] == '/')
                {
                    return (level, parameter[(i + 1)..]);
                }

                // Anything else is neither counted nor fatal -- the loop just carries on.
            }
        }

        return (currentLevel, parameter);
    }

    /// <summary>Parses a leading integer the way <c>atoi</c> does: no digits means zero.</summary>
    private static int ParseLeadingInt(string text)
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
