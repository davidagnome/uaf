using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>How a field is edited, which decides which template the detail pane picks.</summary>
public enum EventFieldKind
{
    /// <summary>A single-line string.</summary>
    Text,

    /// <summary>A string long enough to want its own box — the three display texts, GPDL.</summary>
    Paragraph,

    /// <summary>An integer, of whatever width the record stores it in.</summary>
    Number,

    /// <summary>A <c>BOOL</c> or <c>BYTE</c> used as one.</summary>
    Flag,

    /// <summary>An ordinal drawn from an <see cref="EventCatalog"/> table.</summary>
    Choice,

    /// <summary>Another event's id, with a jump.</summary>
    Chain,

    /// <summary>Something the editor shows but does not edit — a nested list, a picture slot.</summary>
    Info,
}

/// <summary>
/// One editable field of an event: how to read it, how to write it, and how to show it.
/// </summary>
/// <remarks>
/// <para>
/// Every event record is a <c>sealed record</c> with no setters, so "editing" is producing a new
/// record. A spec is therefore a lens — a getter and a <c>with</c>-expression — and the view model
/// layer holds no field state at all. That is what keeps the edited collection trustworthy: what
/// the panel shows is read back out of the record it just built, so a write that silently did
/// nothing is visible immediately rather than at save time.
/// </para>
/// <para>
/// <see cref="Write"/> being null is the marker for read-only, used for ids, the declared type, and
/// the nested lists (monster rosters, item lists, spell books) whose editors are whole dialogs of
/// their own in the original.
/// </para>
/// </remarks>
/// <param name="Read">Renders the current value as the text the control shows.</param>
/// <param name="Write">
/// Applies an edited value, or returns the body unchanged when the text does not parse. Null for a
/// read-only field.
/// </param>
public sealed record EventFieldSpec(
    string Label,
    EventFieldKind Kind,
    Func<IGameEvent, string> Read,
    Func<IGameEvent, string, IGameEvent>? Write = null,
    IReadOnlyList<EventChoice>? Choices = null);

/// <summary>A named block of fields — one nested record, or one section of a dialog.</summary>
public sealed record EventFieldGroup(string Label, IReadOnlyList<EventFieldSpec> Fields);

/// <summary>
/// Builders for <see cref="EventFieldSpec"/>, one per editing kind.
/// </summary>
/// <remarks>
/// <para>
/// The generic parameter is what makes the tables readable: <c>Field.Flag&lt;TextEvent&gt;("…", e =&gt;
/// e.ForceBackup != 0, (e, v) =&gt; e with { ForceBackup = v ? 1 : 0 })</c> is typed end to end, so a
/// renamed record member is a compile error rather than a blank row.
/// </para>
/// <para>
/// <b>Numbers go through <c>long</c> regardless of the record's width.</b> The event records mix
/// <c>int</c>, <c>uint</c>, <c>ushort</c> and <c>byte</c> more or less arbitrarily — the format
/// does, so they must — and giving each width its own builder would quadruple this file to no
/// purpose. The cast back to the stored width is written at the call site, where the width is
/// visible, and <paramref name="min"/>/<paramref name="max"/> stop a typed value overflowing it.
/// </para>
/// </remarks>
public static class Field
{
    /// <summary>A single-line string.</summary>
    public static EventFieldSpec Text<T>(string label, Func<T, string> get, Func<T, string, T> set)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Text,
            body => get((T)body),
            (body, text) => set((T)body, text));

    /// <summary>A string big enough to deserve a multi-line box.</summary>
    public static EventFieldSpec Paragraph<T>(string label, Func<T, string> get,
                                              Func<T, string, T> set)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Paragraph,
            body => get((T)body),
            (body, text) => set((T)body, text));

    /// <summary>An integer of any stored width.</summary>
    public static EventFieldSpec Number<T>(string label, Func<T, long> get, Func<T, long, T> set,
                                           long min = int.MinValue, long max = int.MaxValue)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Number,
            body => get((T)body).ToString(),
            (body, text) => long.TryParse(text, out long value)
                ? set((T)body, Math.Clamp(value, min, max))
                : body);

    /// <summary>A <c>BOOL</c>, or a <c>BYTE</c> used as one.</summary>
    public static EventFieldSpec Flag<T>(string label, Func<T, bool> get, Func<T, bool, T> set)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Flag,
            body => get((T)body) ? "1" : "0",
            (body, text) => set((T)body, text == "1"));

    /// <summary>An ordinal shown through one of <see cref="EventCatalog"/>'s tables.</summary>
    public static EventFieldSpec Choice<T>(string label, IReadOnlyList<EventChoice> choices,
                                           Func<T, long> get, Func<T, long, T> set)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Choice,
            body => get((T)body).ToString(),
            (body, text) => long.TryParse(text, out long value) ? set((T)body, value) : body,
            choices);

    /// <summary>Another event's id.</summary>
    public static EventFieldSpec Chain<T>(string label, Func<T, long> get, Func<T, long, T> set)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Chain,
            body => get((T)body).ToString(),
            (body, text) => long.TryParse(text, out long value) && value >= 0
                ? set((T)body, Math.Min(value, uint.MaxValue))
                : body);

    /// <summary>Something shown and not edited.</summary>
    public static EventFieldSpec Info(string label, Func<IGameEvent, string> read) =>
        new(label, EventFieldKind.Info, read);

    /// <summary>Something shown and not edited, from a known concrete type.</summary>
    public static EventFieldSpec Info<T>(string label, Func<T, string> read)
        where T : class, IGameEvent =>
        new(label, EventFieldKind.Info, body => read((T)body));
}
