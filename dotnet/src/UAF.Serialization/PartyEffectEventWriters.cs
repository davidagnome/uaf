namespace UAF.Serialization;

/// <summary>
/// Writers for the two party-effect events an import produces.
/// </summary>
/// <remarks>
/// <b>Each is the exact mirror of its reader</b> — <c>PartyEffectEventReaders.ReadDamage</c> and
/// <c>MoreEventReaders.ReadVault</c>. An event body carries no length prefix, so a field written
/// at the wrong width or in the wrong order does not corrupt one event; it desynchronises every
/// event after it in the level.
/// </remarks>
public static class PartyEffectEventWriters
{
    /// <summary>Writes a <c>DAMAGE_EVENT_DATA</c> (<c>GameEvent.cpp:13585</c>).</summary>
    /// <remarks>Eleven <c>int</c>s after the base, with no version gates among them.</remarks>
    public static void WriteDamage(IArchiveWriteCursor ar, DamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(damage);

        GameEventWriter.Write(ar, damage.Base);

        ar.WriteInt32(damage.NbrAttacks);
        ar.WriteInt32(damage.ChancePerAttack);
        ar.WriteInt32(damage.DmgDice);
        ar.WriteInt32(damage.DmgDiceQty);
        ar.WriteInt32(damage.DmgBonus);
        ar.WriteInt32(damage.SaveBonus);
        ar.WriteInt32(damage.AttackThac0);
        ar.WriteInt32(damage.EventSave);
        ar.WriteInt32(damage.SpellSave);
        ar.WriteInt32(damage.Who);
        ar.WriteInt32(damage.Distance);
    }

    /// <summary>Writes a <c>VAULT_EVENT_DATA</c>.</summary>
    /// <remarks>
    /// <b><c>whichVault</c> is a <c>BYTE</c> and arrives at 0.910.</b> The reader gates it and
    /// yields zero below that version; the writer emits it unconditionally because
    /// <see cref="LevelFileWriter.WrittenVersion"/> is well past 0.910 — a file is only ever
    /// written at the modern version, never at the one it was read from.
    /// </remarks>
    public static void WriteVault(IArchiveWriteCursor ar, VaultEvent vault)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(vault);

        GameEventWriter.Write(ar, vault.Base);

        ar.WriteInt32(vault.ForceBackup);
        ar.WriteByte(vault.WhichVault);
    }
}
