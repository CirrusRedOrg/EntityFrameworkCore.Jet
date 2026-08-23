namespace LibRed.Catalog;

/// <summary>
/// A text collating order, identified by its Windows locale id (LCID) — the value Jet/ACE stores in a
/// column descriptor's locale bytes (<c>0x0B/0x0C</c>) and, database-wide, in the page-0 sort order.
/// These mirror DAO's <c>CollatingOrderEnum</c>; each name is the LCID Access records. That is a Jet-3.5-era
/// list and no longer matches what ACE offers, in both directions: Access's "New Database Sort Order" adds
/// Bosnian, Croatian, Serbian, Macedonian, Ukrainian, Estonian, Latvian, Lithuanian, Slovak, Romanian,
/// Georgian Modern, Vietnamese, Indic, French, German Phone Book, Hungarian Technical and the CJK variants,
/// and offers none of the five marked <i>inert</i> below. Those five are still creatable through DAO and are
/// recorded faithfully on page 0 and in column descriptors, but ACE encodes **General** keys for them
/// regardless — verified over 31 samples in <c>DaoLocaleCollationProbeTest</c>. Treat them as metadata.
/// </summary>
/// <remarks>
/// The LCID alone does not pin the on-disk key bytes: "General" (1033) has a legacy order (version 0,
/// Access 2000–2007) and a different default order (version 1, Access 2010+). The version lives in a
/// separate descriptor byte — see <see cref="Collation"/>. The two axes are independent: both Spanish orders
/// are version 0, so the version selects a weight-table generation rather than a locale variant. Paradox-ISAM
/// variants that DAO lists share LCIDs with these (e.g. dbSortPDXIntl == 1033) and are omitted; they are
/// link-only and irrelevant here.
/// </remarks>
public enum CollatingOrder
{
    Undefined = -1,
    Neutral = 1024,
    Arabic = 1025,           // inert: recorded, but ACE encodes General keys — see the remarks below
    ChineseTraditional = 1028,
    Czech = 1029,
    NorwegianDanish = 1030,
    Greek = 1032,            // inert
    German = 1031,           // with sort id 1 = "German Phone Book"
    General = 1033,          // English, German, French, Portuguese — the default
    Spanish = 1034,          // Spanish Traditional: "ch" and "ll" are letters (DAO's dbSortSpanish)
    French = 1036,
    Hebrew = 1037,           // inert
    Hungarian = 1038,        // with sort id 1 = "Hungarian Technical"
    Icelandic = 1039,
    Japanese = 1041,
    Korean = 1042,
    Dutch = 1043,            // inert
    Norwegian = 1044,        // Access's "Norwegian/Danish" — note DAO's dbSortNorwDan is Danish 1030 instead
    Polish = 1045,
    Romanian = 1048,
    Cyrillic = 1049,         // inert
    Croatian = 1050,
    Slovak = 1051,
    SwedishFinnish = 1053,
    Thai = 1054,
    Turkish = 1055,
    Ukrainian = 1058,
    Slovenian = 1060,
    Estonian = 1061,
    Latvian = 1062,
    Lithuanian = 1063,
    Vietnamese = 1066,
    Macedonian = 1071,
    Georgian = 1079,         // with sort id 1 = "Georgian Modern"
    Indic = 1081,
    ChineseSimplified = 2052,
    Serbian = 2074,
    SpanishModern = 3082,    // The 1994 reform: "ch"/"ll" are letter pairs. No DAO name — it postdates the enum
    Bosnian = 5146,
}

/// <summary>
/// A fully-specified text collation: its <see cref="CollatingOrder"/> (LCID) plus the sort-order
/// <see cref="Version"/> that selects between weight tables sharing that LCID. Determines the index-key
/// bytes for text/memo columns, and is written into their column descriptors.
/// </summary>
/// <param name="Order">The collating order's LANGID — column descriptor <c>0x0B</c>/<c>0x0C</c>.</param>
/// <param name="Version">The sort-order version — the byte at column-descriptor <c>0x0E</c>. Verified vs
/// Access: the legacy compacted table (Access 2000–2007) = <c>0</c>; the "General" order Access 2010 made
/// default = <c>1</c>. Not a General-only axis: Croatian and Romanian each ship in both versions.</param>
/// <param name="SortId">The LCID's high word — column descriptor <c>0x0D</c>, page-0 <c>0x70</c>. Zero for a
/// locale's default order; non-zero selects a Windows <i>alternate sort order</i>, which is a different
/// ordering for the <i>same</i> LANGID. <b>Without it Hungarian Technical (<c>0x0001040E</c>) is
/// indistinguishable from Hungarian (<c>0x0000040E</c>), and German Phone Book from German.</b> This byte was
/// documented as "0 in every file seen — keep an eye on it" until fixtures for those two orders showed it
/// carrying <c>0x01</c>.</param>
public readonly record struct Collation(CollatingOrder Order, byte Version, byte SortId = 0)
{
    /// <summary>The full 32-bit Windows LCID: <c>(SortId &lt;&lt; 16) | LANGID</c>. The version is not part of
    /// it — Jet stores that in the LCID's unused top byte, but Windows does not define it.</summary>
    public int Lcid => (SortId << 16) | (int)Order;

    /// <summary>The sort-order version byte for the Access-2010+ "General" order.</summary>
    public const byte GeneralVersion = 1;

    /// <summary>Jet's "General legacy" order — locale 1033, version 0. The order LibRed reads and writes and
    /// can encode index keys for.</summary>
    public static Collation GeneralLegacy => new(CollatingOrder.General, 0);

    /// <summary>The Access-2010+ default "General" order — locale 1033, version 1. Its keys use the Windows
    /// NLS weights directly rather than General-Legacy's compacted table; see <c>JetTextCollationV1</c>.</summary>
    public static Collation General => new(CollatingOrder.General, GeneralVersion);

    /// <summary>Whether LibRed can encode index keys for this collation: both General orders (v0 via
    /// <c>JetTextCollation</c>, v1 via <c>JetTextCollationV1</c>), plus every locale with an entry in
    /// <c>JetLocaleTailoring</c>. Anything else is refused rather than encoded with the English table — see
    /// the format spec §10.4.</summary>
    public bool IsIndexKeyEncodable
    {
        get
        {
            if (this == GeneralLegacy || this == General) return true;
            var tailoring = Storage.JetLocaleTailoring.For(this);
            if (tailoring is null) return false;
            // The v1 encoder has no tailoring hook — its primaries are 2-byte NLS values, a different shape —
            // so a version-1 order is encodable only where it was measured to need no tailoring at all.
            return Version != GeneralVersion || tailoring.Entries.Count == 0;
        }
    }
}
