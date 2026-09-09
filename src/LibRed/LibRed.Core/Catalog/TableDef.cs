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

    /// <summary>True for the MSys* system tables.</summary>
    public bool IsSystem { get; init; }

    public ColumnDef? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
