namespace LibRed.Catalog;

/// <summary>Describes an index: its columns (with sort direction), uniqueness and root page.</summary>
public sealed class IndexDef
{
    public required string Name { get; init; }

    /// <summary>Indexed columns in key order; <c>true</c> = ascending.</summary>
    public IReadOnlyList<(ColumnDef Column, bool Ascending)> Columns { get; init; } = [];

    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }

    /// <summary>Page number of the index B-tree root.</summary>
    public int RootPage { get; init; }
}
