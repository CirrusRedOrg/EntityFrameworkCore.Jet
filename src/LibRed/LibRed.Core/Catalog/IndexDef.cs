namespace LibRed.Catalog;

/// <summary>Describes an index: its columns (with sort direction), uniqueness and root page.</summary>
public sealed record IndexDef
{
    public required string Name { get; init; }

    /// <summary>Indexed columns in key order; <c>true</c> = ascending.</summary>
    public IReadOnlyList<(ColumnDef Column, bool Ascending)> Columns { get; init; } = [];

    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }

    /// <summary>WITH IGNORE NULL: rows with a null in any indexed column are excluded from the index
    /// (index flag <c>0x02</c>). Such rows are not added to the B-tree on insert.</summary>
    public bool IgnoreNulls { get; init; }

    /// <summary>
    /// The index's unique-entry count from the TDEF statistics block. This is a cumulative
    /// count of distinct entries ever added that Access increments but <b>never decrements</b>,
    /// so it equals the current distinct-value count (cardinality) only when no rows have been
    /// deleted. (Same semantics as Jackcess's <c>uniqueEntryCount</c>.)
    /// </summary>
    public int UniqueEntryCount { get; init; }

    /// <summary>Page number of the index B-tree root. Updated in place when the root splits (grows a level).</summary>
    public int RootPage { get; internal set; }

    /// <summary>
    /// This index's position among the table's real indexes — the ordinal of its 12-byte statistics
    /// block (at <c>TdefRealIndexBlockOffset + ordinal × RealIndexEntrySize</c>) and its index-data
    /// block. Used to locate the stats block when maintaining the unique-entry count on insert.
    /// </summary>
    public int RealIndexOrdinal { get; init; }
}
