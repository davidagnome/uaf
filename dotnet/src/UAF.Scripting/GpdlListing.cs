using System.Globalization;

namespace UAF.Scripting;

/// <summary>
/// Port of <c>GPDLCOMP::list</c> (GPDLcomp.cpp:4008) — the human-readable disassembly that
/// <c>GPDLcomp</c> writes to its optional third argument.
/// </summary>
/// <remarks>
/// <para>
/// This is the most useful artefact for oracle diffing short of the binary itself: it is text, so a
/// mismatch points at an address and a mnemonic instead of a byte offset. The format is fixed by the
/// original's <c>fprintf</c>s and is reproduced exactly, including the eight leading spaces and the
/// trailing space after a mnemonic with no operand.
/// </para>
/// <para>
/// One deliberate omission: the original carries a <c>skip</c> counter that is initialised to 0,
/// tested, and decremented — but never incremented, so the branch is dead. It is not reproduced.
/// </para>
/// </remarks>
public static class GpdlListing
{
    /// <summary>
    /// <c>submneumonics[]</c> (GPDLcomp.cpp:3973), consulted only for sub-opcodes with no entry in
    /// <c>systemfunctions[]</c> — the compiler-internal ones. The last row is the "?????" fallback
    /// and the search deliberately stops one short of it so that an unknown sub-opcode lands there.
    /// </summary>
    private static readonly (SubOp Op, string Mnemonic)[] SubMnemonics =
    [
        (SubOp.SUBOP_DUP, "Dup"),
        (SubOp.SUBOP_ISEQUAL, "=="),
        (SubOp.SUBOP_LESS, "<"),
        (SubOp.SUBOP_LESSEQUAL, "<="),
        (SubOp.SUBOP_GREATER, ">"),
        (SubOp.SUBOP_GREATEREQUAL, ">="),
        (SubOp.SUBOP_nISEQUAL, "==#"),
        (SubOp.SUBOP_nNOTEQUAL, "!=#"),
        (SubOp.SUBOP_NOTEQUAL, "!="),
        (SubOp.SUBOP_nLESS, "<#"),
        (SubOp.SUBOP_nLESSEQUAL, "<=#"),
        (SubOp.SUBOP_nMINUS, "-#"),
        (SubOp.SUBOP_nGREATER, ">#"),
        (SubOp.SUBOP_nGREATEREQUAL, ">=#"),
        (SubOp.SUBOP_nTIMES, "*#"),
        (SubOp.SUBOP_nSLASH, "/#"),
        (SubOp.SUBOP_nPERCENT, "%#"),
        (SubOp.SUBOP_nPLUS, "+#"),
        (SubOp.SUBOP_nNEGATE, "nNegate"),
        (SubOp.SUBOP_nAND, "&#"),
        (SubOp.SUBOP_nOR, "|#"),
        (SubOp.SUBOP_nXOR, "^#"),
        (SubOp.SUBOP_NOOP, "Noop"),
        (SubOp.SUBOP_LAND, "&&"),
        (SubOp.SUBOP_LOR, "||"),
        (SubOp.SUBOP_POP, "Pop"),
        (SubOp.SUBOP_PLUS, "$PLUS"),
        (SubOp.SUBOP_OVER, "Over"),
        (SubOp.SUBOP_SWAP, "Swap"),
        (SubOp.SUBOP_ILLEGAL, "?????"),
        (SubOp.SUBOP_FORCENUMERIC, "numeric"),
    ];

    /// <summary><c>mneumonics[]</c> (GPDLcomp.cpp:3962).</summary>
    private static string MnemonicFor(BinOp op) => op switch
    {
        BinOp.BINOP_CALL => "call",
        BinOp.BINOP_ReferenceGLOBAL => "FetchGlobal",
        BinOp.BINOP_JUMP => "Jump",
        BinOp.BINOP_JUMPFALSE => "JumpFalse",
        BinOp.BINOP_STORE_FP => "Store_FP",
        BinOp.BINOP_FETCH_FP => "Fetch_FP",
        BinOp.BINOP_RETURN => "$RETURN",
        BinOp.BINOP_LOCALS => "AllocateLocals",
        // Everything not in the table -- including BINOP_FETCHTEXT and BINOP_ILLEGAL -- falls onto
        // the last row, which is BINOP_SUBOP's "?????".
        _ => "?????",
    };

    /// <summary>Writes the assembly listing for a compiler that has just finished a compile.</summary>
    public static void Write(GpdlCompiler compiler, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(writer);

        uint[] code = compiler.Code;
        var globals = compiler.Globals;

        for (uint k = 0; k < code.Length; k++)
        {
            string funcName = compiler.Root.FindUserFunc(k);
            if (funcName.Length != 0) { writer.Write(funcName + "\n"); }

            uint bincode = code[k];
            writer.Write(string.Format(
                CultureInfo.InvariantCulture,
                "        {0:x6} {1:x2} {2:x6} ",
                k, bincode >> 24, bincode & 0xffffff));

            var opcode = GpdlCode.OpOf(bincode);
            uint subop = GpdlCode.OperandOf(bincode);
            string mnemonic = MnemonicFor(opcode);
            string operand = string.Empty;

            switch (opcode)
            {
                case BinOp.BINOP_CALL:
                    operand = compiler.Root.FindUserFunc(subop);
                    break;

                case BinOp.BINOP_ReferenceGLOBAL:
                    if ((bincode & GpdlCode.GlobalStoreBit) != 0) { mnemonic = "StoreGlobal"; }
                    // Note the mask here is 0x7fffff, not 0xffffff: bit 23 is the store flag.
                    int index = (int)(bincode & 0x7fffff);
                    operand = globals.GetType(index) == 'C'
                        ? "\"" + globals.GetValue(index) + "\""
                        : globals.GetValue(index);
                    operand = MfcString.Left(operand, 30);
                    break;

                case BinOp.BINOP_JUMP:
                case BinOp.BINOP_STORE_FP:
                case BinOp.BINOP_FETCH_FP:
                case BinOp.BINOP_RETURN:
                case BinOp.BINOP_LOCALS:
                case BinOp.BINOP_JUMPFALSE:
                    operand = string.Empty;
                    break;

                case BinOp.BINOP_SUBOP:
                    {
                        var fn = GpdlSystemFunctions.FindBySubOp((SubOp)subop);
                        if (fn is not null) { mnemonic = fn.Name; }
                        else { mnemonic = SubMnemonicFor((SubOp)subop); }
                        break;
                    }

                default:
                    operand = "?????";
                    break;
            }

            writer.Write(mnemonic + " " + operand + "\n");
        }
    }

    private static string SubMnemonicFor(SubOp subop)
    {
        // GPDLcomp.cpp:4073 loops to numSubMneumonic-1 and then indexes with the loop variable, so a
        // sub-opcode that is in neither table is labelled with whatever the LAST row happens to be.
        // That row is SUBOP_FORCENUMERIC, so an unknown sub-opcode disassembles as "numeric" --
        // misleading, but it is what the reference listing says. The "?????" row above it is only
        // reached by an explicit SUBOP_ILLEGAL.
        for (int i = 0; i < SubMnemonics.Length - 1; i++)
        {
            if (SubMnemonics[i].Op == subop) { return SubMnemonics[i].Mnemonic; }
        }
        return SubMnemonics[^1].Mnemonic;
    }

    /// <summary>Convenience wrapper returning the listing as a string.</summary>
    public static string ToText(GpdlCompiler compiler)
    {
        using var sw = new StringWriter();
        Write(compiler, sw);
        return sw.ToString();
    }
}
