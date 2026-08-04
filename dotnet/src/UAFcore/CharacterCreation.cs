using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The steps a new character is made in (<c>CREATE_CHARACTER_DATA::OnIdle</c>,
/// <c>RunEvent.cpp:2824</c>).
/// </summary>
/// <remarks>
/// <b>The order is a dependency chain, not a preference.</b> Race narrows the classes on offer,
/// and so does gender — <c>ALLOWED_CLASSES::Restrictions</c> takes both — so class cannot come
/// first. Alignment is where the character is actually rolled. Nothing here is reorderable.
/// </remarks>
public enum CreationStep
{
    Race = 0,
    Gender,
    Class,
    Alignment,
    Stats,
    Name,
    Icon,
    SmallPicture,
    Spells,
    AskToSave,

    /// <summary>Nothing left to ask.</summary>
    Done,
}

/// <summary>One thing a player can pick at the current step.</summary>
public sealed record CreationChoice(string Id, string Name);

/// <summary>
/// A character being made: which step is showing, and what has been chosen so far.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first wizard in the port.</b> Every screen before this one answered a question and
/// finished; this pushes ten in sequence, each writing into one shared character. The reference
/// drives it from <c>OnIdle</c> with a task state, which is why the steps are an enum here rather
/// than a call chain — a screen has to be able to say "I am done" without knowing what follows.
/// </para>
/// <para>
/// <b>There is no going back.</b> Every picker's EXIT sets <c>m_AbortCharCreation</c> and unwinds
/// the whole thing (<c>RunEvent.cpp:3253</c>); none of them steps back one. A player who picks the
/// wrong race starts again.
/// </para>
/// </remarks>
public sealed class CharacterCreation
{
    /// <summary>Which question is being asked.</summary>
    public CreationStep Step { get; private set; } = CreationStep.Race;

    /// <summary>Whether the player backed out of the whole thing.</summary>
    public bool Aborted { get; private set; }

    public string? RaceId { get; private set; }

    public Gender Gender { get; private set; }

    public string? ClassId { get; private set; }

    public int Alignment { get; private set; }

    /// <summary>What the character is called, once the name step has been through.</summary>
    public string? CharacterName { get; private set; }

    /// <summary>Takes the typed name and moves on (<c>GETCHARNAME_MENU_DATA</c>).</summary>
    /// <remarks>
    /// <b>An empty name is refused, not accepted.</b> The reference returns without popping —
    /// "Need at least one character" — so Return on a blank line does nothing at all.
    /// </remarks>
    public void Name(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (Step is not CreationStep.Name)
        {
            return;
        }

        CharacterName = name;
        Step = CreationStep.Icon;
    }

    /// <summary>Records a choice and moves on.</summary>
    public void Choose(string id)
    {
        switch (Step)
        {
            case CreationStep.Race:
                RaceId = id;
                break;

            case CreationStep.Gender:
                Gender = string.Equals(id, nameof(Gender.Female), StringComparison.OrdinalIgnoreCase)
                    ? Gender.Female
                    : Gender.Male;
                break;

            case CreationStep.Class:
                ClassId = id;
                break;

            case CreationStep.Alignment:
                Alignment = int.TryParse(id, out int value) ? value : 0;
                break;

            default:
                return;
        }

        Step = Step is CreationStep.Done ? CreationStep.Done : Step + 1;
    }

    /// <summary>
    /// Accepts the roll and moves past the re-roll screen.
    /// </summary>
    /// <remarks>
    /// <b>The stats step does not make the stats.</b> <c>ALIGNMENT_MENU_DATA</c> calls
    /// <c>generateNewCharacter</c> the moment the alignment is picked, so by the time
    /// <c>CHOOSESTATS_MENU_DATA</c> appears the character exists — its item 2 is literally "don't
    /// re-roll". Skipping it is therefore "keep the first roll", not "skip generating a
    /// character".
    /// </remarks>
    public void SkipStats()
    {
        if (Step is CreationStep.Stats)
        {
            Step = CreationStep.Name;
        }
    }

    /// <summary>The portrait chosen, or null before the small-picture step.</summary>
    public string? SmallPicture { get; private set; }

    /// <summary>The combat icon chosen, or null before the icon step.</summary>
    public string? Icon { get; private set; }

    /// <summary>
    /// Takes a picture and moves on. A design with no art still advances.
    /// </summary>
    /// <remarks>
    /// <b>SELECT is always available, even with nothing to select.</b> The reference darkens only
    /// NEXT and PREV, so a design shipping no portraits leaves the player pressing SELECT over an
    /// empty screen — and the character is made without one.
    /// </remarks>
    public void Pick(string? art)
    {
        switch (Step)
        {
            case CreationStep.Icon:
                Icon = art;
                Step = CreationStep.SmallPicture;
                break;

            case CreationStep.SmallPicture:
                SmallPicture = art;
                Step = CreationStep.Spells;
                break;

            default:
                break;
        }
    }

    /// <summary>Moves past the spell screens to the save prompt.</summary>
    public void LearnedSpells()
    {
        if (Step is CreationStep.Spells)
        {
            Step = CreationStep.AskToSave;
        }
    }

    /// <summary>Backs out of character creation entirely.</summary>
    public void Abort()
    {
        Aborted = true;
        Step = CreationStep.Done;
    }
}

/// <summary>
/// What a player may pick at each step, out of the design's own tables.
/// </summary>
public static class CreationChoices
{
    /// <summary>The attribute a race lists its permitted classes in (<c>Externs.h:525</c>).</summary>
    public const string AllowedClassAttribute = "AllowedClass";

    /// <summary>The two genders the character generator offers.</summary>
    /// <remarks>
    /// <c>ALLOWED_CLASSES::Restrictions</c> passes <c>"M"</c> or <c>"F"</c> to the baseclass hook,
    /// so the generator's notion of gender is these two and nothing else — whatever the
    /// <c>genderType</c> enum holds elsewhere.
    /// </remarks>
    public static readonly CreationChoice[] Genders =
        [new(nameof(Gender.Male), "MALE"), new(nameof(Gender.Female), "FEMALE")];

    /// <summary>The nine alignments, in the engine's own order.</summary>
    public static readonly CreationChoice[] Alignments =
    [
        new("0", "LAWFUL GOOD"), new("1", "LAWFUL NEUTRAL"), new("2", "LAWFUL EVIL"),
        new("3", "NEUTRAL GOOD"), new("4", "TRUE NEUTRAL"), new("5", "NEUTRAL EVIL"),
        new("6", "CHAOTIC GOOD"), new("7", "CHAOTIC NEUTRAL"), new("8", "CHAOTIC EVIL"),
    ];

    /// <summary>Every race the design defines, by name.</summary>
    public static List<CreationChoice> Races(IReadOnlyDictionary<string, RaceRecord>? races) =>
        races is null
            ? []
            : [.. races.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                       .Select(r => new CreationChoice(r.Key, r.Value.Name))];

    /// <summary>
    /// The classes a race may take (<c>CLASS_DATA::IsRaceAllowed</c>, <c>class.cpp:7913</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two gates, and the first one is usually wide open.</b> <c>IsAllowedClass</c> returns
    /// true outright when the race carries no <c>AllowedClass</c> attribute — or one that is not a
    /// legal delimited string — and that is the <i>first</i> test in <c>IsRaceAllowed</c>. Most
    /// designs write no such attribute, so for most races the second gate never runs at all and
    /// every class is on offer, multi-classes included.
    /// </para>
    /// <para>
    /// <b>An <i>empty</i> list is the opposite of an absent one.</b> Empty is legal and contains
    /// nothing, so every class falls through to the second gate — which is the only way the
    /// baseclass table ever gets a say. Absent and empty are opposite answers from adjacent lines,
    /// and it is the empty case that turns the filter on.
    /// </para>
    /// <para>
    /// <b>The second gate never admits a multi-class.</b> The comment beside the reference says
    /// it: allowed "if we have a single Base Class and the Base Class allows this race, <i>or</i>
    /// the race explicitly allows this class". So once a race writes a list, a multi-class is
    /// offered only if that list names it — both of its baseclasses permitting the race counts for
    /// nothing. And naming one class does <i>not</i> hide the others: they still pass on their own
    /// baseclasses, so a designer writing the list to mean "only these" would be surprised.
    /// </para>
    /// <para>
    /// <b>Not ported: the <c>IS_BASECLASS_ALLOWED</c> hook.</b> The reference also runs a script
    /// per baseclass with the race and gender as parameters, and a reply beginning <c>N</c>
    /// removes the class. An empty reply means no opinion, so a design that does not write the
    /// hook behaves identically here — and one that does will offer classes it should not.
    /// </para>
    /// </remarks>
    public static List<CreationChoice> ClassesFor(
        string? raceId,
        IReadOnlyDictionary<string, ClassRecord>? classes,
        IReadOnlyDictionary<string, RaceRecord>? races,
        IReadOnlyDictionary<string, BaseclassRecord>? baseclasses)
    {
        if (classes is null || raceId is null)
        {
            return [];
        }

        string? allowed = races is not null && races.TryGetValue(raceId, out var race)
            ? race.Attributes.FirstOrDefault(a => a.Key == AllowedClassAttribute)?.Value
            : null;

        var offered = new List<CreationChoice>();

        foreach (var (id, record) in classes.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (IsRaceAllowed(record, raceId, allowed, baseclasses))
            {
                offered.Add(new CreationChoice(id, record.Name));
            }
        }

        return offered;
    }

    private static bool IsRaceAllowed(ClassRecord record, string raceId, string? allowed,
                                      IReadOnlyDictionary<string, BaseclassRecord>? baseclasses)
    {
        // The race's own list wins outright, and a missing or malformed one is no restriction.
        if (allowed is null || !DelimitedString.IsLegal(allowed))
        {
            return true;
        }

        if (DelimitedString.Contains(allowed, record.Name))
        {
            return true;
        }

        // Otherwise: exactly one baseclass, and it must permit the race.
        if (record.Baseclasses.Count != 1 || baseclasses is null)
        {
            return false;
        }

        return baseclasses.TryGetValue(record.Baseclasses[0], out var baseclass)
               && baseclass.AllowedRaces.Contains(raceId, StringComparer.Ordinal);
    }
}
