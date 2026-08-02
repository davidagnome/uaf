using UAF.Serialization;

namespace UAFcore;

/// <summary>What a <c>UTILITIES_EVENT_DATA</c> does to its arithmetic target.</summary>
public enum MathOperation
{
    None = 0,
    StoredIn = 1,
    AddedTo = 2,
    SubtractFrom = 3,
}

/// <summary>How a utilities event tests the party's tokens.</summary>
public enum MultiItemCheck
{
    None = 0,
    AllItems = 1,
    AtLeastOneItem = 2,
}

/// <summary>What a utilities event did.</summary>
/// <param name="Activated">Whether the item check passed, so the result token was awarded.</param>
/// <param name="EndsPlay">True when the event ends the game outright.</param>
public readonly record struct UtilitiesOutcome(bool Activated, bool EndsPlay);

/// <summary>
/// Runs a <c>UTILITIES_EVENT_DATA</c> (<c>UTILITIES_EVENT_DATA::OnIdle</c>,
/// <c>RunEvent.cpp:11382</c>) — arithmetic on the design's counters.
/// </summary>
/// <remarks>
/// <para>
/// The second most common unexecuted type, 280 across the corpus, and it presents nothing at all:
/// <c>OnInitialEvent</c> clears the menu and <c>OnIdle</c> does the work and chains. It is how a
/// design does sums — every special item, key and quest carries a <c>stage</c>, and this reads,
/// writes and compares them.
/// </para>
/// <para>
/// It runs in three parts, in order: an arithmetic step on one token, a check across a list of
/// them, and — if the check passed — an award to a third. Any of the three can be switched off.
/// </para>
/// </remarks>
public static class Utilities
{
    /// <summary><c>QUEST_FLAG</c> (<c>Externs.h:892</c>).</summary>
    public const byte QuestFlag = 0x04;

    /// <summary>
    /// The stage a quest reaches to count as complete (<c>GlobalData.h:213</c>).
    /// </summary>
    /// <remarks>
    /// 0xFDE8, with failure one above it at 0xFDE9. These are sentinels in the same
    /// <c>WORD</c> as ordinary stage numbers, which is why quest arithmetic clamps to the
    /// completed stage rather than to 65535 like items and keys do.
    /// </remarks>
    public const int QuestCompletedStage = 0xFDE8;

    /// <summary><c>QUEST_FAILED_STAGE</c> (<c>GlobalData.h:212</c>).</summary>
    public const int QuestFailedStage = 0xFDE9;

    /// <summary>The ceiling for special-item and key arithmetic — a bare <c>WORD</c>.</summary>
    public const int MaxStage = 65535;

    /// <summary>Applies the event.</summary>
    /// <remarks>
    /// <para>
    /// <b>A negative index switches a step off.</b> The reference guards each of the three parts
    /// with <c>&gt;= 0</c>, which is how the editor stores "no target" — so an event may do
    /// arithmetic and no check, or a check and no arithmetic.
    /// </para>
    /// <para>
    /// <b>An empty item list never activates</b>, under either check. "All of nothing" would be
    /// vacuously true, and the reference explicitly writes <c>activate = FALSE</c> for that case
    /// rather than letting the loop decide.
    /// </para>
    /// </remarks>
    public static UtilitiesOutcome Run(UtilitiesEvent utilities, WorldState world)
    {
        ArgumentNullException.ThrowIfNull(utilities);
        ArgumentNullException.ThrowIfNull(world);

        if (utilities.MathItemIndex >= 0)
        {
            ApplyMath(utilities, world);
        }

        bool activated = Check(utilities, world);

        if (activated && utilities.ResultItemIndex >= 0)
        {
            Award(utilities.ResultItemType, utilities.ResultItemIndex, world);
        }

        return new UtilitiesOutcome(activated, utilities.EndPlay != 0);
    }

    /// <summary>
    /// The arithmetic step.
    /// </summary>
    /// <remarks>
    /// <b>Adding to a quest is not the same operation as adding to an item.</b> Items and keys get
    /// a plain add clamped to <see cref="MaxStage"/>; a quest goes through <c>IncStage</c>, which
    /// clamps to <see cref="QuestCompletedStage"/> instead, <b>refuses to act at all on an already
    /// complete quest</b>, and re-derives the quest's <i>state</i> from the stage it lands on.
    /// The reference's comment says why: "cannot add to a quest and make it fail".
    /// <para>
    /// Subtraction has no such special case — all three stores take the plain clamped path, so
    /// subtracting from a quest <i>can</i> drop it below the sentinels and out of completion
    /// without touching its state. That asymmetry is the reference's.
    /// </para>
    /// </remarks>
    private static void ApplyMath(UtilitiesEvent utilities, WorldState world)
    {
        int index = utilities.MathItemIndex;
        int amount = utilities.MathAmount;

        switch ((MathOperation)utilities.Operation)
        {
            case MathOperation.StoredIn:
                SetStage(utilities.MathItemType, index, amount, world);
                break;

            case MathOperation.AddedTo when utilities.MathItemType == QuestFlag:
                IncQuestStage(index, amount, world);
                break;

            case MathOperation.AddedTo:
                SetStage(utilities.MathItemType, index,
                         Clamp(Stage(utilities.MathItemType, index, world) + amount), world);
                break;

            case MathOperation.SubtractFrom:
                SetStage(utilities.MathItemType, index,
                         Clamp(Stage(utilities.MathItemType, index, world) - amount), world);
                break;
        }
    }

    /// <summary><c>QUEST_LIST::IncStage</c> (<c>GlobalData.cpp:2069</c>).</summary>
    private static void IncQuestStage(int id, int amount, WorldState world)
    {
        int current = world.QuestStageOf(id);
        if (current >= QuestCompletedStage)
        {
            return;                                      // a complete quest is left alone entirely
        }

        int result = Math.Clamp(current + amount, 0, QuestCompletedStage);
        world.SetQuestStage(id, result);

        // The state follows the stage it landed on, which is what makes arithmetic able to
        // complete a quest without the design saying so.
        world.SetQuestState(id, result switch
        {
            QuestCompletedStage => QuestState.Complete,
            QuestFailedStage => QuestState.Failed,        // unreachable: the clamp is below it
            0 => QuestState.NotStarted,
            _ => QuestState.InProgress,
        });
    }

    /// <summary>The item check.</summary>
    /// <remarks>
    /// <b>An entry with a negative index is skipped, not failed.</b> Under
    /// <see cref="MultiItemCheck.AllItems"/> that means a list of nothing but blanks passes — the
    /// list is non-empty, so the empty-list rule does not save it.
    /// <para>
    /// A quest counts as held when its <i>state</i> is anything but not-started
    /// (<c>IsPresent</c>), where an item or key counts on its <i>stage</i> being above zero. Two
    /// different questions, one loop.
    /// </para>
    /// </remarks>
    private static bool Check(UtilitiesEvent utilities, WorldState world)
    {
        var check = (MultiItemCheck)utilities.ItemCheck;
        if (check == MultiItemCheck.None || utilities.Items.Count == 0)
        {
            return false;
        }

        bool all = check == MultiItemCheck.AllItems;

        foreach (var entry in utilities.Items)
        {
            if (entry.Index < 0)
            {
                continue;
            }

            bool held = Held(entry.ItemType, entry.Index, world);

            if (all && !held)
            {
                return false;
            }

            if (!all && held)
            {
                return true;
            }
        }

        return all;
    }

    /// <summary>The award, when the check passed.</summary>
    /// <remarks>
    /// <b>A quest is set to stage 1 rather than incremented.</b> Items and keys get +1 clamped;
    /// the quest branch writes a literal 1, so awarding the same quest twice does not advance it.
    /// </remarks>
    private static void Award(byte type, int index, WorldState world)
    {
        if (type == QuestFlag)
        {
            world.SetQuestStage(index, 1);
            return;
        }

        SetStage(type, index, Clamp(Stage(type, index, world) + 1), world);
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, MaxStage);

    private static int Stage(byte type, int index, WorldState world) => type switch
    {
        SpecialItems.ItemFlag => world.SpecialItemStage(index),
        SpecialItems.KeyFlag => world.KeyStage(index),
        QuestFlag => world.QuestStageOf(index),
        _ => 0,
    };

    private static void SetStage(byte type, int index, int stage, WorldState world)
    {
        switch (type)
        {
            case SpecialItems.ItemFlag:
                world.SetSpecialItemStage(index, stage);
                break;

            case SpecialItems.KeyFlag:
                world.SetKeyStage(index, stage);
                break;

            case QuestFlag:
                world.SetQuestStage(index, stage);
                break;
        }
    }

    private static bool Held(byte type, int index, WorldState world) => type switch
    {
        SpecialItems.ItemFlag => world.HasSpecialItem(index),
        SpecialItems.KeyFlag => world.HasKey(index),
        QuestFlag => world.QuestStateOf(index) != QuestState.NotStarted,
        _ => false,
    };
}
