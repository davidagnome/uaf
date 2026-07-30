namespace UAF.Scripting;

/// <summary>
/// Transcribed from <c>enum TOKENTYPE</c>, GPDLcomp.cpp:22. Only the identities matter (the values
/// never reach a file), but the order is kept so the two lists can be diffed by eye.
/// </summary>
public enum GpdlTokenType
{
    TKN_NONE = 0,
    TKN_NAME,
    TKN_PLUS,
    TKN_MINUS,
    TKN_OPENBRACE,
    TKN_CLOSEBRACE,
    TKN_INTEGER,
    TKN_OPENPAREN,
    TKN_CLOSEPAREN,
    TKN_COLON,
    TKN_SEMICOLON,
    TKN_GEAR,
    TKN_SLASH,
    TKN_DOUBLESLASH,
    TKN_STRING,
    TKN_COMMA,
    TKN_EQUAL,
    TKN_POUND,
    TKN_OPENBRACKET,
    TKN_CLOSEBRACKET,
    TKN_LESS,
    TKN_nLESS,
    TKN_GREATER,
    TKN_nPLUS,
    TKN_nMINUS,
    TKN_nGEAR,
    TKN_ISEQUAL,
    TKN_NOTEQUAL,
    TKN_nISEQUAL,
    TKN_nNOTEQUAL,
    TKN_LOR,
    TKN_LAND,
    TKN_nOR,
    TKN_nXOR,
    TKN_nAND,
    TKN_LESSEQUAL,
    TKN_nLESSEQUAL,
    TKN_nGREATER,
    TKN_GREATEREQUAL,
    TKN_nGREATEREQUAL,
    TKN_nSLASH,
    TKN_nPERCENT,
    TKN_PERCENT,
    TKN_NOT,

    /// <summary><c>#PUBLIC</c> and friends.</summary>
    TKN_PRAGMA,

    /// <summary><c>=#</c> — assign, forcing the value numeric.</summary>
    TKN_nEQUAL,
}
