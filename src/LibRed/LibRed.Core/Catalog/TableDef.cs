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

    /// <summary>CHECK constraints (name, expression), read from the table's extended-properties
    /// (<c>LvProp</c>) blob. Set by the catalog after the definition is decoded.</summary>
    public IReadOnlyList<(string Name, string Expression)> CheckConstraints { get; internal set; } = [];

    /// <summary>True for the MSys* system tables.</summary>
    public bool IsSystem { get; init; }

    public ColumnDef? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
