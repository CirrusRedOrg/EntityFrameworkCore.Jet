using LibRed.Catalog;

namespace LibRed.Storage;

/// <summary>
/// The Chinese, Japanese and Korean sort orders: each is General of the same version plus a table of
/// per-character weights, measured from ACE and embedded one resource per order.
/// </summary>
/// <remarks>
/// <para>
/// Measured character by character over the whole BMP against Access-authored fixtures, every character these
/// orders weigh differently from General gets a single weight in its place — a replacement primary, and in
/// version 1 a secondary with it. The ideographs are what move: into pronunciation, stroke-count, Bopomofo or
/// radical order, 7,000 to 28,000 characters an order, with a handful of symbols besides (the Japanese orders
/// weigh <c>\</c> as <c>¥</c> and give <c>―</c> the unweighted <c>FF FF</c>). An iteration mark copies the
/// tailored weight before it.
/// </para>
/// <para>
/// Korean is the one order that is not only a table: it sorts Hangul first, moving the lead byte of every
/// weight General contributes (see <see cref="LocaleTailoring.LeadBytes"/>), and its table holds the hanja —
/// weighed by their Hangul reading, the primary a syllable's and the secondary telling hanja apart — with the
/// <c>\</c> weighed as <c>₩</c>.
/// </para>
/// <para>
/// Not every order Access offers is here. "Japanese - Unicode" (<c>0x00010411</c>) and "Korean - Unicode"
/// (<c>0x00010412</c>) are created by Access but refused by ACE on open, because Windows no longer supports
/// those alternate sorts — so there is no engine to measure them against, and they stay refused.
/// </para>
/// </remarks>
internal static class JetCjkSortOrders
{
    /// <summary>
    /// Suppresses the tables, leaving only the Korean lead-byte move, so the encoder is General of each order's
    /// version. Only the generator sets this, for the reason <see cref="JetTextCollationV1Overrides.Suppressed"/>
    /// gives: it records where the encoder disagrees with ACE, and must not measure an encoder that already
    /// agrees.
    /// </summary>
    internal static bool Suppressed { get; set; }

    /// <summary>Each order's collation and the name of its resource, which is the name of the fixture it was
    /// measured from.</summary>
    internal static readonly (Collation Collation, string Name)[] Orders =
    [
        (new(CollatingOrder.ChineseSimplified, 1), "ChinesePronunciation"),
        (new(CollatingOrder.ChineseSimplified, 0), "ChinesePronunciationLegacy"),
        (new(CollatingOrder.ChineseSimplified, 1, SortId: 2), "ChineseStrokeCount"),
        (new(CollatingOrder.ChineseSimplified, 0, SortId: 2), "ChineseStrokeCountLegacy"),
        (new(CollatingOrder.ChineseTraditional, 1, SortId: 3), "ChineseTradBopomofo"),
        (new(CollatingOrder.ChineseTraditional, 0, SortId: 3), "ChineseTradBopomofoLegacy"),
        (new(CollatingOrder.ChineseTraditional, 1), "ChineseTradStrokeCount"),
        (new(CollatingOrder.ChineseTraditional, 0), "ChineseTradStrokeCountLegacy"),
        (new(CollatingOrder.Japanese, 1), "Japanese"),
        (new(CollatingOrder.Japanese, 0), "JapaneseLegacy"),
        (new(CollatingOrder.Japanese, 1, SortId: 4), "JapaneseRadicalStrokeCount"),
        (new(CollatingOrder.Korean, 0), "Korean"),
    ];

    private static readonly Dictionary<Collation, Lazy<LocaleTailoring>> Tables =
        Orders.ToDictionary(o => o.Collation, o => new Lazy<LocaleTailoring>(() => Load(o.Collation, o.Name)));

    private static readonly byte[] KoreanLeads = KoreanLeadBytes();

    private static readonly Lazy<LocaleTailoring> Suppressable = new(() => new LocaleTailoring(
        new Dictionary<string, TailoredWeight>(), leadBytes: KoreanLeads));

    /// <summary>The order's tailoring, or null when the collation is not a CJK order.</summary>
    public static LocaleTailoring? For(Collation collation)
    {
        if (!Tables.TryGetValue(collation, out Lazy<LocaleTailoring>? table)) return null;
        if (!Suppressed) return table.Value;
        return collation.Order == CollatingOrder.Korean
            ? Suppressable.Value
            : new LocaleTailoring(new Dictionary<string, TailoredWeight>());
    }

    /// <summary>
    /// Korean's move of General's lead bytes: <c>81</c>–<c>F2</c> down <c>0x37</c> and <c>4A</c>–<c>80</c>
    /// up <c>0x72</c>, everything else in place. Measured over every weight General gives the BMP, with no
    /// exception in either band.
    /// </summary>
    private static byte[] KoreanLeadBytes()
    {
        var leads = new byte[256];
        for (int b = 0; b < 256; b++)
            leads[b] = (byte)(b switch
            {
                >= 0x4A and <= 0x80 => b + 0x72,
                >= 0x81 and <= 0xF2 => b - 0x37,
                _ => b,
            });
        return leads;
    }

    private static LocaleTailoring Load(Collation collation, string name)
    {
        using Stream stream = typeof(JetCjkSortOrders).Assembly
            .GetManifestResourceStream($"LibRed.Resources.Cjk.{name}.bin")
            ?? throw new InvalidOperationException($"The {name} sort-order resource is missing from the assembly.");

        var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        byte[] deltas = JetTextCollationV1Overrides.ReadStream(reader);
        byte[] primaryLengths = JetTextCollationV1Overrides.ReadStream(reader);
        byte[] primaryBytes = JetTextCollationV1Overrides.ReadStream(reader);
        byte[] secondaries = JetTextCollationV1Overrides.ReadStream(reader);

        var entries = new Dictionary<string, TailoredWeight>(count);
        int codePoint = 0, cursor = 0, primary = 0;
        for (int i = 0; i < count; i++)
        {
            codePoint += JetTextCollationV1Overrides.ReadVarInt(deltas, ref cursor);
            entries[((char)codePoint).ToString()] =
                new TailoredWeight(primaryBytes[primary..(primary + primaryLengths[i])], secondaries[i]);
            primary += primaryLengths[i];
        }
        return new LocaleTailoring(entries,
            leadBytes: collation.Order == CollatingOrder.Korean ? KoreanLeads : null,
            weighsDecompositions: false);
    }
}
