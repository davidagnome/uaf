using System.Buffers.Binary;
using System.Text;

namespace UAF.Scripting;

/// <summary>
/// Writes and reads the <c>talk.bin</c> container: the three segments a compiled GPDL program
/// consists of, encoded with MFC <c>CArchive</c> primitives.
/// </summary>
/// <remarks>
/// <para>
/// The layout comes from <c>main()</c> in src/GPDL/GPDL.cpp:96, which calls
/// <c>WriteCode</c>, then <c>WriteConstants</c>, then <c>WriteDictionary</c> into one
/// <c>CArchive</c>. There is <b>no magic number, no version field and no object schema</b> —
/// nothing is written through <c>CArchive::WriteObject</c>, so a reader has no way to validate the
/// file beyond the plausibility of the counts:
/// </para>
/// <code>
/// uint32  codeLength                 // CODE::write, GPDLcomp.cpp:840
/// uint32 xcodeLength code words
/// uint32  globalCount                // GLOBALS::write, GPDLcomp.cpp:1905  (includes unused slot 0)
/// string xglobalCount                // variables written as ""
/// uint32  publicFunctionCount        // DICTIONARY::write, GPDLcomp.cpp:1229
/// { string name; uint32 address } xpublicFunctionCount
/// </code>
/// <para>
/// Strings use MFC's <c>_AfxWriteStringLength</c> prefix and carry <b>no NUL terminator</b>. The
/// escape thresholds are asymmetric and easy to get wrong: a length of exactly 255 does <i>not</i>
/// fit in the single byte (the test is <c>&lt; 255</c>, not <c>&lt;= 255</c>), and the two-byte
/// form stops one short of <c>0xFFFF</c> because <c>0xFFFE</c> is reserved as the Unicode tag.
/// </para>
/// <para>
/// Because <c>CharacterSet=MultiByte</c>, characters are single bytes in a Windows codepage. The
/// Unicode tag is therefore never written on this path, and a reader that honours it would
/// mis-parse a legitimate 65534-character string.
/// </para>
/// </remarks>
public static class GpdlBinaryWriter
{
    /// <summary>Writes a compiled program in <c>talk.bin</c> order.</summary>
    public static void Write(Stream stream, GpdlProgram program, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(program);
        encoding ??= MfcString.Encoding;

        WriteUInt32(stream, (uint)program.Code.Length);
        foreach (uint word in program.Code) { WriteUInt32(stream, word); }

        WriteUInt32(stream, (uint)program.Globals.Length);
        foreach (string value in program.Globals) { WriteString(stream, value, encoding); }

        WriteUInt32(stream, (uint)program.Index.Count);
        foreach (var (name, address) in program.Index)
        {
            WriteString(stream, name, encoding);
            WriteUInt32(stream, address);
        }
    }

    /// <summary>Serialises a compiled program to a byte array.</summary>
    public static byte[] ToBytes(GpdlProgram program, Encoding? encoding = null)
    {
        using var ms = new MemoryStream();
        Write(ms, program, encoding);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads a program back, mirroring <c>GPDL::Load(CArchive&amp;)</c> (GPDLexec.cpp:591) —
    /// <c>ReadProgram</c>, then <c>GLOBALS::read</c>, then <c>INDEX::read</c>.
    /// </summary>
    public static GpdlProgram Read(Stream stream, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= MfcString.Encoding;

        uint codeLength = ReadUInt32(stream);
        uint[] code = new uint[codeLength];
        for (uint i = 0; i < codeLength; i++) { code[i] = ReadUInt32(stream); }

        uint globalCount = ReadUInt32(stream);
        string[] globals = new string[globalCount];
        for (uint i = 0; i < globalCount; i++) { globals[i] = ReadString(stream, encoding); }

        uint indexCount = ReadUInt32(stream);
        var index = new List<(string, uint)>((int)indexCount);
        for (uint i = 0; i < indexCount; i++)
        {
            // INDEX::read trims the name on both sides (GPDLexec.cpp:7340). The compiler never
            // writes a padded name, so this only matters for hand-edited files -- but a lookup
            // against an untrimmed name would silently fail, which is why it is kept.
            string name = ReadString(stream, encoding).Trim();
            index.Add((name, ReadUInt32(stream)));
        }

        return new GpdlProgram(code, globals, index);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        stream.Write(b);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> b = stackalloc byte[4];
        ReadExactly(stream, b);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer[read..]);
            if (n == 0)
            {
                throw new EndOfStreamException(
                    $"Expected {buffer.Length} bytes at offset {stream.Position}, got {read}.");
            }
            read += n;
        }
    }

    /// <summary>
    /// <c>_AfxWriteStringLength</c> with <c>bUnicode = FALSE</c>, then the raw bytes.
    /// </summary>
    private static void WriteString(Stream stream, string value, Encoding encoding)
    {
        byte[] bytes = encoding.GetBytes(value);
        uint length = (uint)bytes.Length;
        if (length < 255)
        {
            stream.WriteByte((byte)length);
        }
        else if (length < 0xfffe)
        {
            stream.WriteByte(0xff);
            Span<byte> w = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)length);
            stream.Write(w);
        }
        else
        {
            stream.WriteByte(0xff);
            Span<byte> w = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(w, 0xffff);
            stream.Write(w);
            WriteUInt32(stream, length);
        }
        stream.Write(bytes);
    }

    private static string ReadString(Stream stream, Encoding encoding)
    {
        Span<byte> one = stackalloc byte[1];
        ReadExactly(stream, one);
        uint length = one[0];
        if (length == 0xff)
        {
            Span<byte> w = stackalloc byte[2];
            ReadExactly(stream, w);
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(w);
            length = word == 0xffff ? ReadUInt32(stream) : word;
        }
        if (length == 0) { return string.Empty; }
        byte[] bytes = new byte[length];
        ReadExactly(stream, bytes);
        return encoding.GetString(bytes);
    }
}
