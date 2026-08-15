using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;

namespace UAFedit.Spells;

/// <summary>One of a spell's four stage sounds — a plain file name in the record.</summary>
public sealed partial class SpellSoundViewModel : EditableViewModel
{
    public SpellSoundViewModel(string label, string fileName)
    {
        Label = label;
        FileName = fileName ?? string.Empty;
        ResetDirty();
    }

    public string Label { get; }

    [ObservableProperty]
    private string fileName = string.Empty;
}

/// <summary>
/// One of a spell's five art slots, shown but not edited.
/// </summary>
/// <remarks>
/// A <c>PIC_DATA</c> is twelve fields, and choosing one in the reference goes through
/// <c>CSmallPicDlg</c>, which browses the design's art and force-stamps <c>picType</c> to
/// <c>SpriteDib</c> on accept (<c>SpellDBDlgEx.cpp:1166</c>). A text box over the file name alone
/// would let a designer name a file whose frame count and size no longer match — worse than
/// read-only.
/// </remarks>
public sealed class SpellArtViewModel(string label, PicRecord? pic)
{
    public string Label { get; } = label;

    public string FileName => pic?.FileName ?? string.Empty;

    public bool IsEmpty => FileName.Length == 0;

    /// <summary>The animation parameters, or empty when the slot holds nothing.</summary>
    public string Detail => pic is null || IsEmpty
        ? string.Empty
        : $"{pic.NumFrames} frames, {pic.FrameWidth}x{pic.FrameHeight}";
}

/// <summary>One baseclass, and whether this spell may be cast by it.</summary>
public sealed partial class BaseclassChoiceViewModel(string name, bool isAllowed)
    : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool isAllowed = isAllowed;
}

/// <summary>
/// The editable detail form for one spell — <c>CSpellDBDlgEx</c>
/// (<c>UAFWinEd/SpellDBDlgEx.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One of these exists for every spell in the design, not one for the selection.</b> That is
/// what lets the master list show a spell's edited name as it is typed, and it is what makes
/// <see cref="SpellDatabaseViewModel.EditedSpells"/> a straight projection rather than a merge of
/// edits over originals.
/// </para>
/// <para>
/// <b>Fields absent from the record are absent here.</b> <c>SPELL_DATA</c> carries about thirty-five
/// retired scalars (<c>Target_</c>, <c>Damage_</c>, <c>Protection_</c> and friends) that only exist
/// at or below version 0.6992, where <c>DICEPLUS</c> expressions replaced them;
/// <see cref="SpellRecordReader"/> reads past them without keeping them, so there is nothing to
/// edit and no dialog for them.
/// </para>
/// </remarks>
public sealed partial class SpellEditorViewModel : EditableViewModel
{
    /// <summary><c>Permanent</c> — the duration rate that makes the duration meaningless.</summary>
    private const int PermanentDuration = 4;

    private readonly SpellRecord original;
    private readonly SpellScriptViewModel[] scriptsBySlot;

    public SpellEditorViewModel(SpellRecord spell)
        : this(spell, [])
    {
    }

    /// <param name="designBaseclasses">
    /// Every baseclass the design defines, so the caster list can offer ones this spell does not
    /// yet allow. The spell's own names come first regardless — see <see cref="Baseclasses"/>.
    /// </param>
    public SpellEditorViewModel(SpellRecord spell, IReadOnlyList<string> designBaseclasses)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(designBaseclasses);

        original = spell;

        Name = spell.Name;
        SchoolId = spell.SchoolId;
        CastMessage = spell.CastMessage;
        CastSound = spell.CastSound;

        Level = spell.Level;
        CastingTime = spell.CastingTime;
        CastingTimeType = spell.CastingTimeType;
        CastCost = spell.CastCost;
        CastPriority = spell.CastPriority;

        Targeting = spell.Targeting;
        SaveVersus = spell.SaveVersus;
        SaveResult = spell.SaveResult;
        DurationRate = spell.DurationRate;

        CanTargetFriend = spell.CanTargetFriend != 0;
        CanTargetEnemy = spell.CanTargetEnemy != 0;
        IsCumulative = spell.IsCumulative != 0;
        CanBeDispelled = spell.CanBeDispelled != 0;
        CanMemorize = spell.CanMemorize != 0;
        AllowScribe = spell.AllowScribe != 0;
        AutoScribe = spell.AutoScribe != 0;
        Lingers = spell.Lingers != 0;
        LingerOnceOnly = spell.LingerOnceOnly != 0;
        AllowedInCamp = (spell.Restrictions & SpellChoices.RestrictionInCamp) != 0;
        AllowedInCombat = (spell.Restrictions & SpellChoices.RestrictionInCombat) != 0;

        // The spell's own casters first and in their own order, so an untouched spell rebuilds its
        // list byte-identically -- the order the names sit in is the order the reader produced.
        foreach (string name in spell.AllowedBaseclasses)
        {
            Baseclasses.Add(new BaseclassChoiceViewModel(name, isAllowed: true));
        }

        foreach (string name in designBaseclasses.Except(spell.AllowedBaseclasses,
                                                         StringComparer.OrdinalIgnoreCase))
        {
            Baseclasses.Add(new BaseclassChoiceViewModel(name, isAllowed: false));
        }

        var labels = SpellChoices.ParameterLabels(Targeting);
        for (int i = 0; i < spell.Parameters.Count; i++)
        {
            Parameters.Add(new SpellDiceViewModel(
                i < labels.Count ? labels[i] : $"P{i}", spell.Parameters[i]));
        }

        EffectDuration = spell.EffectDuration is { } duration
            ? new SpellDiceViewModel("Spell's duration on target (in rounds)", duration)
            : null;

        scriptsBySlot = new SpellScriptViewModel[SpellRecordReader.SpellScriptCount];
        for (int i = 0; i < scriptsBySlot.Length && i < spell.Scripts.Count; i++)
        {
            scriptsBySlot[i] = new SpellScriptViewModel((SpellScriptSlot)i, spell.Scripts[i]);
        }

        foreach (var slot in SpellChoices.ScriptOrder)
        {
            if (scriptsBySlot[(int)slot] is { } script)
            {
                Scripts.Add(script);
            }
        }

        for (int i = 0; i < SpellChoices.Stages.Count; i++)
        {
            Sounds.Add(new SpellSoundViewModel(
                SpellChoices.Stages[i],
                i < spell.Sounds.Count ? spell.Sounds[i] : string.Empty));
        }

        Art.Add(new SpellArtViewModel("Cast", spell.CastArt));
        for (int i = 0; i < SpellChoices.Stages.Count; i++)
        {
            Art.Add(new SpellArtViewModel(
                SpellChoices.Stages[i], i < spell.Art.Count ? spell.Art[i] : null));
        }

        foreach (var effect in spell.Effects)
        {
            Effects.Add(new SpellEffectViewModel(effect));
        }

        ApplyParameterLabels();
        Watch();
        ResetDirty();
    }

    [ObservableProperty]
    private string name = string.Empty;

    /// <summary>
    /// The spell's school.
    /// </summary>
    /// <remarks>
    /// <b>A <c>SCHOOL_ID</c> is a <c>CString</c> (<c>Externs.h:1350</c>)</b>, despite reading like a
    /// code, and nothing in the design enumerates the schools. The reference builds its combo by
    /// collecting the distinct values across the loaded database (<c>SpellDBDlgEx.cpp:902</c>),
    /// which is what <see cref="SpellDatabaseViewModel.Schools"/> does — so this stays free text
    /// with suggestions rather than becoming a closed list.
    /// </remarks>
    [ObservableProperty]
    private string schoolId = string.Empty;

    [ObservableProperty]
    private string castMessage = string.Empty;

    [ObservableProperty]
    private string castSound = string.Empty;

    [ObservableProperty]
    private int level;

    [ObservableProperty]
    private int castingTime;

    [ObservableProperty]
    private int castingTimeType;

    [ObservableProperty]
    private int castCost;

    [ObservableProperty]
    private int castPriority;

    /// <summary>
    /// How the spell picks its targets, which also decides what five of its dice fields mean.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetingLabel))]
    private int targeting;

    [ObservableProperty]
    private int saveVersus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveResultLabel))]
    [NotifyPropertyChangedFor(nameof(IsSaveVersusUsed))]
    private int saveResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLingerAvailable))]
    private int durationRate;

    [ObservableProperty]
    private bool canTargetFriend;

    [ObservableProperty]
    private bool canTargetEnemy;

    [ObservableProperty]
    private bool isCumulative;

    [ObservableProperty]
    private bool canBeDispelled;

    [ObservableProperty]
    private bool canMemorize;

    [ObservableProperty]
    private bool allowScribe;

    [ObservableProperty]
    private bool autoScribe;

    /// <summary>Whether the spell leaves a lingering effect on the map.</summary>
    [ObservableProperty]
    private bool lingers;

    /// <summary>Affects a target once, rather than once per round.</summary>
    [ObservableProperty]
    private bool lingerOnceOnly;

    /// <summary>The <c>IN_CAMP</c> bit of <c>Restrictions</c>.</summary>
    [ObservableProperty]
    private bool allowedInCamp;

    /// <summary>The <c>IN_COMBAT</c> bit of <c>Restrictions</c>.</summary>
    [ObservableProperty]
    private bool allowedInCombat;

    /// <summary>Which baseclasses may cast it. Ticked ones become <c>AllowedBaseclasses</c>.</summary>
    public ObservableCollection<BaseclassChoiceViewModel> Baseclasses { get; } = [];

    /// <summary>Duration and the five targeting parameters, in wire order.</summary>
    public ObservableCollection<SpellDiceViewModel> Parameters { get; } = [];

    /// <summary>How long the effect sits on the target, or null for a design too old to have it.</summary>
    public SpellDiceViewModel? EffectDuration { get; }

    /// <summary>The seven scripts, in the order the original's dropdown listed them.</summary>
    public ObservableCollection<SpellScriptViewModel> Scripts { get; } = [];

    [ObservableProperty]
    private SpellScriptViewModel? selectedScript;

    /// <summary>The four stage sounds. <see cref="CastSound"/> is a separate field.</summary>
    public ObservableCollection<SpellSoundViewModel> Sounds { get; } = [];

    /// <summary>Cast art and the four stage animations, read only.</summary>
    public ObservableCollection<SpellArtViewModel> Art { get; } = [];

    /// <summary>The attributes this spell changes, read only.</summary>
    public ObservableCollection<SpellEffectViewModel> Effects { get; } = [];

    /// <summary>The special abilities the spell carries, by name, read only.</summary>
    /// <remarks>
    /// These are names into <c>specialAbilities.txt</c> — the join
    /// <see cref="SpecialAbilityDatabaseViewModel"/> edits the other end of. Only the modern
    /// <c>Pairs</c> form is listed; a design at or below 0.920 stores legacy slots instead, and
    /// those are converted on write rather than shown.
    /// </remarks>
    public IReadOnlyList<string> SpecialAbilities =>
        [.. original.SpecialAbilities.Pairs.Select(p => p.Key)];

    /// <summary>The record's own ASL attributes, read only.</summary>
    public IReadOnlyList<AslEntry> Attributes => original.Attributes;

    /// <summary>
    /// The record's pre-name key, read only.
    /// </summary>
    /// <remarks>
    /// A serialization artefact, not content: it is written only below <c>VersionSpellNames</c> or
    /// at or above <c>VersionSaveIDs</c> (<see cref="SpellRecordReader"/>), and it is <c>-1</c> for
    /// everything in between. Shown because a designer chasing a save-file problem wants to see it,
    /// not because it is editable.
    /// </remarks>
    public int PreSpellNameKey => original.PreSpellNameKey;

    public string TargetingLabel => SpellChoices.Label(SpellChoices.Targeting, Targeting);

    public string SaveResultLabel => SpellChoices.Label(SpellChoices.SaveResult, SaveResult);

    /// <summary>
    /// False when the save result makes the save-versus category meaningless.
    /// </summary>
    /// <remarks>
    /// The reference greys out both the combo and its label for <c>No Save</c> and
    /// <c>Use Player THAC0</c> (<c>SpellDBDlgEx.cpp:199</c>) — with no save being rolled, or the
    /// caster's THAC0 standing in for one, there is no category to roll against.
    /// </remarks>
    public bool IsSaveVersusUsed => SaveResult is not (0 or 3);

    /// <summary>
    /// Whether lingering is offered at all.
    /// </summary>
    /// <remarks>
    /// Only for durations measured in rounds, hours or days (<c>SpellDBDlgEx.cpp:363</c>): a
    /// lingering effect is placed on the map for a span of time, and "by damage taken", "by nbr
    /// attacks" and "permanent" are not spans.
    /// </remarks>
    public bool IsLingerAvailable => DurationRate is 0 or 2 or 3;

    /// <summary>Every script slot that holds something.</summary>
    public IReadOnlyList<SpellScriptViewModel> NonEmptyScripts =>
        [.. Scripts.Where(s => !s.IsEmpty)];

    /// <summary>The columns the master list shows, in order.</summary>
    public string BaseclassSummary =>
        string.Join(", ", Baseclasses.Where(b => b.IsAllowed).Select(b => b.Name));

    partial void OnTargetingChanged(int value)
    {
        _ = value;
        ApplyParameterLabels();
    }

    partial void OnDurationRateChanged(int value)
    {
        _ = value;
        ApplyParameterLabels();
    }

    /// <remarks>
    /// <b>Turning on lingering clears "in camp", as the reference does on OK</b>
    /// (<c>SpellDBDlgEx.cpp:760</c>). A lingering effect is a thing placed on a map square, and
    /// there is no map in camp — the original resolves the contradiction silently and in this
    /// direction, so leaving both set would produce a spell the editor could never have written.
    /// </remarks>
    partial void OnLingersChanged(bool value)
    {
        if (value)
        {
            AllowedInCamp = false;
        }
    }

    /// <summary>Compiles every non-empty script, answering how many failed.</summary>
    public int CompileScripts()
    {
        int failed = 0;

        foreach (var script in Scripts.Where(s => !s.IsEmpty))
        {
            script.Compile();
            if (!script.Diagnostics.Succeeded)
            {
                failed++;
            }
        }

        return failed;
    }

    [RelayCommand]
    private void CompileAllScripts() => CompileScripts();

    /// <summary>The spell as edited.</summary>
    /// <remarks>
    /// <b>Every unedited field is carried through by identity, not rebuilt.</b> The <c>with</c>
    /// expression keeps the art, the effects, the special abilities and the ASL exactly as they were
    /// read — this editor does not model them, and reconstructing what it does not model is how a
    /// round trip loses data.
    /// </remarks>
    public SpellRecord ToRecord() => original with
    {
        Name = Name,
        SchoolId = SchoolId,
        CastMessage = CastMessage,
        CastSound = CastSound,
        AllowedBaseclasses = [.. Baseclasses.Where(b => b.IsAllowed).Select(b => b.Name)],
        Level = Level,
        CastingTime = CastingTime,
        CastingTimeType = CastingTimeType,
        CastCost = CastCost,
        CastPriority = CastPriority,
        Targeting = Targeting,
        SaveVersus = SaveVersus,
        SaveResult = SaveResult,
        DurationRate = DurationRate,
        CanTargetFriend = Flag(CanTargetFriend, original.CanTargetFriend),
        CanTargetEnemy = Flag(CanTargetEnemy, original.CanTargetEnemy),
        IsCumulative = Flag(IsCumulative, original.IsCumulative),
        CanBeDispelled = Flag(CanBeDispelled, original.CanBeDispelled),
        CanMemorize = Flag(CanMemorize, original.CanMemorize),
        AllowScribe = Flag(AllowScribe, original.AllowScribe),
        AutoScribe = Flag(AutoScribe, original.AutoScribe),
        Lingers = Flag(Lingers, original.Lingers),
        LingerOnceOnly = Flag(LingerOnceOnly, original.LingerOnceOnly),
        Restrictions = Restrictions(),
        Parameters = [.. Parameters.Select(p => p.ToDice())],
        Sounds = [.. Sounds.Select(s => s.FileName)],
        Scripts = [.. scriptsBySlot.Select(s => s?.ToScript() ?? SpellScript.Empty)],
        EffectDuration = EffectDuration?.ToDice(),
    };

    /// <summary>Throws away every edit.</summary>
    public void Revert()
    {
        Name = original.Name;
        SchoolId = original.SchoolId;
        CastMessage = original.CastMessage;
        CastSound = original.CastSound;
        Level = original.Level;
        CastingTime = original.CastingTime;
        CastingTimeType = original.CastingTimeType;
        CastCost = original.CastCost;
        CastPriority = original.CastPriority;
        Targeting = original.Targeting;
        SaveVersus = original.SaveVersus;
        SaveResult = original.SaveResult;
        DurationRate = original.DurationRate;
        CanTargetFriend = original.CanTargetFriend != 0;
        CanTargetEnemy = original.CanTargetEnemy != 0;
        IsCumulative = original.IsCumulative != 0;
        CanBeDispelled = original.CanBeDispelled != 0;
        CanMemorize = original.CanMemorize != 0;
        AllowScribe = original.AllowScribe != 0;
        AutoScribe = original.AutoScribe != 0;

        // Before Lingers, so its side effect on "in camp" cannot outlive the revert.
        Lingers = original.Lingers != 0;
        LingerOnceOnly = original.LingerOnceOnly != 0;
        AllowedInCamp = (original.Restrictions & SpellChoices.RestrictionInCamp) != 0;
        AllowedInCombat = (original.Restrictions & SpellChoices.RestrictionInCombat) != 0;

        foreach (var choice in Baseclasses)
        {
            choice.IsAllowed = original.AllowedBaseclasses.Contains(
                choice.Name, StringComparer.Ordinal);
        }

        for (int i = 0; i < Sounds.Count; i++)
        {
            Sounds[i].FileName = i < original.Sounds.Count ? original.Sounds[i] : string.Empty;
        }

        foreach (var dice in Parameters)
        {
            dice.Revert();
        }

        EffectDuration?.Revert();

        foreach (var script in Scripts)
        {
            script.Revert();
        }

        ResetDirty();
    }

    /// <remarks>
    /// Selection, derived labels and the enable/disable flags all follow edits rather than being
    /// edits; marking the spell dirty for them would make merely opening it look like a change.
    /// </remarks>
    protected override bool IsEdit(string? propertyName) =>
        propertyName is not (nameof(SelectedScript) or nameof(TargetingLabel)
                             or nameof(SaveResultLabel) or nameof(IsSaveVersusUsed)
                             or nameof(IsLingerAvailable) or nameof(NonEmptyScripts)
                             or nameof(BaseclassSummary));

    /// <summary>
    /// Puts a checkbox back into the record's int, keeping the stored value when it did not change.
    /// </summary>
    /// <remarks>
    /// <b>These are <c>int</c>s on the wire and <c>BOOL</c>s in the reference</b>, so any non-zero
    /// value means true — and a design carrying, say, 2 would be normalised to 1 by a naive round
    /// trip through <c>bool</c>. Returning the original when the tick did not move keeps a spell
    /// nobody touched byte-identical, which is the difference between a diff of one field and a
    /// diff of the whole database.
    /// </remarks>
    private static int Flag(bool now, int before) =>
        now == (before != 0) ? before : now ? 1 : 0;

    /// <remarks>
    /// Only the two documented bits are rewritten. Anything else a design has set in
    /// <c>restrictions</c> is preserved rather than cleared: the field is a <c>BOOL</c> used as a
    /// bitmask (<c>Shared/Spell.h:487</c>) and nothing guarantees those are the only bits ever
    /// stored.
    /// </remarks>
    private int Restrictions()
    {
        int bits = original.Restrictions
                   & ~(SpellChoices.RestrictionInCamp | SpellChoices.RestrictionInCombat);

        if (AllowedInCamp)
        {
            bits |= SpellChoices.RestrictionInCamp;
        }

        if (AllowedInCombat)
        {
            bits |= SpellChoices.RestrictionInCombat;
        }

        return bits;
    }

    private void ApplyParameterLabels()
    {
        var labels = SpellChoices.ParameterLabels(Targeting);

        for (int i = 0; i < Parameters.Count; i++)
        {
            string label = i < labels.Count ? labels[i] : $"P{i}";
            Parameters[i].Label = label;

            // Index 0 is Duration: used unless the rate makes it meaningless. The rest are used
            // exactly when the targeting type gave them a name.
            Parameters[i].IsUsed = i == 0
                ? DurationRate != PermanentDuration
                : label.Length > 0;
        }
    }

    /// <remarks>
    /// The parts are their own <see cref="EditableViewModel"/>s and nothing else joins their
    /// dirtiness to the spell's. Without this, editing a script or a dice expression would leave
    /// the spell — and so the whole database — looking untouched.
    /// </remarks>
    private void Watch()
    {
        foreach (var part in Parts())
        {
            part.PropertyChanged += OnPartChanged;
        }

        foreach (var choice in Baseclasses)
        {
            choice.PropertyChanged += OnBaseclassChanged;
        }
    }

    private IEnumerable<EditableViewModel> Parts()
    {
        foreach (var dice in Parameters)
        {
            yield return dice;
        }

        if (EffectDuration is not null)
        {
            yield return EffectDuration;
        }

        foreach (var script in Scripts)
        {
            yield return script;
        }

        foreach (var sound in Sounds)
        {
            yield return sound;
        }
    }

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsDirty) && sender is EditableViewModel { IsDirty: true })
        {
            IsDirty = true;
        }

        if (e.PropertyName == nameof(SpellScriptViewModel.Source))
        {
            OnPropertyChanged(nameof(NonEmptyScripts));
        }
    }

    private void OnBaseclassChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BaseclassChoiceViewModel.IsAllowed))
        {
            IsDirty = true;
            OnPropertyChanged(nameof(BaseclassSummary));
        }
    }
}
