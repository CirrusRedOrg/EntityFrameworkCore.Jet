namespace LibRed.Sql.Ast;

/// <summary>Base type for a source of rows in a FROM clause.</summary>
public abstract record TableReference : SqlNode;

/// <summary>A named base table, optionally aliased.</summary>
public sealed record NamedTable(string Name, string? Alias) : TableReference;

/// <summary>A derived table (subquery) in the FROM clause. The query is any <see cref="SqlStatement"/>
/// query — a <see cref="SelectStatement"/> or a <see cref="SetOperationStatement"/> (e.g. a UNION).</summary>
public sealed record SubqueryTable(SqlStatement Query, string? Alias) : TableReference;

/// <summary><see cref="Full"/> is a LibRed extension - ACE has no full outer join.</summary>
public enum JoinKind { Inner, Left, Right, Cross, Full }

/// <summary>A join between two table references with an ON condition.</summary>
public sealed record JoinTable(
    TableReference Left,
    TableReference Right,
    JoinKind Kind,
    Expression? On) : TableReference;

public enum SortDirection { Ascending, Descending }

public sealed record OrderByItem(Expression Value, SortDirection Direction) : SqlNode;
