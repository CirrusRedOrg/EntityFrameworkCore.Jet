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

/// <summary>A column in a CREATE TABLE: its declared SQL type, optional size/scale, constraints, and the
/// raw text of an optional DEFAULT value expression (stored as the column's DefaultValue property).</summary>
public sealed record ColumnDefinition(
    string Name,
    string TypeName,
    int? Size,
    int? Scale,
    bool NotNull,
    bool PrimaryKey,
    string? Default = null);

/// <summary>Referential action for a foreign key's ON DELETE / ON UPDATE clause. Jet records only
/// enforce + cascade-update + cascade-delete, so NoAction/SetNull/SetDefault collapse to "no cascade".</summary>
public enum ReferentialAction { NoAction, Cascade, SetNull, SetDefault }

/// <summary>A FOREIGN KEY constraint (table-level, or a column-level REFERENCES): the child columns,
/// the referenced (parent) table and its columns, the ON DELETE / ON UPDATE actions, and whether the
/// FOREIGN KEY NO INDEX modifier was given (suppresses the backing index).</summary>
public sealed record ForeignKeyConstraint(
    string? Name,
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    ReferentialAction OnDelete,
    ReferentialAction OnUpdate,
    bool NoIndex = false);

/// <summary>A UNIQUE constraint (table-level, or a column-level UNIQUE) over one or more columns.</summary>
public sealed record UniqueConstraint(string? Name, IReadOnlyList<string> Columns);

/// <summary>A CHECK constraint: an optional name and the raw expression text (validated by Access, not
/// yet enforced by LibRed).</summary>
public sealed record CheckConstraint(string? Name, string Expression);

public sealed record CreateTableStatement(
    string Table,
    IReadOnlyList<ColumnDefinition> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeyConstraint> ForeignKeys,
    IReadOnlyList<UniqueConstraint> UniqueConstraints,
    IReadOnlyList<CheckConstraint> CheckConstraints) : SqlStatement;

/// <summary>The optional WITH clause of CREATE INDEX: PRIMARY (make it the primary key), DISALLOW NULL
/// (no nulls allowed), IGNORE NULL (rows with nulls excluded from the index).</summary>
public enum IndexWithOption { None, Primary, DisallowNull, IgnoreNull }

/// <summary>CREATE [UNIQUE] INDEX name ON table (col [ASC|DESC], …) [WITH …] — a new index on an
/// existing table.</summary>
public sealed record CreateIndexStatement(
    string Name,
    string Table,
    bool IsUnique,
    IReadOnlyList<(string Column, bool Descending)> Columns,
    IndexWithOption WithOption) : SqlStatement;

public enum ViewJoinKind { Inner, Left, Right }

/// <summary>A source in a view's FROM: either a named table (<paramref name="Table"/> set) or a derived
/// table (<paramref name="SubquerySql"/> = the verbatim inner subquery text, with a required alias).</summary>
public sealed record ViewSource(string? Table, string? Alias, string? SubquerySql = null);

/// <summary>A join in a view: its kind, the verbatim ON condition, and the left/right side aliases.</summary>
public sealed record ViewJoin(ViewJoinKind Kind, string Condition, string LeftAlias, string RightAlias);

/// <summary>A view's decomposed "simple SELECT" (columns/tables/joins/where, all as verbatim text), which
/// Access stores as MSysQueries rows. Aggregates, GROUP BY, HAVING and ORDER BY are not allowed in a view.</summary>
public sealed record ViewDefinition(
    bool Distinct,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ViewSource> Tables,
    IReadOnlyList<ViewJoin> Joins,
    string? Where);

/// <summary>CREATE VIEW view [(fields)] AS select — a stored query, decomposed for byte-faithful storage.</summary>
public sealed record CreateViewStatement(
    string Name,
    IReadOnlyList<string> Columns,
    ViewDefinition Definition,
    string QuerySql) : SqlStatement;

public sealed record Assignment(string Column, Expression Value) : SqlNode;

public sealed record UpdateStatement(
    string Table,
    IReadOnlyList<Assignment> Assignments,
    Expression? Where) : SqlStatement;

public sealed record DeleteStatement(
    string Table,
    Expression? Where) : SqlStatement;
