namespace UAF.Scripting;

/// <summary>
/// The Forth kernel's own source (<c>char m[MAX_MEM] = …</c>, <c>Forth.cpp:221</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The dictionary is built by interpreting this text, not by C code.</b> Only two words —
/// <c>CREATE</c> and <c>+p</c> — are compiled by <c>ExpandKernel</c> calling their primitives
/// directly; everything after that is defined the ordinary way, by the outer interpreter reading
/// the words below. So the VM's job is to run this string, and a port that reimplemented the
/// dictionary in C# would be porting the wrong thing.
/// </para>
/// <para>
/// <b>Most of <c>Forth.cpp</c> is commented out.</b> Of the 1,415 lines the initialiser spans,
/// 181 are live: the rest are an earlier kernel and a reference copy of <c>THINK</c>, both inside
/// <c>/* … */</c>. What is here is the live text with the <c>sm_*</c> macros expanded
/// (<c>sm_DP</c> → <c>" 2"</c>, <c>sm_LATEST</c> → <c>" 4"</c>, <c>sm_2IN</c> → <c>" 10"</c>,
/// <c>sm_STATE</c> → <c>" 12"</c>), which is what the C preprocessor hands the compiler.
/// </para>
/// <para>
/// <b>The <c>PRIM</c> lines are positional.</b> <c>+p</c> stamps <c>nextPrim++</c> into the word
/// it has just created, so the <i>n</i>th primitive named here becomes index <i>n</i> in
/// <see cref="ForthMachine"/>'s table. Reordering either side silently rebinds every word after
/// the change — <c>ForthMachineTests</c> asserts the two agree.
/// </para>
/// </remarks>
public static class ForthKernel
{
    /// <summary>Words in the kernel source, for a test that wants to know it is all here.</summary>
    public const int WordCount = 697;

    /// <inheritdoc cref="ForthKernel"/>
    public const string Source =
        " CREATE +p CREATE ' +p CREATE LIT +p CREATE @ +p CREATE NOP +p CREATE SETIMMEDIATE +p" +
        " CREATE [ +p 4 @ SETIMMEDIATE CREATE ] +p CREATE 0< +p CREATE C@ +p CREATE C! +p" +
        " CREATE +! +p CREATE ! +p CREATE 1- +p CREATE DUP +p CREATE ROT +p CREATE EXIT +p" +
        " CREATE ?EXIT +p CREATE SP+- +p CREATE docolon +p 4 @ 1- C@ ] LIT [ 2 DUP @ 2 ROT +!" +
        " ! ] 4 @ 1- C! EXIT [ CREATE -->OP docolon ] 1- EXIT [ CREATE OP! docolon ] -->OP C!" +
        " EXIT [ -2 SP+- CREATE PRIM docolon ] CREATE +p EXIT [ PRIM BRANCH PRIM ?BRANCH PRIM" +
        " OVER PRIM HEAD PRIM WORD PRIM FIND PRIM SWAP PRIM >R PRIM R> PRIM OR PRIM XOR PRIM" +
        " AND PRIM ABS PRIM DROP PRIM + PRIM - PRIM NEGATE PRIM DNEGATE PRIM UM* PRIM M* PRIM" +
        " = PRIM NOT PRIM != PRIM < PRIM > PRIM ?DUP PRIM MU/MOD PRIM DEBUG PRIM EXECUTE" +
        " CREATE , docolon ] 2 DUP @ 2 ROT +! ! EXIT [ -1 SP+- PRIM docon 4 @ -->OP C@ docolon" +
        " ] LIT [ , ] 4 @ OP! EXIT [ CREATE CONSTANT docolon ] CREATE docon , EXIT [ -1 SP+-" +
        " H'20' CONSTANT BL 10 CONSTANT >IN 4 CONSTANT LATEST 2 CONSTANT DP 12 CONSTANT STATE" +
        " CREATE HIDE docolon ] H'04' LATEST @ 2 - DUP >R C@ OR R> C! EXIT [ CREATE REVEAL" +
        " docolon ] H'FB' LATEST @ 2 - DUP >R C@ AND R> C! EXIT [ CREATE : docolon ] CREATE" +
        " docolon HIDE ] EXIT [ : IMMEDIATE LATEST @ SETIMMEDIATE EXIT [ : ; REVEAL [ ' [ , ]" +
        " LIT EXIT , EXIT [ IMMEDIATE : 2DUP OVER OVER ; : ['] LIT LIT , ' , EXIT [ IMMEDIATE" +
        " : NIP SWAP DROP ; : S>D DUP 0< ; : HERE DP @ ; 1 SP+- : >MARK HERE 0 , ; 1 SP+- :" +
        " >RESOLVE HERE SWAP ! ; -1 SP+- : 2+ 2 + ; : COMPILE R> DUP @ , 2+ >R ; : IF COMPILE" +
        " ?BRANCH >MARK ; IMMEDIATE 1 SP+- : ELSE COMPILE BRANCH >MARK SWAP >RESOLVE ;" +
        " IMMEDIATE : THEN >RESOLVE ; IMMEDIATE -1 SP+- : ?NEGATE 0< IF NEGATE THEN ; : ABS" +
        " DUP 0< IF NEGATE THEN ; : DABS DUP 0< IF DNEGATE THEN ; : UM/MOD MU/MOD DROP ; :" +
        " SM/REM 2DUP XOR >R OVER >R ABS >R DABS R> UM/MOD SWAP R> ?NEGATE SWAP R> ?NEGATE ; :" +
        " FM/MOD DUP >R SM/REM DUP 0< IF SWAP R> + SWAP 1- ELSE R> DROP THEN ; : SM/REM 2DUP" +
        " XOR >R OVER >R ABS >R DABS R> UM/MOD SWAP R> ?NEGATE SWAP R> ?NEGATE ; : * M* DROP ;" +
        " : /MOD >R S>D R> FM/MOD ; : / /MOD NIP ; : MOD /MOD DROP ; : */MOD >R M* R> FM/MOD ;" +
        " : U/MOD 0 SWAP MU/MOD DROP ; : */ */MOD NIP ; : MAX 2DUP < IF SWAP THEN DROP ; : MIN" +
        " 2DUP > IF SWAP THEN DROP ; : CHAR BL WORD 1 + C@ ; 1 SP+- : [CHAR] CHAR LIT LIT , ," +
        // The comment word is ONE backslash. C++ writes it "\\" in its own literal (Forth.cpp:481);
        // transcribing that source form rather than its value escaped it a second time, and the
        // kernel defined a word named \\ that no script ever names. The kernel still built, so
        // nothing caught it until a real AI_Script.BLK -- whose every comment line opens with \ --
        // was fed to the machine and aborted on its first line.
        " ; IMMEDIATE : ( [CHAR] ) WORD DROP ; IMMEDIATE : \\ 0 WORD DROP ; IMMEDIATE PRIM Me" +
        " PRIM He PRIM A PRIM B PRIM A:Type PRIM A:Damage PRIM W:Type PRIM W:Range PRIM" +
        " W:Protection PRIM W:Damage PRIM W:ROF PRIM W:AttackBonus PRIM W:Priority PRIM" +
        " Shield.Next PRIM Shield.Ready! PRIM Fleeing@ PRIM C:State PRIM C:Distance PRIM" +
        " C:Friendly PRIM C:AIBaseclass PRIM C:HasLineOfSight 0 CONSTANT A:T:Unknnown 1" +
        " CONSTANT A:T:SpellCaster 2 CONSTANT A:T:Advance 3 CONSTANT A:T:RangedWeapon 4" +
        " CONSTANT A:T:MeleeWeapon 5 CONSTANT A:T:Judo 6 CONSTANT A:T:SpellLikeAbility 0" +
        " CONSTANT W:T:NoWeapon 1 CONSTANT W:T:HandBlunt 2 CONSTANT W:T:HandCutting 3 CONSTANT" +
        " W:T:HandThrow 4 CONSTANT W:T:SlingNoAmmo 5 CONSTANT W:T:Bow 6 CONSTANT W:T:Crossbow" +
        " 7 CONSTANT W:T:Throw 8 CONSTANT W:T:Ammo 9 CONSTANT W:T:SpellCaster 10 CONSTANT" +
        " W:T:SpellLikeAbility 0 CONSTANT C:S:None 1 CONSTANT C:S:Casting 2 CONSTANT" +
        " C:S:Attacking 3 CONSTANT C:S:Guarding 4 CONSTANT C:S:Bandaging 5 CONSTANT C:S:Using" +
        " 6 CONSTANT C:S:Moving 7 CONSTANT C:S:Turning 8 CONSTANT C:S:Fleeing 9 CONSTANT" +
        " C:S:Fled 10 CONSTANT C:S:ContinueGuarding 11 CONSTANT C:S:Petrified 12 CONSTANT" +
        " C:S:Dying 13 CONSTANT C:S:Unconscious 14 CONSTANT C:S:Dead 15 CONSTANT C:S:Gone";
}
