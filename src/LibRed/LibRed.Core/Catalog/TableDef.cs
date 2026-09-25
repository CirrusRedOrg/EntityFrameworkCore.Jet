namespace LibRed.Catalog;

/// <summary>
/// The resolved definition of a table: its columns and indexes plus the page that
/// anchors its data. Produced by <see cref="JetCatalog"/> and consumed by the
/// storage layer to open a <see cref="Storage.Table"/>.
/// </summary>
public sealed class TableDef
{
    public required string Name { get; init; }

    /// <summary>Page number of the table's TDEF (definition) page.</summary>
    public required int DefinitionPage { get; init; }

    public IReadOnlyList<ColumnDef> Columns { get; init; } = [];
    public IReadOnlyList<IndexDef> Indexes { get; init; } = [];

    /// <summary>Every name the TDEF gives an index, including the relationship names that share a real index
    /// with a named one — see <see cref="LogicalIndexDef"/>. <see cref="Indexes"/> holds one entry per real
    /// index and so carries only the name that won.</summary>
    public IReadOnlyList<LogicalIndexDef> LogicalIndexes { get; init; } = [];

    /// <summary>
    /// The variable-column count from the TDEF header (<c>0x2B</c>) — a <b>high-water</b> mark, not a live
    /// count. It never decrements on DROP COLUMN, so it exceeds the number of variable columns still present
    /// once one has been dropped, and it is the number of variable slots a row carries.
    /// </summary>
    /// <remarks>
    /// Carried on the table rather than derived from <see cref="Columns"/> because the two differ exactly
    /// where it matters. <c>max(VariableIndex) + 1</c> over the live columns equals this until the LAST
    /// variable column is dropped, and then falls below it — leaving a row one slot short of what the
    /// definition says, which the ALTER COLUMN re-lay then appends onto and lands on the wrong index.
    /// </remarks>
    public int VariableColumnCount { get; init; }

    /// <summary>The column-id high-water from the TDEF header (<c>0x29</c>) — how many ids the table has handed
    /// out, which never decrements either. It is what a row's leading count and null-bitmap width span, so it
    /// outlives a dropped column: a two-column table whose second column is dropped keeps writing rows that
    /// count 2 (measured vs ACE).</summary>
    public int ColumnIdHighWater { get; init; }

    /// <summary>The complex-type AutoNumber high-water (TDEF header <c>0x1C</c>) — the next id for a complex
    /// (multi-value/attachment) column. Carried for faithful round-trip; 0 for every table LibRed handles.</summary>
    public int ComplexAutoNumber { get; init; }

    /// <summary>CHECK constraints (name, expression), read from the table's extended-properties
    /// (<c>LvProp</c>) blob. Set by the catalog after the definition is decoded.</summary>
    public IReadOnlyList<(string Name, string Expression)> CheckConstraints { get; internal set; } = [];

    /// <summary>The table's <c>ValidationRule</c>/<c>ValidationText</c> designer properties, read from the
    /// extended-properties (<c>LvProp</c>) blob; null if none. Surfaced through
    /// <c>INFORMATION_SCHEMA.TABLES</c> (VALIDATION_RULE/VALIDATION_TEXT), matching EFCore.Jet's
    /// <c>AdoxSchema.GetTables</c> (<c>Jet OLEDB:Table Validation Rule/Text</c>).</summary>
    public string? ValidationRule { get; internal set; }

    /// <inheritdoc cref="ValidationRule"/>
    public string? ValidationText { get; internal set; }

    /// <summary>The row count the TDEF header carries (<c>0x10</c>), which the engine keeps current as rows are
    /// inserted and deleted. Reported as the table's cardinality in schema metadata, as Access reports it.</summary>
    public int RowCount { get; init; }

    /// <summary>True for the MSys* system tables.</summary>
    public bool IsSystem { get; init; }

    /// <summary>The object's raw <c>MSysObjects.Flags</c>, kept as read so callers can classify an object the
    /// way Access does rather than by name — the system bit (<c>0x80000000</c>), the hidden bit (<c>0x08</c>)
    /// and the bits that keep an object out of the schema rowsets altogether are all in here. Set by the
    /// catalog after the definition is decoded; zero for a table built without one (a test's synthetic
    /// definition, say).</summary>
    public uint ObjectFlags { get; internal set; }

    /// <summary>
    /// One <see cref="IndexDef"/> per B-tree the table actually has: those with a root page, deduplicated by
    /// it. Several logical indexes can name one index-data block — a primary key that also backs a
    /// relationship is the everyday case — and they are the same tree, so anything that maintains index
    /// entries must visit it once. Doing it per <see cref="Indexes"/> entry writes the same key twice.
    /// </summary>
    public IEnumerable<IndexDef> RealIndexes =>
        Indexes.Where(i => i.RootPage > 0).GroupBy(i => i.RootPage).Select(g => g.First());

    public ColumnDef? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The column, or the error naming the table that is missing it — the partner of
    /// <see cref="JetCatalog.RequireTable"/>, for a column the caller's own correctness depends on rather than
    /// one the schema may or may not have.</summary>
    public ColumnDef RequireColumn(string name) =>
        FindColumn(name) ?? throw new InvalidOperationException($"'{Name}' is missing the '{name}' column.");
}