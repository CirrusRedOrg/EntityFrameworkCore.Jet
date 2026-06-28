namespace LibRed.Sql.Ast;

/// <summary>Base type for top-level SQL statements.</summary>
public abstract record SqlStatement : SqlNode;

/// <summary>A single item in a SELECT projection, optionally aliased.</summary>
public sealed record SelectItem(Expression Value, string? Alias) : SqlNode;

public sealed record SelectStatement(
    IReadOnlyList<SelectItem> Projection,
    bool IsSelectStar,
    TableReference From,
    Expression? Where,
    IReadOnlyList<Expression> GroupBy,
    Expression? Having,
    IReadOnlyList<OrderByItem> OrderBy,
    int? Top) : SqlStatement;

public enum SetOperator { Union, UnionAll, Intersect, Except }

/// <summary>
/// A set operation combining two queries. UNION dedupes, UNION ALL keeps duplicates,
/// INTERSECT keeps rows in both, EXCEPT keeps rows in the left not in the right.
/// </summary>
public sealed record SetOperationStatement(
    SqlStatement Left,
    SetOperator Operator,
    SqlStatement Right) : SqlStatement;

public sealed record InsertStatement(
    string Table,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<Expression>> Rows) : SqlStatement;

/// <summary>A column in a CREATE TABLE: its declared SQL type, optional size/scale and constraints.</summary>
public sealed record ColumnDefinition(
    string Name,
    string TypeName,
    int? Size,
    int? Scale,
    bool NotNull,
    bool PrimaryKey);

public sealed record CreateTableStatement(
    string Table,
    IReadOnlyList<ColumnDefinition> Columns,
    IReadOnlyList<string> PrimaryKey) : SqlStatement;

public sealed record Assignment(string Column, Expression Value) : SqlNode;

public sealed record UpdateStatement(
    string Table,
    IReadOnlyList<Assignment> Assignments,
    Expression? Where) : SqlStatement;

public sealed record DeleteStatement(
    string Table,
    Expression? Where) : SqlStatement;
