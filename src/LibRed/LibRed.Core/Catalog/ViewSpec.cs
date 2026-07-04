namespace LibRed.Catalog;

/// <summary>Join kind in a view (stored as the MSysQueries join-row flag: inner=1, left=2, right=3).</summary>
public enum ViewJoinType { Inner = 1, Left = 2, Right = 3 }

/// <summary>A source in a view's FROM: a named table (<paramref name="Table"/>), or a derived table whose
/// <paramref name="SubquerySql"/> is the verbatim inner subquery (stored in the MSysQueries table row's
/// Expression instead of Name1, with the alias in Name2).</summary>
public sealed record ViewTableSpec(string? Table, string? Alias, string? SubquerySql = null);

/// <summary>A join in a view: kind, verbatim ON condition, and the two tables it joins (from the
/// condition), stored as the join row's Name1/Name2.</summary>
public sealed record ViewJoinSpec(ViewJoinType Kind, string Condition, string LeftAlias, string RightAlias);

/// <summary>An output column of a view: its verbatim expression and optional alias (MSysQueries column
/// row Expression + Name1).</summary>
public sealed record ViewColumnSpec(string Expression, string? Alias);

/// <summary>
/// A view's decomposed "simple SELECT" — the columns, source tables, joins and WHERE (all verbatim text) —
/// that Access stores as MSysQueries rows. Aggregates / GROUP BY / HAVING / ORDER BY are not permitted.
/// </summary>
public sealed record ViewSpec(
    bool Distinct,
    IReadOnlyList<ViewColumnSpec> Columns,
    IReadOnlyList<ViewTableSpec> Tables,
    IReadOnlyList<ViewJoinSpec> Joins,
    string? Where);
