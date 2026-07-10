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
/// <param name="Version">The sort-order version (column descriptor <c>0x0D</c>): 0 = the legacy order
/// (Access 2000–2007), 1 = the order introduced by Access 2010.</param>
public readonly record struct Collation(CollatingOrder Order, byte Version)
{
    /// <summary>Jet's "General legacy" order — locale 1033, version 0. The order LibRed reads and writes,
    /// and the default for every file it currently handles (an ACE-2007-format database).</summary>
    public static Collation GeneralLegacy => new(CollatingOrder.General, 0);

    /// <summary>Whether LibRed can encode index keys for this collation. Only General legacy is implemented;
    /// see <c>JetTextCollation</c> and the format spec §10.4.</summary>
    public bool IsIndexKeyEncodable => this == GeneralLegacy;
}
