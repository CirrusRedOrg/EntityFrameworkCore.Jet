namespace LibRed.Sql.Ast;

/// <summary>Base type for a source of rows in a FROM clause.</summary>
public abstract record TableReference : SqlNode;

/// <summary>A named base table, optionally aliased.</summary>
public sealed record NamedTable(string Name, string? Alias) : TableReference;

/// <summary>A derived table (subquery) in the FROM clause. The query is any <see cref="SqlStatement"/>
/// query — a <see cref="SelectStatement"/> or a <see cref="SetOperationStatement"/> (e.g. a UNION).</summary>
public sealed record SubqueryTable(SqlStatement Query, string? Alias) : TableReference;

/// <summary>
/// <see cref="Full"/> is a LibRed extension - ACE has no full outer join. So are <see cref="CrossApply"/> and
/// <see cref="OuterApply"/>, the two <b>lateral</b> kinds: their right side is evaluated once per left row with
/// that row in scope (so it may correlate to the left) and they carry no ON condition. CROSS APPLY drops a left
/// row whose right side produced nothing; OUTER APPLY keeps it null-padded, as <see cref="Left"/> does.
/// </summary>
public enum JoinKind { Inner, Left, Right, Cross, Full, CrossApply, OuterApply }

/// <summary>A join between two table references with an ON condition (null for the kinds that take none:
/// <see cref="JoinKind.Cross"/> and the two APPLY kinds).</summary>
public sealed record JoinTable(
    TableReference Left,
    TableReference Right,
    JoinKind Kind,
    Expression? On) : TableReference;

public enum SortDirection { Ascending, Descending }

public sealed record OrderByItem(Expression Value, SortDirection Direction) : SqlNode;
