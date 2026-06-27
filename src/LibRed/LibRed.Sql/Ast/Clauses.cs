namespace LibRed.Sql.Ast;

/// <summary>Base type for a source of rows in a FROM clause.</summary>
public abstract record TableReference : SqlNode;

/// <summary>A named base table, optionally aliased.</summary>
public sealed record NamedTable(string Name, string? Alias) : TableReference;

/// <summary>A derived table (subquery) in the FROM clause.</summary>
public sealed record SubqueryTable(SelectStatement Query, string? Alias) : TableReference;

public enum JoinKind { Inner, Left, Right, Cross }

/// <summary>A join between two table references with an ON condition.</summary>
public sealed record JoinTable(
    TableReference Left,
    TableReference Right,
    JoinKind Kind,
    Expression? On) : TableReference;

public enum SortDirection { Ascending, Descending }

public sealed record OrderByItem(Expression Value, SortDirection Direction) : SqlNode;
