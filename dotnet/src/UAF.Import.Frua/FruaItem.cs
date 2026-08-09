using System.Text;

namespace UAF.Import.Frua;

/// <summary>A damage roll (<c>ImportUADamage</c>, <c>UAFWinEd/UAImport.cpp:5381</c>).</summary>
public readonly record struct FruaDamage(byte Dice, byte Sides, byte Bonus);

/// <summary>
/// A general item class from <c>items.dat</c> (<c>ImportUAItems</c>,
/// <c>UAFWinEd/UAImport.cpp:5391</c>).
/// </summary>
/// <remarks>
/// <b>Not to be confused with the UAF <c>items.dat</c></b>, which is a different format entirely
/// and is read by <c>UAF.Serialization</c>. This one is DOS FRUA's, 16 bytes per record and no
/// framing at all — the file's length is the record count.
/// </remarks>
/// <param name="Slot">Where it is carried; 0 is the weapon hand, 1 the shield hand.</param>
/// <param name="TwoHanded">How many hands it needs.</param>
/// <param name="VersusLarge">Damage against a large target.</param>
/// <param name="VersusSmall">Damage against a small or medium one.</param>
/// <param name="Rate">Attacks per two rounds.</param>
/// <param name="Protection">Armour value.</param>
/// <param name="CuttingOrBlunt">0 not a weapon, 1 cutting, 128 blunt.</param>
/// <param name="Melee">Whether it can be used in melee.</param>
/// <param name="Range">Reach in squares.</param>
/// <param name="Classes">A bitmask: 1 magic-user, 2 cleric, 4 thief, 8 fighter, 64 paladin/ranger.</param>
/// <param name="WeaponType">
/// 0 none, 4 hand-held, 10 sling, 11/15 bow, 18 ammunition, 20 hand-held-or-thrown, 26 thrown,
/// 138 crossbow.
/// </param>
public sealed record FruaItemClass(
    byte Slot, byte TwoHanded, FruaDamage VersusLarge, byte Rate, byte Protection,
    byte CuttingOrBlunt, byte Melee, FruaDamage VersusSmall, byte Range, byte Classes,
    byte WeaponType)
{
    /// <summary>Bytes per record.</summary>
    public const int Length = 16;

    /// <summary>Reads one class record.</summary>
    public static FruaItemClass Read(ReadOnlySpan<byte> b) =>
        new(Slot: b[0],
            TwoHanded: b[1],
            VersusLarge: new FruaDamage(b[2], b[3], b[4]),
            Rate: b[5],
            Protection: b[6],
            CuttingOrBlunt: b[7],
            Melee: b[8],
            VersusSmall: new FruaDamage(b[9], b[10], b[11]),
            Range: b[12],
            Classes: b[13],
            WeaponType: b[14]);

    // b[15] is the declaration's "foo" and is not read.
}

/// <summary>
/// A specific item from <c>item.dat</c> (<c>ImportUAItem</c>,
/// <c>UAFWinEd/UAImport.cpp:5410</c>).
/// </summary>
/// <param name="ClassIndex">Which <see cref="FruaItemClass"/> it draws its mechanics from.</param>
/// <param name="Name1">First vocabulary index.</param>
/// <param name="Name2">Second, or a quantity when <paramref name="Name3"/> is 77.</param>
/// <param name="Name3">Third.</param>
/// <param name="Encumbrance">In coins; ten coins to the pound.</param>
/// <param name="Price">In platinum.</param>
/// <param name="Identified">
/// <b>A bitmask over the three name fields, not a boolean.</b> Bit 0 hides the first word from the
/// unidentified name, bit 1 the second, bit 2 the third — which is how "Long Sword +1" reads as
/// "Long Sword" until it is identified.
/// </param>
/// <param name="Charges">0 means permanent.</param>
public sealed record FruaItem(
    byte ClassIndex, byte Name1, byte Name2, byte Name3,
    ushort Encumbrance, ushort Price, byte MagicBonus, byte SecondaryCode,
    byte Ready, byte Identified, byte Cursed, byte BundleQuantity, byte Charges,
    byte MagicalCode, byte SpecialCode)
{
    /// <summary>Bytes per record.</summary>
    public const int Length = 18;

    /// <summary>
    /// <b>The three name fields are stored in reverse.</b> The declaration reads
    /// <c>name3, name2, name1</c>, so the byte order on disk is the opposite of the order the
    /// words appear in.
    /// </summary>
    public static FruaItem Read(ReadOnlySpan<byte> b) =>
        new(ClassIndex: b[0],
            Name3: b[1],
            Name2: b[2],
            Name1: b[3],
            Encumbrance: (ushort)(b[4] | (b[5] << 8)),
            Price: (ushort)(b[6] | (b[7] << 8)),
            MagicBonus: b[8],
            SecondaryCode: b[9],
            Ready: b[10],
            Identified: b[11],
            Cursed: b[12],
            BundleQuantity: b[13],
            Charges: b[14],
            MagicalCode: b[15],
            SpecialCode: b[16]);

    /// <summary>
    /// The item's full name, as it reads once identified.
    /// </summary>
    public string Name => Compose(hideUnidentified: false);

    /// <summary>
    /// What the party sees before identifying it.
    /// </summary>
    /// <remarks>
    /// Each bit of <see cref="Identified"/> suppresses one of the three words, so a magical suffix
    /// disappears while the base name stays.
    /// </remarks>
    public string UnidentifiedName => Compose(hideUnidentified: true);

    private string Compose(bool hideUnidentified)
    {
        var text = new StringBuilder();

        if (Name1 is > 0 and < FruaItemVocabulary.Limit
            && !(hideUnidentified && (Identified & 1) != 0))
        {
            text.Append(FruaItemVocabulary.Word(Name1)).Append(' ');
        }

        if (Name2 is > 0 and < FruaItemVocabulary.Limit
            && !(hideUnidentified && (Identified & 2) != 0))
        {
            // With a "Bundle of" third word, the second field is a count rather than a word.
            text.Append(Name3 == FruaItemVocabulary.BundleOf
                            ? Name2.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : FruaItemVocabulary.Word(Name2))
                .Append(' ');
        }

        if (Name3 is > 0 and < FruaItemVocabulary.Limit
            && !(hideUnidentified && (Identified & 4) != 0))
        {
            // No trailing space on the last word -- the reference appends none.
            text.Append(FruaItemVocabulary.Word(Name3));
        }

        return text.ToString().TrimEnd();
    }
}

/// <summary>
/// A DOS FRUA item database: the classes and the items that point at them.
/// </summary>
/// <remarks>
/// <b>Neither file has a header or a count</b> — the reference reads records until <c>fread</c>
/// fails, capped at 300. The stock database is 128 classes and 254 items, and both files divide
/// exactly, so the length is the count.
/// <para>
/// <b>Its record counter is off by one.</b> The reference writes
/// <c>while (n &lt; 300 &amp;&amp; success) { success = fread(...); n++; }</c>, incrementing even on
/// the read that failed, so the count it logs is one past what it read. Nothing depends on the
/// figure, but it is not the number of records.
/// </para>
/// </remarks>
public sealed record FruaItemDatabase(
    IReadOnlyList<FruaItemClass> Classes, IReadOnlyList<FruaItem> Items)
{
    /// <summary>The most records the reference will read from either file.</summary>
    public const int MaxRecords = 300;

    /// <summary>Reads both files out of a directory, or null when either is missing.</summary>
    public static FruaItemDatabase? Read(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string? classPath = FruaFiles.Resolve(directory, "items.dat");
        string? itemPath = FruaFiles.Resolve(directory, "item.dat");

        if (classPath is null || itemPath is null)
        {
            return null;
        }

        return new FruaItemDatabase(
            Records(File.ReadAllBytes(classPath), FruaItemClass.Length, FruaItemClass.Read),
            Records(File.ReadAllBytes(itemPath), FruaItem.Length, FruaItem.Read));
    }

    private static List<T> Records<T>(byte[] bytes, int size, ReadRecord<T> read)
    {
        var records = new List<T>();

        for (int at = 0; at + size <= bytes.Length && records.Count < MaxRecords; at += size)
        {
            records.Add(read(bytes.AsSpan(at, size)));
        }

        return records;
    }

    private delegate T ReadRecord<out T>(ReadOnlySpan<byte> bytes);
}
