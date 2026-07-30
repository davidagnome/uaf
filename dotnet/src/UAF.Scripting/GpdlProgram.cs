namespace UAF.Scripting;

/// <summary>
/// A compiled GPDL program: the three segments the compiler emits and the VM loads.
/// </summary>
/// <remarks>
/// <para>
/// This corresponds to the trio <c>GPDL::m_program</c> / <c>GPDL::GLOBALS</c> / <c>GPDL::INDEX</c>
/// (GPDLexec.h:195–197), not to any single C++ type — the C++ keeps them as three separate members
/// of the interpreter.
/// </para>
/// <para>
/// <b><see cref="Globals"/> index 0 is unused.</b> The compiler never hands it out
/// (GPDLcomp.cpp:1791), so it is written to the file only to keep the indices aligned. Entries that
/// were <c>$VAR</c> declarations are stored as empty strings; the VM writes real values into those
/// slots at run time via <c>BINOP_ReferenceGLOBAL</c> with bit 23 set.
/// </para>
/// <para>
/// <b><see cref="Index"/> addresses are never 0.</b> <c>INDEX::lookup</c> returns 0 for "no such
/// function", which is why the compiler reserves address 0 for a <c>NOOP</c>.
/// </para>
/// </remarks>
public sealed class GpdlProgram
{
    public GpdlProgram(uint[] code, string[] globals, IReadOnlyList<(string Name, uint Address)> index)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Globals = globals ?? throw new ArgumentNullException(nameof(globals));
        Index = index ?? throw new ArgumentNullException(nameof(index));
    }

    /// <summary>The code segment: one 32-bit word per instruction.</summary>
    public uint[] Code { get; }

    /// <summary>The global pool: string constants and (empty) global-variable slots.</summary>
    public string[] Globals { get; }

    /// <summary>Public function names (<c>outer@inner@name</c>) and their code addresses.</summary>
    public IReadOnlyList<(string Name, uint Address)> Index { get; }

    /// <summary>
    /// <c>INDEX::lookup</c> (GPDLexec.cpp:7321): the entry address of a public function, or 0 when
    /// there is no such name. Comparison is exact and case-sensitive.
    /// </summary>
    public uint Lookup(string funcName)
    {
        foreach (var (name, address) in Index)
        {
            if (string.Equals(name, funcName, StringComparison.Ordinal)) { return address; }
        }
        return 0;
    }

    /// <summary>Builds a program from a compiler that has just finished a successful compile.</summary>
    public static GpdlProgram FromCompiler(GpdlCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return new GpdlProgram(
            compiler.Code,
            [.. compiler.Globals.SerializedValues()],
            compiler.PublicFunctionIndex());
    }
}
