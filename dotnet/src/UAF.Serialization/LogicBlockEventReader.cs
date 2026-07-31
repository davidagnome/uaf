using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// A <c>LOGIC_BLOCK_DATA</c> — a small boolean circuit gating other events.
/// </summary>
/// <param name="Inputs">Parameters A, B, D, F, G — the letters are circuit terminals, not indices.</param>
/// <param name="ActionParams">Parameters for the two actions.</param>
/// <param name="GateTypes">Gates C, E, H, I, J, K, L.</param>
/// <param name="InputTypes">Types for the five inputs.</param>
/// <param name="Negations">Whether gates C, E, H, I, J, K are inverted.</param>
public sealed record LogicBlockEvent(
    GameEventBase Base, uint FalseChain, uint TrueChain,
    IReadOnlyList<string> Inputs, IReadOnlyList<string> ActionParams,
    IReadOnlyList<byte> GateTypes, IReadOnlyList<byte> InputTypes,
    IReadOnlyList<byte> ActionTypes,
    byte ChainIfFalse, byte ChainIfTrue, byte NoChain,
    IReadOnlyList<byte> Negations, IReadOnlyList<byte> IfTrue,
    byte Flags, string Misc) : IGameEvent;

/// <summary>
/// Reads <c>LOGIC_BLOCK_DATA</c> (<c>GameEvent.cpp:14103</c>).
/// </summary>
/// <remarks>
/// <para>
/// The largest event subclass: two <c>DWORD</c>s, seven strings, then <b>twenty-six consecutive
/// <c>BYTE</c>s</b>, then one more string. That byte run is the densest in the format, and every
/// one of those fields has a name that reads like an enum — <c>m_GateTypeC</c>,
/// <c>m_ActionType1</c>, <c>m_Flags</c> — so the temptation to widen them to <c>int</c> is
/// constant. Doing so for even one field costs three bytes and desynchronises the rest.
/// </para>
/// <para>
/// The letters are terminal labels on a circuit diagram, not indices: inputs are A, B, D, F, G and
/// gates are C, E, H, I, J, K, L. The gaps are meaningful to the editor's UI, not to the format,
/// but they make an off-by-one very easy to introduce when transcribing.
/// </para>
/// <para>
/// The strings are read raw, without the <c>DAS</c> blank convention.
/// </para>
/// </remarks>
public static class LogicBlockEventReader
{
    /// <summary>Input terminals, in serialized order.</summary>
    public static readonly char[] InputTerminals = ['A', 'B', 'D', 'F', 'G'];

    /// <summary>Gate terminals, in serialized order.</summary>
    public static readonly char[] GateTerminals = ['C', 'E', 'H', 'I', 'J', 'K', 'L'];

    /// <summary>Gates that carry a negation flag — L has none.</summary>
    public static readonly char[] NegatedTerminals = ['C', 'E', 'H', 'I', 'J', 'K'];

    public static LogicBlockEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        uint falseChain = ar.ReadUInt32();
        uint trueChain = ar.ReadUInt32();

        var inputs = ReadStrings(ar, InputTerminals.Length);        // A, B, D, F, G
        var actionParams = ReadStrings(ar, 2);

        var gateTypes = ReadBytes(ar, GateTerminals.Length);        // C, E, H, I, J, K, L
        var inputTypes = ReadBytes(ar, InputTerminals.Length);
        var actionTypes = ReadBytes(ar, 2);

        byte chainIfFalse = ar.ReadByte();
        byte chainIfTrue = ar.ReadByte();
        byte noChain = ar.ReadByte();

        var negations = ReadBytes(ar, NegatedTerminals.Length);     // C, E, H, I, J, K -- not L
        var ifTrue = ReadBytes(ar, 2);

        byte flags = ar.ReadByte();
        string misc = ar.ReadString();

        return new LogicBlockEvent(
            baseEvent, falseChain, trueChain, inputs, actionParams,
            gateTypes, inputTypes, actionTypes,
            chainIfFalse, chainIfTrue, noChain, negations, ifTrue, flags, misc);
    }

    private static List<string> ReadStrings(IArchiveCursor ar, int count)
    {
        var values = new List<string>(count);
        for (int i = 0; i < count; i++) values.Add(ar.ReadString());
        return values;
    }

    private static List<byte> ReadBytes(IArchiveCursor ar, int count)
    {
        var values = new List<byte>(count);
        for (int i = 0; i < count; i++) values.Add(ar.ReadByte());
        return values;
    }
}
