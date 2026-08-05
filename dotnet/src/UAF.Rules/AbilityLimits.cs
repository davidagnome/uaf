namespace UAF.Rules;

/// <summary>
/// The range one ability score may take, with the modifier limits that go with it
/// (<c>ABILITYLIMITS</c>, <c>Externs.h:627</c>).
/// </summary>
/// <param name="Min">The lowest base score.</param>
/// <param name="MinMod">The lowest modifier — the exceptional-strength percentile, in practice.</param>
/// <param name="Max">The highest base score.</param>
/// <param name="MaxMod">The highest modifier.</param>
public readonly record struct AbilityLimits(int Min, int MinMod, int Max, int MaxMod)
{
    /// <summary>
    /// What a baseclass with no requirement for this ability contributes
    /// (<c>class.cpp:6608</c>), and what an unknown baseclass does too (<c>class.cpp:7381</c>).
    /// </summary>
    public static AbilityLimits Default => new(3, 0, 18, 0);

    /// <summary>
    /// Packs four values the way <c>ASSEMBLEABILITYLIMITS</c> does — one byte each.
    /// </summary>
    /// <remarks>
    /// <b>This is not a storage detail, it is arithmetic that changes answers.</b> Each field is
    /// masked to <c>0xff</c>, so a limit above 255 wraps. See <see cref="Unbounded"/>.
    /// </remarks>
    public static AbilityLimits Pack(int min, int minMod, int max, int maxMod) =>
        new(min & 0xff, minMod & 0xff, max & 0xff, maxMod & 0xff);

    /// <summary>
    /// What a class with <b>no</b> baseclasses at all comes out at.
    /// </summary>
    /// <remarks>
    /// <b>Its maximum is 15, not 9999.</b> <c>CLASS_DATA::GetAbilityLimits</c>
    /// (<c>class.cpp:7698</c>) starts its running maximum at 9999 as a sentinel and, with no
    /// baseclass to lower it, packs that straight into a byte: <c>9999 &amp; 0xff</c> is 15. So a
    /// class the design left empty caps every score below the value a normal roll produces.
    /// </remarks>
    public static AbilityLimits Unbounded => Pack(0, 0, 9999, 9999);

    /// <summary>
    /// What an unknown class comes out at: <c>GetAbilityLimits</c> returns the literal <c>1</c>
    /// (<c>class.cpp:8505</c>), which unpacks as a maximum of zero.
    /// </summary>
    /// <remarks>
    /// <b>A score can then only go down.</b> The increment refuses at or above the maximum, and
    /// every score is above zero.
    /// </remarks>
    public static AbilityLimits UnknownClass => new(0, 0, 0, 1);

    /// <summary>
    /// Combines a class's baseclasses into the range that satisfies all of them
    /// (<c>CLASS_DATA::GetAbilityLimits</c>, <c>class.cpp:7698</c>).
    /// </summary>
    /// <param name="requirementsPerBaseclass">
    /// One entry per baseclass the class lists, in order: its limits for the ability in question,
    /// or null for a baseclass the design does not have — which contributes
    /// <see cref="Default"/> rather than being skipped.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The tightest of each end wins</b> — the greatest minimum and the least maximum — so a
    /// dual-classed character must satisfy both halves. A tie on the base value is broken by
    /// taking the <i>greater</i> modifier, on both ends: <c>rminmod &gt; minmod</c> and
    /// <c>rmaxmod &gt; maxmod</c>. That is symmetric where the base values are not, and it is what
    /// the reference does.
    /// </para>
    /// <para>
    /// <b>An empty list is not an error.</b> The loop simply never runs and the sentinels stand,
    /// which is <see cref="Unbounded"/> and its truncated maximum.
    /// </para>
    /// </remarks>
    public static AbilityLimits Combine(IEnumerable<AbilityLimits?> requirementsPerBaseclass)
    {
        ArgumentNullException.ThrowIfNull(requirementsPerBaseclass);

        int min = 0, minMod = 0, max = 9999, maxMod = 9999;

        foreach (var requirement in requirementsPerBaseclass)
        {
            var limits = requirement ?? Default;

            if (limits.Min > min)
            {
                min = limits.Min;
                minMod = limits.MinMod;
            }
            else if (limits.Min == min && limits.MinMod > minMod)
            {
                minMod = limits.MinMod;
            }

            if (limits.Max < max)
            {
                max = limits.Max;
                maxMod = limits.MaxMod;
            }
            else if (limits.Max == max && limits.MaxMod > maxMod)
            {
                maxMod = limits.MaxMod;
            }
        }

        return Pack(min, minMod, max, maxMod);
    }
}
