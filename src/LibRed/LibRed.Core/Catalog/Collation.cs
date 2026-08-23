namespace LibRed.Catalog;

/// <summary>
/// A text collating order, identified by its Windows locale id (LCID) — the value Jet/ACE stores in a
/// column descriptor's locale bytes (<c>0x0B/0x0C</c>) and, database-wide, in the page-0 sort order.
/// These mirror DAO's <c>CollatingOrderEnum</c>; each name is the LCID Access records.
/// </summary>
/// <remarks>
/// The LCID alone does not pin the on-disk key bytes: "General" (1033) has a legacy order (version 0,
/// Access 2000–2007) and a different default order (version 1, Access 2010+). The version lives in a
/// separate descriptor byte — see <see cref="Collation"/>. Paradox-ISAM variants that DAO lists share
/// LCIDs with these (e.g. dbSortPDXIntl == 1033) and are omitted; they are link-only and irrelevant here.
/// </remarks>
public enum CollatingOrder
{
    Undefined = -1,
    Neutral = 1024,
    Arabic = 1025,
    ChineseTraditional = 1028,
    Czech = 1029,
    NorwegianDanish = 1030,
    Greek = 1032,
    General = 1033,          // English, German, French, Portuguese — the default
    Spanish = 1034,
    Hebrew = 1037,
    Hungarian = 1038,
    Icelandic = 1039,
    Japanese = 1041,
    Korean = 1042,
    Dutch = 1043,
    Polish = 1045,
    Cyrillic = 1049,
    SwedishFinnish = 1053,
    Thai = 1054,
    Turkish = 1055,
    Slovenian = 1060,
    ChineseSimplified = 2052,
}

/// <summary>
/// A fully-specified text collation: its <see cref="CollatingOrder"/> (LCID) plus the sort-order
/// <see cref="Version"/> that selects between weight tables sharing that LCID. Determines the index-key
/// bytes for text/memo columns, and is written into their column descriptors.
/// </summary>
/// <param name="Order">The collating order (LCID).</param>
/// <param name="Version">The sort-order version — the byte at column-descriptor <c>0x0E</c> (the high byte of
/// a nominally 2-byte version field at <c>0x0D</c>). Verified vs Access: General Legacy (Access 2000–2007) =
/// <c>0</c>; the "General" order Access 2010 made default = <c>1</c>. <b>The low byte at <c>0x0D</c> is <c>0</c>
/// in every file seen and is not modelled — but the field is nominally 2 bytes, so keep an eye on it: if a
/// database ever carries a non-zero <c>0x0D</c> we are truncating a wider value (cf. the AutoNumber increment
/// at TDEF <c>0x18</c>, which looked like a 1-byte flag but was a full signed int32).</b></summary>
public readonly record struct Collation(CollatingOrder Order, byte Version)
{
    /// <summary>The sort-order version byte for the Access-2010+ "General" order.</summary>
    public const byte GeneralVersion = 1;

    /// <summary>Jet's "General legacy" order — locale 1033, version 0. The order LibRed reads and writes and
    /// can encode index keys for.</summary>
    public static Collation GeneralLegacy => new(CollatingOrder.General, 0);

    /// <summary>The Access-2010+ default "General" order — locale 1033, version 1. Its keys use the Windows
    /// NLS weights directly rather than General-Legacy's compacted table; see <c>JetTextCollationV1</c>.</summary>
    public static Collation General => new(CollatingOrder.General, GeneralVersion);

    /// <summary>Whether LibRed can encode index keys for this collation. Both General orders are implemented
    /// (v0 via <c>JetTextCollation</c>, v1 via <c>JetTextCollationV1</c>); other locales are not — see the
    /// format spec §10.4.</summary>
    public bool IsIndexKeyEncodable => this == GeneralLegacy || this == General;
}
