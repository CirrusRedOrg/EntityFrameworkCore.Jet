namespace LibRed.Sql.Ast;

/// <summary>Base type for top-level SQL statements.</summary>
public abstract record SqlStatement : SqlNode;

/// <summary>Which transaction-control action a <see cref="TransactionControlStatement"/> performs.</summary>
public enum TransactionControlKind { Begin, Commit, Rollback }

/// <summary>A transaction-control statement: <c>BEGIN</c>/<c>COMMIT</c>/<c>ROLLBACK [TRANSACTION|WORK]</c>.
/// Manages the transaction rather than running inside one.</summary>
public sealed record TransactionControlStatement(TransactionControlKind Kind) : SqlStatement;

/// <summary>A single item in a SELECT projection, optionally aliased.</summary>
public sealed record SelectItem(Expression Value, string? Alias) : SqlNode;

public sealed record SelectStatement(
    IReadOnlyList<SelectItem> Projection,
    bool IsSelectStar,
    // Null for a FROM-less SELECT (e.g. `SELECT 2`): a single-row source that evaluates the projection once.
    TableReference? From,
    Expression? Where,
    IReadOnlyList<Expression> GroupBy,
    Expression? Having,
    IReadOnlyList<OrderByItem> OrderBy,
    Expression? Top,
    bool Distinct = false,
    // DISTINCTROW: dedupe on the underlying rows of the output-contributing tables (Access-specific).
    bool DistinctRow = false,
    // TOP n PERCENT: Top is a percentage (0–100) of the row count rather than an absolute count.
    bool TopPercent = false,
    /// <summary>
    /// The <c>INTO newtable</c> of a MAKE-TABLE query: the rows are written to a new table of that name
    /// rather than returned. Null for an ordinary SELECT.
    /// </summary>
    /// <remarks>
    /// The new table takes the result's column names and types and nothing else. Measured against ACE: the
    /// source's PRIMARY KEY and indexes are NOT copied, an expression column is typed from the expression
    /// (<c>Qty * 2</c> gives Int32, a concatenation gives Text(255)), <c>SUM</c> widens to Double, an empty
    /// result still creates the table, and an existing name is an error ("Table 'X' already exists").
    /// </remarks>
    string? Into = null,
    /// <summary>
    /// Rows to skip, from ANSI <c>OFFSET n ROWS</c>. The take half of <c>OFFSET … FETCH NEXT m</c>, and a
    /// standalone <c>FETCH FIRST m ROWS ONLY</c>, both land in <see cref="Top"/> instead — they mean exactly
    /// <c>TOP m</c> and so reuse the path that already exists for it. <see cref="TopPercent"/> is never set
    /// from a FETCH, which has no PERCENT form.
    /// </summary>
    Expression? Offset = null) : SqlStatement;

/// <summary><c>EXECUTE|EXEC procedure [arg, …]</c> — invokes a stored procedure/query by name, passing
/// positional argument values that bind to its declared parameters (in declaration order).</summary>
public sealed record ExecuteStatement(string Procedure, IReadOnlyList<Expression> Arguments) : SqlStatement;

/// <summary>Jet's conditional DDL <c>IF [NOT] EXISTS (&lt;condition&gt;) THEN &lt;then&gt;</c> — EF emits it for
/// idempotent migrations (e.g. create the history table only if it isn't already in INFORMATION_SCHEMA.TABLES).
/// <paramref name="Then"/> runs when the (possibly correlated) condition subquery returns a row — or, when
/// <paramref name="Negated"/>, when it returns none.</summary>
public sealed record IfThenStatement(bool Negated, SelectStatement Condition, SqlStatement Then) : SqlStatement;

/// <summary>A FROM-less <c>SELECT @@IDENTITY</c> / <c>SELECT @@ROWCOUNT</c> (a comma list of system
/// variables only). ACE allows these without a FROM clause; they yield a single row. The projection
/// items' <see cref="SelectItem.Value"/> are all <see cref="SystemVariableExpression"/>.</summary>
public sealed record SystemVariableSelectStatement(IReadOnlyList<SelectItem> Projection) : SqlStatement;

public enum SetOperator { Union, UnionAll, Intersect, Except }

/// <summary>
/// A set operation combining two queries. UNION dedupes, UNION ALL keeps duplicates,
/// INTERSECT keeps rows in both, EXCEPT keeps rows in the left not in the right.
/// </summary>
public sealed record SetOperationStatement(
    SqlStatement Left,
    SetOperator Operator,
    SqlStatement Right) : SqlStatement;

/// <summary>
/// A table value constructor used as a query rather than as an INSERT's VALUES clause —
/// <c>VALUES (1), (2)</c> — yielding one row per list. EF Core emits it as an operand of a set operation for
/// an inline collection.
/// </summary>
/// <remarks>
/// The rows carry no column names of their own. In the set operation that always encloses one today, names
/// come from the leading query per SQL, so none are needed; naming them would require the column alias list
/// (<c>AS t(a, b)</c>) that derived tables do not yet support. Row values may reference outer columns, so the
/// expressions are evaluated per outer row rather than once. <c>DEFAULT</c> is rejected here — the standard
/// permits it only inside an INSERT.
/// </remarks>
public sealed record ValuesStatement(IReadOnlyList<IReadOnlyList<Expression>> Rows) : SqlStatement;

/// <summary>
/// An INSERT — Access's two append-query forms.
/// </summary>
/// <param name="Rows">
/// The <b>single-record</b> form's values, <c>INSERT INTO t (…) VALUES (…)</c>. Empty when
/// <paramref name="Source"/> is set.
/// </param>
/// <param name="Source">
/// The <b>multiple-record</b> form's query, <c>INSERT INTO t (…) SELECT … FROM …</c>. Null for the
/// single-record form. Access documents the source as a SELECT; a full query expression is accepted here so
/// a UNION can feed an append, which is the shape EF emits from a Concat.
/// </param>
/// <param name="DefaultValues">
/// The <c>DEFAULT VALUES</c> form (no column or value list): a single row where every column takes its
/// default / AutoNumber. <paramref name="Columns"/> is empty and <paramref name="Rows"/> holds one empty row.
/// </param>
public sealed record InsertStatement(
    string Table,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<Expression>> Rows,
    bool DefaultValues = false,
    SqlStatement? Source = null) : SqlStatement;

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
    IReadOnlyList<CheckConstraint> CheckConstraints,
    // The PRIMARY KEY's CONSTRAINT name, if one was given (column- or table-level). ACE names the primary
    // key index after the constraint (verified: the scaffolder round-trips it), so it must be preserved.
    // When null (no name given), the engine picks its own stable fallback in TableCreator.
    string? PrimaryKeyName = null) : SqlStatement;

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

/// <summary>A join in a view: its kind, the verbatim ON condition, and the two tables it joins (from the
/// condition), which Access stores as the join row's Name1/Name2.</summary>
public sealed record ViewJoin(ViewJoinKind Kind, string Condition, string LeftAlias, string RightAlias);

/// <summary>An output column of a view: the verbatim expression text and its optional alias (stored in the
/// MSysQueries column row's Expression and Name1).</summary>
public sealed record ViewColumn(string Expression, string? Alias);

/// <summary>An ORDER BY key of a view: the verbatim sort expression and its direction (stored as an
/// MSysQueries <c>Attribute=0x0B</c> row — Expression = the sort column, Name1 = "d" for descending).</summary>
public sealed record ViewOrderBy(string Expression, bool Descending);

/// <summary>A view's decomposed SELECT (columns/tables/joins/where/group-by, all as verbatim text), which
/// Access stores as MSysQueries rows. A GROUP BY makes it a "totals" query (aggregate columns are ordinary
/// column rows; the group-by columns are separate rows). HAVING and ORDER BY are not stored yet.</summary>
public sealed record ViewDefinition(
    bool Distinct,
    IReadOnlyList<ViewColumn> Columns,
    IReadOnlyList<ViewSource> Tables,
    IReadOnlyList<ViewJoin> Joins,
    string? Where,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<ViewOrderBy> OrderBy,
    int? Top);

/// <summary>CREATE VIEW view [(fields)] AS select — a stored query, decomposed for byte-faithful storage.</summary>
public sealed record CreateViewStatement(
    string Name,
    IReadOnlyList<string> Columns,
    ViewDefinition Definition,
    string QuerySql) : SqlStatement;

/// <summary>A CREATE PROCEDURE parameter: a name and its declared Access SQL type name.</summary>
public sealed record ProcedureParameter(string Name, string TypeName);

/// <summary>CREATE PROCEDURE name [param datatype, …] AS select — a parameterized stored query. Stored like
/// a view (the decomposed <see cref="Definition"/>) plus a parameter row per declared parameter.</summary>
public sealed record CreateProcedureStatement(
    string Name,
    IReadOnlyList<ProcedureParameter> Parameters,
    ViewDefinition Definition,
    string QuerySql) : SqlStatement;

/// <summary>The kind of non-SELECT (action) CREATE PROCEDURE body.</summary>
public enum ProcedureActionKind { DataDefinition, Append }

/// <summary>One appended column of an INSERT procedure body: the target column and the verbatim value text.</summary>
public sealed record AppendColumn(string Column, string ValueExpression);

/// <summary>A CREATE PROCEDURE whose body is an action query (not a SELECT). A
/// <see cref="ProcedureActionKind.DataDefinition"/> body carries the whole <paramref name="DdlSql"/>
/// (CREATE/DROP TABLE); an <see cref="ProcedureActionKind.Append"/> body carries the
/// <paramref name="TargetTable"/> and its appended <paramref name="AppendColumns"/>.</summary>
public sealed record CreateActionProcedureStatement(
    string Name,
    ProcedureActionKind Kind,
    string? DdlSql,
    string? TargetTable,
    IReadOnlyList<AppendColumn>? AppendColumns) : SqlStatement;

/// <summary>One action of an ALTER TABLE statement (Access allows exactly one per statement).</summary>
public abstract record AlterTableAction;

/// <summary>ADD [COLUMN] field type … — add a new column (with its inline constraints).</summary>
public sealed record AddColumnAction(ColumnDefinition Column) : AlterTableAction;

/// <summary>ADD CONSTRAINT … FOREIGN KEY … — add a foreign key.</summary>
public sealed record AddForeignKeyAction(ForeignKeyConstraint ForeignKey) : AlterTableAction;

/// <summary>ADD CONSTRAINT … PRIMARY KEY (cols) — add the primary key.</summary>
public sealed record AddPrimaryKeyAction(string? Name, IReadOnlyList<string> Columns) : AlterTableAction;

/// <summary>ADD CONSTRAINT … UNIQUE (cols) — add a unique constraint.</summary>
public sealed record AddUniqueAction(UniqueConstraint Unique) : AlterTableAction;

/// <summary>ADD CONSTRAINT … CHECK (…) — add a check constraint.</summary>
public sealed record AddCheckAction(CheckConstraint Check) : AlterTableAction;

/// <summary>ALTER COLUMN field type[(size[,scale])] [NOT NULL|NULL] [DEFAULT expr] — change a column's data
/// type, and optionally its nullability (<see cref="NotNull"/>: true = NOT NULL, false = NULL, null = leave
/// as-is) and default.</summary>
public sealed record AlterColumnAction(string Field, string TypeName, int? Size, int? Scale, string? Default = null, bool? NotNull = null) : AlterTableAction;

/// <summary>ALTER COLUMN field SET DEFAULT expr — set (replace) a column's default, without retyping it.</summary>
public sealed record AlterColumnSetDefaultAction(string Field, string Default) : AlterTableAction;

/// <summary>ALTER COLUMN field DROP DEFAULT — remove a column's default, leaving its type and NOT NULL intact.</summary>
public sealed record AlterColumnDropDefaultAction(string Field) : AlterTableAction;

/// <summary>DROP COLUMN field — delete a column.</summary>
public sealed record DropColumnAction(string Field) : AlterTableAction;

/// <summary>DROP CONSTRAINT name — delete a (multi-field) index/constraint by name.</summary>
public sealed record DropConstraintAction(string Name) : AlterTableAction;

/// <summary>RENAME TO newName — rename the table itself.</summary>
public sealed record RenameTableAction(string NewName) : AlterTableAction;

/// <summary>RENAME COLUMN field TO newName — rename one of the table's columns.</summary>
public sealed record RenameColumnAction(string Field, string NewName) : AlterTableAction;

/// <summary>RENAME INDEX index TO newName — rename one of the table's indexes.</summary>
public sealed record RenameIndexAction(string Index, string NewName) : AlterTableAction;

/// <summary>ALTER TABLE table &lt;action&gt; — modifies an existing table's design.</summary>
public sealed record AlterTableStatement(string Table, AlterTableAction Action) : SqlStatement;

// DROP { TABLE table | INDEX index ON table | PROCEDURE procedure | VIEW view } — deletes an object.
public sealed record DropTableStatement(string Table) : SqlStatement;
public sealed record DropIndexStatement(string Index, string Table) : SqlStatement;
public sealed record DropProcedureStatement(string Procedure) : SqlStatement;
public sealed record DropViewStatement(string View) : SqlStatement;

/// <summary>A SET assignment: the target column (optionally table/alias-qualified for a multi-table UPDATE)
/// and the value expression.</summary>
public sealed record Assignment(string? Table, string Column, Expression Value) : SqlNode;

/// <summary>UPDATE over a table source (a single table, or a join — Access allows SET to touch columns in
/// several joined tables). <paramref name="From"/> is the join tree; the WHERE is an ordinary expression.</summary>
public sealed record UpdateStatement(
    TableReference From,
    IReadOnlyList<Assignment> Assignments,
    Expression? Where) : SqlStatement;

/// <summary>DELETE over a table source. <paramref name="TargetTable"/> is the <c>table.*</c> target (which
/// table's rows to delete in a join); null means the single table named by <paramref name="From"/>.</summary>
public sealed record DeleteStatement(
    string? TargetTable,
    TableReference From,
    Expression? Where) : SqlStatement;
