namespace LibRed.Sql.Ast;

/// <summary>Base type for top-level SQL statements.</summary>
public abstract record SqlStatement : SqlNode;

/// <summary>Which transaction-control action a <see cref="TransactionControlStatement"/> performs.</summary>
public enum TransactionControlKind { Begin, Commit, Rollback }

/// <summary>A transaction-control statement: <c>BEGIN</c>/<c>COMMIT</c>/<c>ROLLBACK [TRANSACTION|WORK]</c>.
/// Manages the transaction rather than running inside one.</summary>
public sealed record TransactionControlStatement(TransactionControlKind Kind) : SqlStatement;

/// <summary>A single item in a SELECT projection, optionally aliased. <paramref name="Written"/> is a column reference's
/// text as written when the item has no AS alias: ACE tells an item written twice from two different ones by it
/// (verified: <c>CompanyName, companyname</c> is one item twice, <c>CompanyName, [CompanyName]</c> two), which decides how
/// the outputs are named.</summary>
public sealed record SelectItem(Expression Value, string? Alias, string? Written = null) : SqlNode;

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
    Expression? Offset = null,
    /// <summary>
    /// <c>TOP n WITH TIES</c> (or <c>FETCH … WITH TIES</c>): <see cref="Top"/> also returns every further row whose
    /// ORDER BY keys equal the last one's. It needs an ORDER BY.
    /// </summary>
    bool WithTies = false,
    /// <summary>The <c>IN externaldatabase</c> after <see cref="Into"/>: the file the new table is made in, or null
    /// for the database the statement runs on.</summary>
    ExternalDatabase? IntoDatabase = null) : SqlStatement;

/// <summary>
/// A crosstab: <c>TRANSFORM value SELECT … GROUP BY … [ORDER BY …] PIVOT pivot [IN (…)]</c>. <paramref name="Select"/>
/// is the SELECT, its ORDER BY included, whose columns are the row headings — one row per group. Each value of
/// <paramref name="Pivot"/> is a further column, holding <paramref name="Value"/> computed over that row's group and
/// that pivot value; <paramref name="Headings"/>, when the PIVOT lists them, are those columns instead, whatever the
/// data holds. <paramref name="ValueAlias"/> is accepted and stored as ACE does, and names nothing (verified).
/// </summary>
public sealed record TransformStatement(
    Expression Value,
    string? ValueAlias,
    SelectStatement Select,
    Expression Pivot,
    IReadOnlyList<PivotHeading>? Headings) : SqlStatement;

/// <summary>A fixed crosstab heading from a PIVOT's IN list: the literal it matches and the text it was written as,
/// which names the column — a string's value, any other literal's own spelling, and nothing for Null (verified vs ACE:
/// <c>True</c> names a column <c>True</c>, <c>#2020-01-02#</c> one <c>#2020-01-02#</c>, Null one <c>&lt;&gt;</c>).</summary>
public sealed record PivotHeading(LiteralExpression Value, string Text);

/// <summary><c>EXECUTE|EXEC procedure [arg, …]</c> — invokes a stored procedure/query by name, passing
/// positional argument values that bind to its declared parameters (in declaration order).</summary>
public sealed record ExecuteStatement(string Procedure, IReadOnlyList<Expression> Arguments) : SqlStatement;

/// <summary>Jet's conditional DDL <c>IF [NOT] EXISTS (&lt;condition&gt;) THEN &lt;then&gt;</c> — EF emits it for
/// idempotent migrations (e.g. create the history table only if it isn't already in INFORMATION_SCHEMA.TABLES).
/// <paramref name="Then"/> runs when the (possibly correlated) condition subquery returns a row — or, when
/// <paramref name="Negated"/>, when it returns none.</summary>
public sealed record IfThenStatement(bool Negated, SqlStatement Condition, SqlStatement Then) : SqlStatement;

/// <summary>A FROM-less <c>SELECT @@IDENTITY</c> / <c>SELECT @@ROWCOUNT</c> (a comma list of system
/// variables only). ACE allows these without a FROM clause; they yield a single row. The projection
/// items' <see cref="SelectItem.Value"/> are all <see cref="SystemVariableExpression"/>.</summary>
public sealed record SystemVariableSelectStatement(IReadOnlyList<SelectItem> Projection) : SqlStatement;

public enum SetOperator { Union, UnionAll, Intersect, Except }

/// <summary>
/// A set operation combining two queries. UNION dedupes, UNION ALL keeps duplicates,
/// INTERSECT keeps rows in both, EXCEPT keeps rows in the left not in the right.
/// </summary>
/// <remarks>
/// <paramref name="OrderBy"/>, <paramref name="Top"/> and <paramref name="Offset"/> order and page the
/// <b>result of the whole operation</b>, which is where the grammar puts them and where the standard defines
/// them — an operand cannot carry them unless it is parenthesised, in which case it is a nested query
/// expression with its own. Only the outermost node of a chain ever carries them: <c>A UNION B UNION C ORDER BY
/// x</c> is one query expression, so the ordering belongs to the tree, not to any pair within it.
/// </remarks>
public sealed record SetOperationStatement(
    SqlStatement Left,
    SetOperator Operator,
    SqlStatement Right,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    Expression? Top = null,
    Expression? Offset = null,
    // FETCH … WITH TIES, as SelectStatement.WithTies.
    bool WithTies = false) : SqlStatement;

/// <summary>
/// A table value constructor used as a query rather than as an INSERT's VALUES clause —
/// <c>VALUES (1), (2)</c> — yielding one row per list. EF Core emits it as an operand of a set operation for
/// an inline collection.
/// </summary>
/// <remarks>
/// The rows carry no column names of their own: in a set operation names come from the leading query per SQL, and
/// as a derived table the column list names them (<c>AS t(a, b)</c>). Row values may reference outer columns, so the
/// expressions are evaluated per outer row rather than once. <c>DEFAULT</c> is rejected here — the standard
/// permits it only inside an INSERT.
/// </remarks>
public sealed record ValuesStatement(IReadOnlyList<IReadOnlyList<Expression>> Rows) : SqlStatement;

/// <summary>
/// An INSERT — Access's two append-query forms.
/// </summary>
/// <param name="Table">The table the rows are appended to.</param>
/// <param name="Columns">The target columns, in the order the values are given; empty for DEFAULT VALUES.</param>
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
    string? Default = null,
    /// <summary>WITH COMPRESSION / WITH COMP — Text and Memo only.</summary>
    bool Compressed = false,
    /// <summary>A calculated column's expression, from the LibRed-only <c>AS (…)</c> clause, kept as source
    /// text because that text is what goes on disk and what the engine re-evaluates. Null for an ordinary
    /// column.</summary>
    string? Calculated = null,
    /// <summary>A trailing <c>IDENTITY [(seed [, increment])]</c> attribute, or null when the column has none.
    /// The last one written wins when several are.</summary>
    IdentitySpec? Identity = null);

/// <summary>ACE's <c>IDENTITY [(seed [, increment])]</c> column attribute. It makes a Long column an AutoNumber
/// counting from its seed by its increment — each 1 when omitted, replacing any the type declared — and is
/// ignored on every other type. (Named for the SQL clause, not for <see cref="System.Attribute"/>, which is
/// why it is not spelled with that suffix.)</summary>
public sealed record IdentitySpec(int? Seed, int? Increment);

/// <summary>Referential action for a foreign key's ON DELETE / ON UPDATE clause. Jet records only
/// enforce + cascade-update + cascade-delete, so NoAction/SetNull/SetDefault collapse to "no cascade".</summary>
public enum ReferentialAction { NoAction, Cascade, SetNull, SetDefault }

/// <summary>A FOREIGN KEY constraint (table-level, or a column-level REFERENCES): the child columns,
/// the referenced (parent) table and its columns, the ON DELETE / ON UPDATE actions, and whether the
/// FOREIGN KEY NO INDEX modifier was given (suppresses the backing index). An empty
/// <see cref="ReferencedColumns"/> means <c>REFERENCES table</c> with no column list: the parent's primary key.</summary>
public sealed record ForeignKeyConstraint(
    string? Name,
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    ReferentialAction OnDelete,
    ReferentialAction OnUpdate,
    bool NoIndex = false,
    int DeclaredAfter = 0);

/// <summary>A UNIQUE constraint (table-level, or a column-level UNIQUE) over one or more columns.
/// <para><b>DeclaredAfter</b> is how many of the statement's columns and key constraints (PRIMARY KEY, UNIQUE,
/// FOREIGN KEY) it declares before this constraint. It carries the constraint's place in the CREATE TABLE element
/// list through to the storage layer, which numbers the indexes and lays out their usage-map rows in that order.
/// </para></summary>
public sealed record UniqueConstraint(string? Name, IReadOnlyList<string> Columns, int DeclaredAfter = 0);

/// <summary>A CHECK constraint: an optional name and the raw expression text.
/// <para><b>ColumnsBefore</b> is, in CREATE TABLE, how many of the statement's columns it declares before this
/// constraint — the ones it may name, as ACE resolves it where it is written. ALTER TABLE ADD CONSTRAINT leaves it
/// unused: the table's columns all exist by then.</para></summary>
public sealed record CheckConstraint(string? Name, string Expression, int ColumnsBefore = 0)
{
    /// <summary>The qualifier a stored CHECK names the row it is checking by: U+FFFE, which ACE writes where the
    /// statement qualified a column with the table's own name inside a subquery (verified).</summary>
    public static readonly string RowQualifier = ((char)0xFFFE).ToString();

    /// <summary>ACE's refusal of a CHECK it cannot take, whatever the reason: one that does not parse, names what it
    /// cannot resolve, or spells a qualifier in a way ACE does not accept (verified).</summary>
    public static Parsing.SqlParseException Refusal() => new("Syntax error in CHECK constraint clause.");
}

public sealed record CreateTableStatement(
    string Table,
    IReadOnlyList<ColumnDefinition> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeyConstraint> ForeignKeys,
    IReadOnlyList<UniqueConstraint> UniqueConstraints,
    IReadOnlyList<CheckConstraint> CheckConstraints,
    // The PRIMARY KEY's CONSTRAINT name, if one was given (column- or table-level). ACE names the primary
    // key index after the constraint (verified: the scaffolder round-trips it), so it must be preserved.
    // When null (no name given), the engine picks its own stable fallback in SchemaEditor.
    string? PrimaryKeyName = null,
    // Where the PRIMARY KEY constraint is declared — see UniqueConstraint.DeclaredAfter.
    int PrimaryKeyDeclaredAfter = 0) : SqlStatement;

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

/// <summary>A view's decomposed SELECT (columns/tables/joins/where/group-by/having, all as verbatim text),
/// which Access stores as MSysQueries rows. A GROUP BY makes it a "totals" query (aggregate columns are
/// ordinary column rows; the group-by columns are separate rows), and <paramref name="Having"/> is that
/// query's own filter over the groups, stored as its own <c>Attribute=0x0A</c> row.
/// <paramref name="TopPercent"/> makes <paramref name="Top"/> a percentage, and <paramref name="OwnerAccess"/> is
/// WITH OWNERACCESS OPTION, which a stored query keeps. A <paramref name="Union"/> body's two
/// <paramref name="Tables"/> are its segments' verbatim text — what precedes its last UNION and the term after it —
/// and <paramref name="Distinct"/> is UNION as against UNION ALL. <paramref name="Database"/> is the FROM's
/// <c>IN externaldatabase</c>, and <paramref name="Crosstab"/> makes the body a TRANSFORM's SELECT.
/// <paramref name="AllFields"/> is a bare <c>*</c> in the select list — <paramref name="Columns"/> then holds only the
/// other items — and <paramref name="DistinctRow"/> is DISTINCTROW.</summary>
public sealed record ViewDefinition(
    bool Distinct,
    IReadOnlyList<ViewColumn> Columns,
    IReadOnlyList<ViewSource> Tables,
    IReadOnlyList<ViewJoin> Joins,
    string? Where,
    IReadOnlyList<string> GroupBy,
    string? Having,
    IReadOnlyList<ViewOrderBy> OrderBy,
    int? Top,
    bool TopPercent = false,
    bool OwnerAccess = false,
    bool Union = false,
    ExternalDatabase? Database = null,
    ViewCrosstab? Crosstab = null,
    bool AllFields = false,
    bool DistinctRow = false);

/// <summary>What a crosstab adds to its SELECT, each as its verbatim text: the TRANSFORM <paramref name="Value"/> and
/// its alias, the <paramref name="Pivot"/> field, and <paramref name="PivotColumn"/> — the PIVOT clause's whole text,
/// its IN list included, which ACE stores as one column row while the field alone is a GROUP BY row (verified).</summary>
public sealed record ViewCrosstab(string Value, string? ValueAlias, string Pivot, string PivotColumn);

/// <summary>CREATE VIEW view [(fields)] AS select — a stored query, decomposed for byte-faithful storage.</summary>
public sealed record CreateViewStatement(
    string Name,
    IReadOnlyList<string> Columns,
    ViewDefinition Definition,
    string QuerySql) : SqlStatement;

/// <summary>A CREATE PROCEDURE parameter: a name and its declared Access SQL type, with the
/// <paramref name="Size"/> and <paramref name="Scale"/> it declares. The size also decides the type code —
/// <c>Text(50)</c> is a Text parameter where a bare <c>Text</c> is a memo — and both are stored alongside it.</summary>
/// <summary>A declared parameter. <paramref name="Name"/> is the name it binds by; <paramref name="Stored"/> is the
/// spelling Access stores, which keeps a bracketed name's brackets.</summary>
public sealed record ProcedureParameter(string Name, string TypeName, int? Size = null, int? Scale = null, string? Stored = null);

/// <summary>CREATE PROCEDURE name [param datatype, …] AS select — a parameterized stored query. Stored like
/// a view (the decomposed <see cref="Definition"/>) plus a parameter row per declared parameter.</summary>
public sealed record CreateProcedureStatement(
    string Name,
    IReadOnlyList<ProcedureParameter> Parameters,
    ViewDefinition Definition,
    string QuerySql) : SqlStatement;

/// <summary>The kind of non-SELECT (action) CREATE PROCEDURE body.</summary>
public enum ProcedureActionKind { DataDefinition, Append, Update, Delete, MakeTable }

/// <summary>One column/value pair of an action procedure body: an appended column of an INSERT and its
/// verbatim value text, or one assignment of an UPDATE (whose <paramref name="Column"/> is table-qualified
/// when the update runs over a join). Access stores both the same way.</summary>
public sealed record AppendColumn(string Column, string ValueExpression);

/// <summary>
/// A CREATE PROCEDURE whose body is an action query (not a plain SELECT). A
/// <see cref="ProcedureActionKind.DataDefinition"/> body carries the whole <paramref name="DdlSql"/>
/// (CREATE/DROP TABLE) and nothing else — Access stores it verbatim. Every other kind carries its sources,
/// joins and WHERE in <paramref name="Body"/>, exactly as a view carries them, plus what its own kind needs:
/// <paramref name="AppendColumns"/> are an INSERT's columns or an UPDATE's assignments,
/// <paramref name="TargetTable"/> is the table an INSERT or a make-table writes into, and
/// <paramref name="DeleteTarget"/> is the <c>table.*</c> a DELETE names when it names one.
/// <paramref name="OwnerAccess"/> is WITH OWNERACCESS OPTION, which the stored query keeps.
/// <paramref name="TargetDatabase"/> is the <c>IN externaldatabase</c> a make-table's new table is made in.
/// </summary>
public sealed record CreateActionProcedureStatement(
    string Name,
    ProcedureActionKind Kind,
    string? DdlSql,
    string? TargetTable,
    IReadOnlyList<AppendColumn>? AppendColumns,
    ViewDefinition? Body = null,
    string? DeleteTarget = null,
    IReadOnlyList<ProcedureParameter>? Parameters = null,
    bool OwnerAccess = false,
    ExternalDatabase? TargetDatabase = null) : SqlStatement;

/// <summary>One action of an ALTER TABLE statement (Access allows exactly one per statement).</summary>
public abstract record AlterTableAction;

/// <summary>ADD [COLUMN] field type … — add a new column with its inline constraints: a PRIMARY KEY
/// (<see cref="ColumnDefinition.PrimaryKey"/>, named <see cref="PrimaryKeyName"/> when a CONSTRAINT name was given),
/// a <see cref="Unique"/> constraint and a column-level <see cref="References"/>, each null when absent.</summary>
public sealed record AddColumnAction(
    ColumnDefinition Column,
    ForeignKeyConstraint? References = null,
    UniqueConstraint? Unique = null,
    string? PrimaryKeyName = null) : AlterTableAction;

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
public sealed record AlterColumnAction(string Field, string TypeName, int? Size, int? Scale, string? Default = null, bool? NotNull = null,
    IdentitySpec? Identity = null) : AlterTableAction;

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