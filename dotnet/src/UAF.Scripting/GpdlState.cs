namespace UAF.Scripting;

/// <summary>
/// Transcribed from <c>enum GPDL_STATE</c> (GPDLexec.h:62). One enum serves two purposes in the
/// original — interpreter state and event return code — and the numbering is significant only in
/// that <c>State()</c> asserts the value is one of the first four.
/// </summary>
public enum GpdlState
{
    GPDL_UNINITIALIZED = 1,
    GPDL_IDLE,
    GPDL_WAIT_INPUT,
    GPDL_WAIT_ACK,

    GPDL_OK,
    GPDL_ACCEPTED,
    GPDL_IGNORED,
    GPDL_NOSUCHNAME,
    GPDL_EVENT_ERROR,
    GPDL_READ_ERROR,
    GPDL_OVER_RP,
    GPDL_UNDER_RP,
    GPDL_OVER_SP,
    GPDL_UNDER_SP,
    GPDL_GREPERROR,
    GPDL_ILLPARAM,
    GPDL_BADINTEGER,

    /// <summary>Illegal cell at entry to a function.</summary>
    GPDL_ILLFUNC,
    GPDL_ILLCHARNUM,
    GPDL_EXCESSCPU,
}
