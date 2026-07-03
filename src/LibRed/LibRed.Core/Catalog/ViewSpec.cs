namespace LibRed.Catalog;

/// <summary>Join kind in a view (stored as the MSysQueries join-row flag: inner=1, left=2, right=3).</summary>
public enum ViewJoinType { Inner = 1, Left = 2, Right = 3 }

/// <summary>A source table in a view's FROM.</summary>
public sealed record ViewTableSpec(string Table, string? Alias);

/// <summary>A join in a view: kind, verbatim ON condition, and the left/right side aliases.</summary>
public sealed record ViewJoinSpec(ViewJoinType Kind, string Condition, string LeftAlias, string RightAlias);

/// <summary>
/// A view's decomposed "simple SELECT" — the columns, source tables, joins and WHERE (all verbatim text) —
/// that Access stores as MSysQueries rows. Aggregates / GROUP BY / HAVING / ORDER BY are not permitted.
/// </summary>
public sealed record ViewSpec(
    bool Distinct,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ViewTableSpec> Tables,
    IReadOnlyList<ViewJoinSpec> Joins,
    string? Where);
