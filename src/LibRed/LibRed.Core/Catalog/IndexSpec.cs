namespace LibRed.Catalog;

/// <summary>An index to create over the named columns, anchored at an already-allocated root page.</summary>
public sealed record IndexSpec(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsPrimaryKey,
    bool IsUnique,
    int RootPage,
    int UsageMapRow = 0,
    int UsageMapPage = 0);