using LibRed.Catalog;
using LibRed.Engine.Planning;
using LibRed.Engine.Schema;
using LibRed.IO;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using LibRed.Storage;

namespace LibRed.Engine.Execution;

/// <summary>
/// Executes non-query statements (DDL/DML) against the storage layer: CREATE TABLE and INSERT.
/// Returns the number of affected rows (0 for DDL).
/// </summary>
internal sealed class StatementExecutor(JetDatabase database, IReadOnlyDictionary<string, object?>? parameters, ISqlParser parser, SessionState? session = null)
{
    private readonly JetDatabase _database = database;
    private readonly ParameterBag _parameters = new(parameters);
    private readonly ISqlParser _parser = parser;
    private readonly SessionState? _session = session;
    // For evaluating VALUES expressions (literals, parameters, and any scalar subqueries).
    private readonly QueryExecutor _scalarRunner = new(database, parameters, session);

    // What the constraint checks look up, resolved once per statement rather than once per row: CHECK expressions
    // parsed, each table's enforced relationships from either end, and each relationship's identity.
    private readonly Dictionary<string, Expression> _checkExpressions = [];
    private readonly Dictionary<string, ForeignKey[]> _foreignKeysOf = new([], StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ForeignKey[]> _referencesTo = new([], StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ForeignKey, RelationshipIdentity?> _identities = [];
    private readonly Dictionary<ForeignKey, RelationshipEnds> _ends = [];

    public int Execute(SqlStatement statement) => statement switch
    {
        CreateTableStatement create => ExecuteCreateTable(create),
        CreateIndexStatement createIndex => ExecuteCreateIndex(createIndex),
        CreateViewStatement createView => ExecuteCreateView(createView),
        CreateProcedureStatement createProc => ExecuteCreateProcedure(createProc),
        CreateActionProcedureStatement actionProc => ExecuteCreateActionProcedure(actionProc),
        AlterTableStatement alter => ExecuteAlterTable(alter),
        DropIndexStatement dropIndex => DropIndex(dropIndex.Table, dropIndex.Index),
        DropTableStatement dropTable => DropTable(dropTable.Table),
        DropViewStatement dropView => DropQueryObject(dropView.View),
        DropProcedureStatement dropProc => DropQueryObject(dropProc.Procedure),
        SelectStatement { Into: not null } makeTable => ExecuteSelectInto(makeTable),
        InsertStatement insert => ExecuteInsert(insert),
        UpdateStatement update => ExecuteUpdate(update),
        DeleteStatement delete => ExecuteDelete(delete),
        _ => throw new NotSupportedException($"{statement.GetType().Name} cannot be executed as a non-query."),
    };

    private int ExecuteCreateTable(CreateTableStatement statement)
    {
        var columns = statement.Columns.Select(AccessTypeMapper.ToColumnSpec).ToList();
        IReadOnlyList<string>? primaryKey = statement.PrimaryKey.Count > 0 ? statement.PrimaryKey : null;

        var relationships = statement.ForeignKeys.Select(fk => ToRelationshipSpec(statement.Table, fk)).ToList();

        // An unnamed constraint's generated name has to fit the 64 characters a name may have, however long the
        // table's own name is.
        string Generated(string prefix, int i)
        {
            string suffix = $"_{i}";
            int room = JetName.MaxLength - prefix.Length - 1 - suffix.Length;
            return $"{prefix}_{(statement.Table.Length > room ? statement.Table[..room] : statement.Table)}{suffix}";
        }

        var uniques = statement.UniqueConstraints.Select((u, i) => new UniqueIndexSpec(
            Name: u.Name ?? Generated("UQ", i),
            Columns: u.Columns,
            DeclaredAfter: u.DeclaredAfter)).ToList();

        var defaults = statement.Columns
            .Where(c => c.Default is not null)
            .Select(c => (c.Name, DefaultSql: c.Default!))
            .ToList();

        var checks = statement.CheckConstraints
            .Select((ck, i) => (Name: ck.Name ?? Generated("CK", i), ck.Expression,
                Reads: (IReadOnlyList<(string Table, IReadOnlyList<string> Columns)>)CheckReads(
                    statement.Table, [.. statement.Columns.Take(ck.ColumnsBefore).Select(col => col.Name)], ck.Expression)))
            .ToList();

        _database.CreateTable(statement.Table, columns, primaryKey, relationships, uniques, defaults, checks,
            statement.PrimaryKeyName, statement.PrimaryKeyDeclaredAfter);
        return 0;
    }

    /// <summary>
    /// What a CHECK on <paramref name="table"/> reads, for the object ACE records it with
    /// (<c>SchemaEditor.CheckReadsText</c>): its own table first, then every table a subquery names, in the order
    /// the expression names them; each with the columns read of it, in <paramref name="ownColumns"/>' order or the
    /// table's own, each once. A column takes the innermost table in scope that has it, or the one its qualifier
    /// names. Measured by whole-file diff (<c>WholeFileParityProbeTest</c>): <c>Len([Note]) &lt; [Level] + 20 OR
    /// [Level] &gt; 0</c> on <c>Setting</c> reads <c>Level, Note</c>; a <c>COUNT(*)</c> subquery over
    /// <c>Customer</c> lists it with no column.
    /// <para>The same walk refuses what ACE refuses when the check is created, in CREATE TABLE and ALTER TABLE alike
    /// (verified): an expression that does not parse; a name no scope has, even in an IIf branch that never runs, as
    /// ACE resolves names rather than evaluating; inside a subquery, an unqualified name only the row being checked
    /// has — the row is reached there only through its qualifier (<see cref="CheckConstraint.RowQualifier"/>); a
    /// subquery over a table that does not exist; and an aggregate outside a subquery. <paramref name="ownColumns"/>
    /// are the columns the check may name: in CREATE TABLE, those declared before it.</para>
    /// </summary>
    private List<(string Table, IReadOnlyList<string> Columns)> CheckReads(
        string table, IReadOnlyList<string> ownColumns, string expression)
    {
        Expression parsed;
        try
        {
            parsed = ParsedCheck(expression);
        }
        catch (SqlParseException)
        {
            throw CheckConstraint.Refusal();
        }

        List<(string Name, IReadOnlyList<string> Columns, HashSet<string> Read)> tables =
            [(table, ownColumns, new HashSet<string>(StringComparer.OrdinalIgnoreCase))];

        int TableOf(string name)
        {
            int at = tables.FindIndex(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) return at;
            TableDefinition definition = _database.Catalog.FindTable(name) ?? throw CheckConstraint.Refusal();
            HashSet<string> read = [with(StringComparer.OrdinalIgnoreCase)];
            tables.Add((definition.Name, [.. definition.Columns.Select(c => c.Name)], read));
            return tables.Count - 1;
        }

        // A table in scope as FindColumns resolves against it: under its alias, or its name where it has none.
        SourceTable Source(int at, string? alias)
        {
            string name = alias ?? tables[at].Name;
            return new SourceTable(name, null, [.. tables[at].Columns.Select(c => new OutputColumn(name, c))], null);
        }

        // Each level of scope holds its sources and which of the tables each is; the innermost level comes first.
        void Scan(object root, List<(List<SourceTable> Sources, List<int> Tables)> scopes)
        {
            _ = Planning.ColumnPruning.Walk(root, node =>
            {
                switch (node)
                {
                    case SelectStatement select when !ReferenceEquals(select, root):
                        (List<SourceTable> Sources, List<int> Tables) level = ([], []);
                        _ = Planning.ColumnPruning.Walk(select.From, from =>
                        {
                            if (from is NamedTable named)
                            {
                                int at = TableOf(named.Name);
                                level.Sources.Add(Source(at, named.Alias));
                                level.Tables.Add(at);
                            }
                            return from is not SelectStatement;
                        });
                        Scan(select, [level, .. scopes]);
                        return false;
                    case ColumnReference column:
                        for (int depth = 0; depth < scopes.Count; depth++)
                            if (FindColumns(scopes[depth].Sources, column.Table, column.Column) is [var (found, _), ..])
                            {
                                if (depth == scopes.Count - 1 && depth > 0 && column.Table is null)
                                    throw CheckConstraint.Refusal();
                                tables[scopes[depth].Tables[found]].Read.Add(column.Column);
                                return false;
                            }
                        return ExpressionEvaluator.IsNiladicFunction(column) ? false : throw CheckConstraint.Refusal();
                    case FunctionCall call when scopes.Count == 1 && QueryPlanner.IsAggregate(call.Name):
                        throw CheckConstraint.Refusal();
                    default:
                        return true;
                }
            });
        }

        Scan(parsed, [([Source(0, CheckConstraint.RowQualifier)], [0])]);
        return [.. tables.Select(t => (t.Name, (IReadOnlyList<string>)[.. t.Columns.Where(t.Read.Contains)]))];
    }

    private static readonly System.Text.RegularExpressions.Regex BareWord =
        new(@"^[A-Za-z_][A-Za-z_0-9]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Parses a column DEFAULT into an expression. In Access an **unquoted single word** is a LITERAL
    /// STRING (verified vs ACE: <c>DEFAULT Unknown</c> → "Unknown", <c>DEFAULT K</c> → "K" even when K is a
    /// column) — it is never a column reference. It is what the grammar reads it as only when that is a value: a
    /// constant — <c>Null</c> stores NULL, and <c>True</c>, <c>Yes</c>, <c>On</c>, <c>False</c>, <c>No</c> and
    /// <c>Off</c> store -1 and 0, as the column's type has them (verified vs ACE, into LONG, TEXT, YESNO, DATETIME and
    /// DOUBLE) — or the niladic <c>Now</c>. A bracketed <c>[X]</c> stays a column reference (rejected in a default); a
    /// quoted literal, function call, or compound expression parses normally. (A multi-word bare default is a syntax
    /// error at CREATE, matching ACE.) A subquery is refused: a default reads no table or query, and ACE refuses one.</summary>
    private Expression ParseDefaultExpression(string text)
    {
        string t = PropertyBlob.DefaultExpression(text);
        if (!BareWord.IsMatch(t))
        {
            Expression expression = _parser.ParseExpression(t);
            _ = Planning.ColumnPruning.Walk(expression, node => node is ScalarSubquery or ExistsExpression or InSubqueryExpression
                ? throw new SqlParseException($"A default value cannot read a table or query: '{text}'.")
                : true);
            return expression;
        }
        Expression? parsed;
        try { parsed = _parser.ParseExpression(t); }
        catch (SqlParseException) { parsed = null; }   // a word the grammar reserves is text too
        return parsed is LiteralExpression || parsed is ColumnReference c && ExpressionEvaluator.IsNiladicFunction(c)
            ? parsed
            : new LiteralExpression(t);
    }

    /// <summary>Rejects an insert that leaves a NOT NULL (Required) column null after defaults are applied —
    /// matching Access, which raises "You must enter a value in the 'Table.Column' field." AutoNumber columns
    /// are exempt: they are assigned during the write, not supplied here.</summary>
    private static void EnforceRequired(string table, IReadOnlyList<ColumnDef> columns, object?[] values)
    {
        foreach (ColumnDef column in columns)
            if (!column.IsNullable && !column.IsAutoNumber && values[column.Index] is null)
                throw new InvalidOperationException($"You must enter a value in the '{table}.{column.Name}' field.");
    }

    /// <summary>Rejects a row written against any of the table's CHECK constraints. Called once the row is written, so
    /// a subquery over the table sees it, one row at a time in the order the statement writes them — and for a deleted
    /// row once it is gone, against a row whose every column is Null — as ACE does (all verified). A CHECK is violated
    /// only when its expression is explicitly FALSE; NULL/unknown passes. A row a cascade wrote is refused in ACE's
    /// words for a cascade, and an expression that cannot be evaluated — a division by zero, an overflow, a type
    /// mismatch — with ACE's error for that rather than the evaluator's own.</summary>
    private void EnforceCheckConstraints(TableDefinition definition, object?[] values, bool cascaded)
    {
        if (definition.CheckConstraints.Count == 0) return;
        // A runner of its own, not the statement's: that one keeps a subquery's answer once it has worked it out, as
        // a query can, and this statement has written rows since — a CHECK reading the table has to see them.
        // The row is named as ACE names it, by CheckConstraint.RowQualifier — not by the table's name, which inside a
        // subquery could be the subquery's own table.
        List<OutputColumn> row = [.. definition.Columns.Select(c => OutputColumn.Of(CheckConstraint.RowQualifier, definition, c))];
        var evaluator = new ExpressionEvaluator(new EvalScope(row, values, null),
            new QueryExecutor(_database, parameters, _session), _parameters, _session);
        foreach (var (name, expression) in definition.CheckConstraints)
        {
            Expression parsed = ParsedCheck(expression);
            object? holds;
            try
            {
                holds = evaluator.Evaluate(parsed);
            }
            catch (Exception e) when (e is ArithmeticException or InvalidCastException or ArgumentException or InvalidOperationException)
            {
                throw new InvalidOperationException($"Error evaluating {name} CHECK constraint. {name}", e);
            }
            if (holds is false)
                throw new InvalidOperationException(cascaded
                    ? $"Cannot perform cascading operation. The value entered is prohibited by the validation rule '{name}' set for '{definition.Name}'."
                    : $"One or more values are prohibited by the validation rule '{name}' set for '{definition.Name}'. " +
                      "Enter a value that the expression for this field can accept.");
        }
    }

    private Expression ParsedCheck(string expression)
    {
        if (!_checkExpressions.TryGetValue(expression, out Expression? parsed))
            _checkExpressions[expression] = parsed = _parser.ParseExpression(expression);
        return parsed;
    }

    /// <summary>The relationship a parsed foreign key creates on <paramref name="childTable"/>, for CREATE TABLE and
    /// ALTER TABLE alike. A <c>REFERENCES table</c> with no column list names only its child columns; the table
    /// creator resolves it to the parent's primary key.</summary>
    private static RelationshipSpec ToRelationshipSpec(string childTable, ForeignKeyConstraint fk) => new(
        Name: fk.Name ?? DefaultRelationshipName(childTable, fk),
        ReferencedTable: fk.ReferencedTable,
        Columns: fk.ReferencedColumns.Count == 0
            ? RelationshipSpec.ChildColumnsOnly(fk.Columns)
            : RelationshipSpec.PairColumns(fk.ReferencedTable, fk.Columns, fk.ReferencedColumns),
        IsEnforced: true,
        CascadeUpdate: fk.OnUpdate == ReferentialAction.Cascade,
        CascadeDelete: fk.OnDelete == ReferentialAction.Cascade,
        NoIndex: fk.NoIndex,
        DeleteSetNull: fk.OnDelete == ReferentialAction.SetNull,
        UpdateSetNull: fk.OnUpdate == ReferentialAction.SetNull,
        ReferencesPrimaryKey: fk.ReferencedColumns.Count == 0,
        DeclaredAfter: fk.DeclaredAfter);

    /// <summary>Access-style fallback name when the constraint is unnamed: "childparent".</summary>
    private static string DefaultRelationshipName(string childTable, ForeignKeyConstraint fk) =>
        $"{fk.ReferencedTable}{childTable}";

    /// <summary>
    /// Rejects an insert whose foreign-key columns reference a non-existent parent row. NULL handling follows
    /// ACE's **MATCH FULL** rule (verified vs ACE, and unlike SQL Server's MATCH SIMPLE): a composite FK is
    /// skipped only when **every** column is null; a **partial** null (some null, some not) can never match a
    /// parent key and is rejected. Only enforced relationships (grbit without "don't enforce") are checked.
    /// <paramref name="update"/> says the row is an UPDATE's rather than an INSERT's, for the message.
    /// </summary>
    private void EnforceReferentialIntegrity(string childTable, Table table, object?[] values, bool update = false)
    {
        foreach (ForeignKey fk in ForeignKeysOf(childTable))
        {
            RelationshipEnds ends = EndsOf(fk);
            // All FK columns null → the FK is not applied to this row.
            if (ForeignKeyValue(values, ends.ChildColumns, out bool partialNull) is not { } target) continue;

            // A **self-referencing** FK: a fully-specified row that points at its own key (the root of a
            // required self-ref, e.g. Inverse1Id = Id) satisfies the FK even though it isn't on disk yet — the
            // parent scan runs before the insert. Access allows this (EF's ComplexNavigations seed relies on it).
            if (!partialNull
                && string.Equals(fk.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase)
                && KeyEquals(values, ends.ParentColumns, target))
                continue;

            if (partialNull || !ParentRowExists(ends, target))
                throw new InvalidOperationException(
                    $"{(update ? "UPDATE of" : "INSERT into")} '{childTable}' violates foreign key '{fk.Name}': "
                    + $"no matching row in '{fk.ReferencedTable}'.");

            // Inside a transaction, finding the parent now is not enough: reading it writes no page, so the
            // commit's page-conflict check cannot see the dependency, and a concurrent transaction is free to
            // delete that parent and commit — each having checked the other's precondition, leaving a child row
            // referencing nothing. The condition is re-checked when this transaction commits, once per
            // relationship and key however many rows relied on it.
            DependOnRelationship(fk, target,
                $"Transaction conflict on foreign key '{fk.Name}': the row in '{fk.ReferencedTable}' that "
                + $"'{childTable}' was checked against no longer exists.");
        }
    }

    /// <summary>
    /// Makes the transaction's commit re-check that no row of <paramref name="fk"/>'s child table holds
    /// <paramref name="key"/> without a parent row holding it too. Both ends of a relationship need it, because
    /// each end's check reads a row the other end's transaction can change and reading writes no page: a child
    /// write found its parent, or a parent delete or key change found no children, and a concurrent transaction
    /// can commit the opposite write in between. The condition is the same from either end, so it is held once per
    /// relationship and key however many rows relied on it.
    /// </summary>
    private void DependOnRelationship(ForeignKey fk, object?[] key, string conflict)
    {
        var dependency = new ForeignKeyDependency(
            IdentityOf(fk) ?? throw new InvalidOperationException(
                $"Relationship '{fk.Name}' names a table or column that does not exist."),
            key);
        _database.DependOn(() => ForeignKeyDependencyHolds(dependency), conflict, key: dependency);
    }

    /// <summary>
    /// Whether the parent a transaction's write relied on is still there, or no longer needed. It is needed only
    /// while the relationship survives and a child row still holds the key: later statements may delete the child,
    /// move its key, cascade it, or drop the relationship. The relationship is found by name and is the same one
    /// only while it joins the same tables over the same columns, whatever they are called by now. The parent is
    /// looked for first, since it is almost always still there.
    /// </summary>
    private bool ForeignKeyDependencyHolds(ForeignKeyDependency dependency)
    {
        if (CurrentRelationship(dependency.Relationship) is not { } relationship) return true;
        RelationshipEnds ends = Ends(relationship); // as the catalog holds them now, not when the write was made
        return ParentRowExists(ends, dependency.Key)
            || !RowsHoldingKey(ends.Child, ends.ChildColumns, dependency.Key).Any();
    }

    /// <summary>The enforced relationship <paramref name="identity"/> identifies, as the catalog holds it now; null
    /// once it has been dropped.</summary>
    private ForeignKey? CurrentRelationship(RelationshipIdentity identity) =>
        _database.Catalog.Relationships.FirstOrDefault(r => r.IsEnforced
            && string.Equals(r.Name, identity.Name, StringComparison.OrdinalIgnoreCase)
            && identity.Equals(Identify(r)));

    /// <summary>The foreign key a row holds, on ACE's **MATCH FULL** terms (verified vs ACE, and unlike SQL
    /// Server's MATCH SIMPLE): null when every column is null — the row references nothing and the relationship is
    /// not applied to it — and <paramref name="partialNull"/> when only some are, a key that can never match a full
    /// parent key and so breaks the relationship (ACE gives the same "a related record is required" error).</summary>
    private static object?[]? ForeignKeyValue(object?[] row, int[] columns, out bool partialNull)
    {
        object?[] key = [.. columns.Select(c => row[c])];
        int nulls = key.Count(v => v is null);
        partialNull = nulls > 0 && nulls < key.Length;
        return nulls == key.Length ? null : key;
    }

    /// <summary>A relationship's two tables, and the positions its columns occupy in each one's rows.</summary>
    private sealed record RelationshipEnds(Table Child, int[] ChildColumns, Table Parent, int[] ParentColumns);

    /// <summary>The relationship's two ends as the catalog holds them now.</summary>
    private RelationshipEnds Ends(ForeignKey fk)
    {
        Table child = _database.OpenTable(fk.Table);
        Table parent = _database.OpenTable(fk.ReferencedTable);
        return new RelationshipEnds(
            child, [.. fk.Columns.Select(c => child.Definition.RequireColumn(c.Column).Index)],
            parent, [.. fk.Columns.Select(c => parent.Definition.RequireColumn(c.ReferencedColumn).Index)]);
    }

    /// <summary><see cref="Ends"/>, once per relationship per statement — what every row the statement writes
    /// checks against. A commit-time check resolves them afresh, since later statements may have changed them.</summary>
    private RelationshipEnds EndsOf(ForeignKey fk)
    {
        if (!_ends.TryGetValue(fk, out RelationshipEnds? ends))
            _ends[fk] = ends = Ends(fk);
        return ends;
    }

    /// <summary>A relationship as the file identifies it rather than by its names: the definition pages of the
    /// tables it joins and the ids of its columns, which renaming a table or a column leaves alone. Null when one
    /// of them no longer exists.</summary>
    private RelationshipIdentity? Identify(ForeignKey fk)
    {
        if (_database.Catalog.FindTable(fk.Table) is not { } child
            || _database.Catalog.FindTable(fk.ReferencedTable) is not { } parent)
            return null;
        var childColumns = new int[fk.Columns.Count];
        var parentColumns = new int[fk.Columns.Count];
        for (int i = 0; i < fk.Columns.Count; i++)
        {
            if (child.FindColumn(fk.Columns[i].Column) is not { } childColumn
                || parent.FindColumn(fk.Columns[i].ReferencedColumn) is not { } parentColumn)
                return null;
            childColumns[i] = childColumn.ColumnId;
            parentColumns[i] = parentColumn.ColumnId;
        }
        return new RelationshipIdentity(fk.Name, child.DefinitionPage, parent.DefinitionPage, childColumns, parentColumns);
    }

    /// <summary>A relationship's name, and what it joins as <see cref="Identify"/> finds it: equal only for the same
    /// relationship, so one dropped and added again under its name over other columns is another.</summary>
    private sealed record RelationshipIdentity(
        string Name, int ChildTable, int ParentTable, int[] ChildColumns, int[] ParentColumns)
    {
        public bool Equals(RelationshipIdentity? other) => other is not null
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && ChildTable == other.ChildTable && ParentTable == other.ParentTable
            && ChildColumns.AsSpan().SequenceEqual(other.ChildColumns)
            && ParentColumns.AsSpan().SequenceEqual(other.ParentColumns);

        public override int GetHashCode() =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Name), ChildTable, ParentTable);
    }

    /// <summary>A parent a transaction's write relied on: the relationship, and the key it was checked for. Equal
    /// values are one dependency, which is what lets the channel hold it once.</summary>
    private sealed record ForeignKeyDependency(RelationshipIdentity Relationship, object?[] Key)
    {
        public bool Equals(ForeignKeyDependency? other) => other is not null
            && Relationship.Equals(other.Relationship)
            && System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(Key, other.Key);

        public override int GetHashCode() => HashCode.Combine(
            Relationship, System.Collections.StructuralComparisons.StructuralEqualityComparer.GetHashCode(Key));
    }

    /// <summary>Whether a parent row holds the key a child row references.</summary>
    private bool ParentRowExists(RelationshipEnds ends, object?[] key) =>
        RowsHoldingKey(ends.Parent, ends.ParentColumns, key).Any();

    /// <summary>The rows of <paramref name="table"/> whose <paramref name="columns"/> hold <paramref name="key"/>
    /// on <see cref="KeyEquals"/>'s terms, read in full: seeked through an index on those columns when the key
    /// can be sought (every value in its column's kind, as a query's <c>column = value</c> seek requires), else
    /// scanned. A relationship's parent side always has one — its referenced columns are a primary or unique
    /// key — so the check every child INSERT makes, which scanned the whole parent table, is a seek.</summary>
    private IEnumerable<(RowId Id, object?[] Values)> RowsHoldingKey(Table table, int[] columns, object?[] key)
    {
        bool Holds(object?[] values) => KeyEquals(values, columns, key);

        var seekKey = new object?[key.Length];
        for (int i = 0; i < key.Length; i++)
        {
            ColumnDef column = table.Definition.Columns.First(c => c.Index == columns[i]);
            switch (ExpressionEvaluator.TryGetSeekKey(column, new LiteralExpression(key[i]), key[i], out seekKey[i]))
            {
                case ExpressionEvaluator.SeekKey.OtherKind:
                    return table.RowsWhere(columns, Holds);
                case ExpressionEvaluator.SeekKey.PastRange or ExpressionEvaluator.SeekKey.Fraction:
                    return [];   // a value its column cannot hold is in no row
            }
        }

        return table.RowsWithKey(columns, seekKey, Holds);
    }

    /// <summary>The enforced relationships for which <paramref name="parentTable"/> is the referenced
    /// (parent) side — i.e. those whose child rows a delete/key-update of a parent row must handle.</summary>
    private ForeignKey[] ChildRelationshipsOf(string parentTable)
    {
        if (!_referencesTo.TryGetValue(parentTable, out ForeignKey[]? found))
            _referencesTo[parentTable] = found = [.. _database.Catalog.Relationships.Where(r =>
                r.IsEnforced && string.Equals(r.ReferencedTable, parentTable, StringComparison.OrdinalIgnoreCase))];
        return found;
    }

    /// <summary>The enforced relationships <paramref name="childTable"/> holds the foreign key of.</summary>
    private ForeignKey[] ForeignKeysOf(string childTable)
    {
        if (!_foreignKeysOf.TryGetValue(childTable, out ForeignKey[]? found))
            _foreignKeysOf[childTable] = found = [.. _database.Catalog.ForeignKeysOf(childTable).Where(f => f.IsEnforced)];
        return found;
    }

    /// <summary><see cref="Identify"/>, once per relationship per statement.</summary>
    private RelationshipIdentity? IdentityOf(ForeignKey fk)
    {
        if (!_identities.TryGetValue(fk, out RelationshipIdentity? identity))
            _identities[fk] = identity = Identify(fk);
        return identity;
    }

    /// <summary>ACE's refusal to delete a parent row, or change its key, while a child row still holds it.</summary>
    private static InvalidOperationException RelatedRecords(ForeignKey fk) =>
        new($"The record cannot be deleted or changed because table '{fk.Table}' includes related records.");

    /// <summary>Child rows whose FK columns equal <paramref name="key"/>. Both callers skip a key with a null
    /// in it — nothing references one — so a null FK column simply fails the comparison.</summary>
    private List<(RowId Id, object?[] Values)> FindChildRows(RelationshipEnds ends, object?[] key) =>
        // Seeked where the child has an index on its key (ACE gives an enforced relationship one), else found by
        // a scan of the key; a match is read whole either way, because the cascade rewrites or deletes it.
        [.. RowsHoldingKey(ends.Child, ends.ChildColumns, key)];

    /// <summary>
    /// Deletes the given root rows and everything ON DELETE CASCADE reaches from them, applying SET NULL and
    /// NO ACTION as it goes. An explicit worklist replaces the former recursion: each row is scheduled at most
    /// once (a <see cref="RowId"/> set makes cyclic and diamond-shaped FK graphs terminate and delete a shared
    /// child exactly once), and rows are deleted in post-order — every cascade child before its parent — so no
    /// row is removed while another still references it. Bounded by the number of rows in the database, so a
    /// deep or cyclic chain can no longer overflow the call stack.
    /// </summary>
    private void CascadeDelete(Table rootTable, IEnumerable<(RowId Id, object?[] Values)> roots)
    {
        var scheduled = new HashSet<(string Table, RowId Id)>();
        var order = new List<(Table Table, RowId Id, object?[] Values)>();
        var stack = new Stack<(Table Table, RowId Id, object?[] Values, bool ChildrenExpanded)>();

        // Pushed last first, so the roots come off the stack — and are deleted — in the order they were found, as
        // ACE deletes them. The order shows in the file: a delete slides the rows below it up and leaves the old
        // bytes in the space it frees, so deleting the same rows the other way round leaves different bytes.
        foreach (var (id, values) in roots.Reverse())
            if (scheduled.Add((rootTable.Name, id)))
                stack.Push((rootTable, id, values, false));
        var rootRows = new HashSet<(string Table, RowId Id)>(scheduled);

        while (stack.Count > 0)
        {
            var (table, id, values, expanded) = stack.Pop();
            if (expanded)
            {
                order.Add((table, id, values)); // its children are all below it on the stack / already emitted
                continue;
            }
            stack.Push((table, id, values, true)); // revisit to emit after its children
            EnqueueCascadeChildren(table.Name, values, scheduled, stack);
        }

        foreach (var (table, id, _) in order)
        {
            table.Delete(id);
            EnforceCheckConstraints(table.Definition, new object?[table.Definition.Columns.Count],
                cascaded: !rootRows.Contains((table.Name, id)));
        }
    }

    /// <summary>Applies each enforced relationship's ON DELETE action to a parent row about to be deleted:
    /// CASCADE schedules its children for deletion (unless already scheduled), SET NULL nulls their FK columns
    /// now, and NO ACTION rejects the delete if any child exists (matching Access's "record cannot be deleted…
    /// includes related records").</summary>
    private void EnqueueCascadeChildren(string parentTable, object?[] parentValues,
        HashSet<(string Table, RowId Id)> scheduled,
        Stack<(Table Table, RowId Id, object?[] Values, bool ChildrenExpanded)> stack)
    {
        foreach (ForeignKey fk in ChildRelationshipsOf(parentTable))
        {
            RelationshipEnds ends = EndsOf(fk);
            object?[] key = [.. ends.ParentColumns.Select(c => parentValues[c])];
            if (key.Any(k => k is null)) continue; // a null parent key is referenced by nobody
            DependOnRelationship(fk, key,
                $"Transaction conflict on foreign key '{fk.Name}': a row in '{fk.Table}' was added for the row "
                + $"of '{fk.ReferencedTable}' this transaction deleted.");
            var children = FindChildRows(ends, key);
            if (children.Count == 0) continue;

            if (fk.CascadeDelete)
            {
                // Pushed last first, as the roots are, so the children come off the stack in the order they were
                // found — the order ACE deletes them in (measured, whole-file diff: a parent's two children left
                // the leaf and the data page holding the second one's bytes, where the reverse leaves the first's).
                foreach (var (cid, cvals) in Enumerable.Reverse(children))
                    if (scheduled.Add((ends.Child.Name, cid)))
                        stack.Push((ends.Child, cid, cvals, false));
            }
            else if (fk.DeleteSetNull)
                foreach (var (cid, cvals) in children)
                {
                    // A child this same statement is already deleting is left alone. Nulling it would rewrite a
                    // row whose values the delete is still holding — captured when it was scheduled — so the
                    // delete would then look its index entries up under a key the index no longer has
                    // ("entry not found"). Reachable on a self-referencing table, where the parent and the
                    // child are rows of one table and one DELETE can name both. Skipping costs nothing: the row
                    // and its entries go in a moment either way, and nobody can observe the intermediate null.
                    if (scheduled.Contains((ends.Child.Name, cid))) continue;
                    SetChildKey(ends, cid, cvals, newKey: null);
                }
            else
                throw RelatedRecords(fk);
        }
    }

    /// <summary>Applies each enforced relationship's ON UPDATE action when a parent row's referenced key
    /// changes: CASCADE rewrites the children's FK to the new key; NO ACTION rejects if any child exists.
    /// (Jet has no ON UPDATE SET NULL.)</summary>
    /// <remarks><c>stillToWrite</c> carries the rows a running UPDATE has yet to write: a cascade onto one of
    /// them changes the values that write will carry instead of writing the row here (see SetChildKey).</remarks>
    private void CascadeParentKeyUpdate(string parentTable, object?[] oldValues, object?[] newValues,
        Dictionary<(int Tdef, RowId Id), object?[]>? stillToWrite = null)
    {
        foreach (ForeignKey fk in ChildRelationshipsOf(parentTable))
        {
            RelationshipEnds ends = EndsOf(fk);
            object?[] oldKey = [.. ends.ParentColumns.Select(c => oldValues[c])];
            object?[] newKey = [.. ends.ParentColumns.Select(c => newValues[c])];
            if (oldKey.Any(k => k is null) || KeyEquals(oldKey, columns: null, newKey)) continue;
            DependOnRelationship(fk, oldKey,
                $"Transaction conflict on foreign key '{fk.Name}': a row in '{fk.Table}' was added for the key of "
                + $"'{fk.ReferencedTable}' this transaction changed.");
            var children = FindChildRows(ends, oldKey);
            if (children.Count == 0) continue;

            if (fk.CascadeUpdate)
                foreach (var (cid, cvals) in children) SetChildKey(ends, cid, cvals, newKey, stillToWrite);
            else
                throw RelatedRecords(fk);
        }
    }

    /// <summary>Whether <paramref name="values"/> holds <paramref name="key"/> — through
    /// <paramref name="columns"/> when the values are a whole row the key sits in columns of, position for
    /// position when they are a key already.</summary>
    /// <remarks>Compared on the evaluator's terms rather than the CLR's, so referential integrity reaches the
    /// same rows a query predicate over the same key would: a LONG 1 and a DOUBLE 1.0 are one key, and text is
    /// compared in the database's collation.</remarks>
    private bool KeyEquals(object?[] values, int[]? columns, object?[] key)
    {
        for (int i = 0; i < key.Length; i++)
            if (ExpressionEvaluator.CompareForSort(values[columns is null ? i : columns[i]], key[i], _scalarRunner.TextComparer) != 0)
                return false;
        return true;
    }

    /// <summary>Rewrites a child row's FK columns to <paramref name="newKey"/> (or NULL for SET NULL).</summary>
    private void SetChildKey(RelationshipEnds ends, RowId childId, object?[] childValues, object?[]? newKey,
        Dictionary<(int Tdef, RowId Id), object?[]>? stillToWrite = null)
    {
        var newValues = (object?[])childValues.Clone();
        for (int i = 0; i < ends.ChildColumns.Length; i++)
            newValues[ends.ChildColumns[i]] = newKey?[i];

        // A child row the running statement has yet to write itself: hand it the new key and let its own write
        // carry it, so the row it writes and the index entries it moves both hold the cascaded value. Writing
        // here as well would be undone by that write — it was built before this cascade ran — while the index
        // entry moved here would keep the new key, which is a row disagreeing with its own index.
        if (stillToWrite?.TryGetValue((ends.Child.Definition.DefinitionPage, childId), out object?[]? waiting) == true)
        {
            foreach (int idx in ends.ChildColumns)
                if (!Equals(newValues[idx], childValues[idx])) waiting[idx] = newValues[idx];
            return;
        }

        // A cascade rewrites a row, so it owes the row everything an UPDATE of it does — and the same code does it.
        UpdateRow(ends.Child, childId, childValues, newValues, stillToWrite, cascaded: true);
    }

    /// <summary>
    /// Rewrites one row whose values <paramref name="values"/> now hold — an UPDATE's own row, or a child a cascade
    /// reaches — after checking it against every rule an updated row is held to, then applies the ON UPDATE rules
    /// of any relationship whose key it changed. The one place a row is rewritten: a cascaded row is checked
    /// exactly as a directly updated one, and a cascade that changes a key others reference carries on down.
    /// <paramref name="cascaded"/> says a cascade is writing it, which ACE words a refused CHECK differently for.
    /// </summary>
    private void UpdateRow(Table table, RowId id, object?[] original, object?[] values,
        Dictionary<(int Tdef, RowId Id), object?[]>? stillToWrite, bool cascaded)
    {
        var changed = new HashSet<int>();
        for (int i = 0; i < values.Length; i++)
            if (!Equals(original[i], values[i])) changed.Add(i);
        if (changed.Count == 0)
        {
            stillToWrite?.Remove((table.Definition.DefinitionPage, id));
            return; // unchanged after all
        }

        // UPDATE must preserve the same Required/NOT NULL invariant as INSERT. Check the complete
        // post-assignment row before any referential action, row rewrite, or index mutation occurs.
        EnforceRequired(table.Name, table.Definition.Columns, values);

        // A changed primary-key or WITH DISALLOW NULL column must not become Null, the insert rule.
        foreach (IndexDef index in table.Definition.Indexes
            .Where(i => (i.IsPrimaryKey || i.Required) && i.Columns.Any(c => changed.Contains(c.Column.Index))))
            if (index.Columns.FirstOrDefault(c => values[c.Column.Index] is null).Column is { } nullColumn)
                throw new InvalidOperationException(
                    $"Index or primary key cannot contain a Null value: column '{nullColumn.Name}' of "
                    + $"'{table.Name}' is Null, and index '{index.Name}' does not allow it.");

        // Child side: a changed FK column must still reference an existing parent (like an insert).
        if (ForeignKeysOf(table.Name).Any(f => EndsOf(f).ChildColumns.Any(changed.Contains)))
            EnforceReferentialIntegrity(table.Name, table, values, update: true);

        // A changed UNIQUE/PRIMARY key must not collide with another row (null keys are distinct — a
        // unique index permits multiple nulls, so they're skipped, matching the insert rule).
        foreach (IndexDef index in table.Definition.RealIndexes
            .Where(i => i.IsUnique && i.Columns.Any(c => changed.Contains(c.Column.Index))))
            if (!index.Columns.Any(c => values[c.Column.Index] is null) && table.HasDuplicateKey(index, values, id))
                throw new ConstraintViolationException(
                    $"Cannot update '{table.Name}': a row with the same " +
                    $"{(index.IsPrimaryKey ? "primary key" : "unique key")} already exists (index '{index.Name}').",
                    index.Name,
                    index.IsPrimaryKey);

        table.Update(id, values, changed);
        stillToWrite?.Remove((table.Definition.DefinitionPage, id));

        // The updated row must still satisfy every CHECK constraint.
        EnforceCheckConstraints(table.Definition, values, cascaded);

        // Parent side, once the row holds its new key: a changed referenced-key column triggers each relationship's
        // ON UPDATE action (CASCADE rewrites the children, NO ACTION rejects if any exist). After the write, so a
        // cascaded child's own foreign-key check finds the new key — and a row referencing itself is found as its
        // own child and rewritten like any other.
        CascadeParentKeyUpdate(table.Name, original, values, stillToWrite);
    }

    private int ExecuteCreateIndex(CreateIndexStatement statement)
    {
        _database.CreateIndex(
            statement.Table,
            statement.Name,
            statement.Columns,
            isUnique: statement.IsUnique,
            isPrimary: statement.WithOption == IndexWithOption.Primary,
            disallowNull: statement.WithOption == IndexWithOption.DisallowNull,
            ignoreNulls: statement.WithOption == IndexWithOption.IgnoreNull);
        return 0;
    }

    private int ExecuteCreateView(CreateViewStatement statement)
    {
        // CREATE VIEW drops a leading space from the name, where every other route refuses one (verified vs ACE).
        _database.CreateView(statement.Name.TrimStart(' '), BuildViewSpec(statement.Definition));
        return 0;
    }

    private int ExecuteCreateProcedure(CreateProcedureStatement statement)
    {
        // A procedure is a parameterized stored query: a view spec plus a parameter row per declared
        // parameter (name + Jet type code, resolved from the declared Access type name).
        //
        // A parameter declares no storage, so its type neither raises the file nor is refused by it: ACE stores a
        // BIGINT or DATETIME2 parameter on an ACE 12 file with its type, and the version byte stays 0x02 (verified).
        _database.CreateView(
            statement.Name,
            BuildViewSpec(statement.Definition) with { Parameters = BuildParameterSpecs(statement.Parameters) });
        return 0;
    }

    /// <summary>The declared parameters of a stored query as the rows that hold them: each name with the Jet
    /// type code its declared Access type maps to. An action query declares them exactly as a SELECT does.</summary>
    private static List<ViewParameterSpec>? BuildParameterSpecs(IReadOnlyList<ProcedureParameter>? parameters) =>
        parameters is null or { Count: 0 }
            ? null
            : parameters
                .Select(p => new ViewParameterSpec(
                    p.Stored ?? p.Name,
                    // The declared size decides the type code as well as being stored: Text(50) is a Text
                    // parameter (code 10) where a bare Text is a memo (12), which is what ACE records.
                    (byte)AccessTypeMapper.ToColumnSpec(new ColumnDefinition(p.Name, p.TypeName, p.Size, p.Scale, false, false)).Type,
                    p.Size, p.Scale))
                .ToList();

    private int ExecuteAlterTable(AlterTableStatement statement) => statement.Action switch
    {
        // ADD CONSTRAINT … PRIMARY KEY (cols): a primary key is a unique, primary index named after the
        // constraint (verified vs ACE) — the same write path as CREATE INDEX … WITH PRIMARY.
        AddPrimaryKeyAction pk => AddPrimaryKey(statement.Table, pk),
        // ADD CONSTRAINT … UNIQUE (cols): a unique (non-primary) index named after the constraint — the same
        // write path as CREATE INDEX / ADD PRIMARY KEY, minus the primary flag.
        AddUniqueAction uq => AddUnique(statement.Table, uq),
        // ADD CONSTRAINT … CHECK (expr): persist the check to the table's LvProp; the engine then enforces it.
        AddCheckAction chk => AddCheck(statement.Table, chk),
        // ADD CONSTRAINT … FOREIGN KEY: add a relationship to the existing table (child index + parent
        // incoming block + MSysRelationships), the same write path as an inline CREATE TABLE foreign key.
        AddForeignKeyAction fk => AddForeignKey(statement.Table, fk.ForeignKey),
        // DROP CONSTRAINT name: drop a foreign key (relationship) by name. LibRed enforces FKs from
        // MSysRelationships, so removing those rows disables it.
        DropConstraintAction drop => DropConstraint(statement.Table, drop.Name),
        // ADD COLUMN: append the column's descriptor/name to the TDEF (existing rows read it as NULL).
        AddColumnAction add => AddColumn(statement.Table, add),
        // DROP COLUMN: a metadata-only TDEF edit (remove the descriptor + name, decrement ColumnCount).
        DropColumnAction dropCol => DropColumn(statement.Table, dropCol.Field),
        // ALTER COLUMN field type: change the column's declared type (a variable text/binary length change is a
        // descriptor edit; other storage-type changes need a full column rewrite and throw NotSupported).
        AlterColumnAction alterCol => AlterColumn(statement.Table, alterCol),
        // ALTER COLUMN … SET/DROP DEFAULT: an LvProp edit only — no type change, and DROP DEFAULT keeps NOT NULL.
        AlterColumnSetDefaultAction setDef => SetColumnDefault(statement.Table, setDef),
        AlterColumnDropDefaultAction dropDef => DropColumnDefault(statement.Table, dropDef),
        // RENAME { TO | COLUMN … TO | INDEX … TO }: provider DDL — Jet/ACE has no rename syntax at all (Access
        // renames through DAO/ADOX), so EFCore.Jet emits this as pseudo-SQL and intercepts it out-of-engine.
        // LibRed has no COM to delegate to, so it does the catalog/TDEF surgery itself.
        RenameTableAction rt => RenameTable(statement.Table, rt.NewName),
        RenameColumnAction rc => RenameColumn(statement.Table, rc.Field, rc.NewName),
        RenameIndexAction ri => RenameIndex(statement.Table, ri.Index, ri.NewName),
        // The remaining actions land in their own follow-up steps.
        _ => throw new NotSupportedException(
            $"ALTER TABLE {statement.Action.GetType().Name} is parsed but not executed yet."),
    };

    /// <summary>
    /// RENAME TO — renames the table itself. Behaviour measured against ACE (see <c>RenameFanOutProbeTest</c>):
    /// update the object's <c>MSysObjects.Name</c> and repoint <c>MSysRelationships</c>
    /// (<c>szObject</c>/<c>szReferencedObject</c>), which stores tables by name — ACE rewrites those, preserves
    /// the relationship's name and enforcement, and does <b>not</b> refuse the rename for a table in an enforced
    /// relationship. Indexes need no action: they ride along with the table and keep their own names.
    /// Stored queries/views are deliberately left dangling — ACE does not rewrite <c>MSysQueries</c> (Name
    /// AutoCorrect is an Access application feature, not an engine one), so a view naming the old table breaks.
    /// Match that rather than "fixing" it, or LibRed and Jet diverge on the same migration.
    /// </summary>
    private int RenameTable(string table, string newName)
    {
        if (!_database.RenameTable(table, newName))
            throw new InvalidOperationException($"ALTER TABLE '{table}' RENAME TO '{newName}': the table was not found.");
        return 0;
    }

    /// <summary>
    /// RENAME COLUMN — renames a column. Behaviour measured against ACE (see <c>RenameFanOutProbeTest</c>).
    /// The name lives in the TDEF's column descriptor and is variable-length, so a different-length name relays
    /// out the TDEF: the rewrite must be faithful (every unparsed descriptor byte preserved, only the name
    /// changed). Two fixups are required because ACE performs them: <c>MSysRelationships</c>
    /// (<c>szColumn</c>/<c>szReferencedColumn</c>), and the per-column keys in the table's <c>LvProp</c> property
    /// blob — a renamed column <b>keeps its DEFAULT</b>, so the name-keyed properties must be carried across.
    /// Indexes need no fixup: they reference columns by id and keep their own names. Stored queries/views are
    /// left dangling, as for a table rename — ACE does not rewrite <c>MSysQueries</c>.
    /// </summary>
    private int RenameColumn(string table, string field, string newName)
    {
        if (!_database.RenameColumn(table, field, newName))
            throw new InvalidOperationException(
                $"ALTER TABLE '{table}' RENAME COLUMN '{field}' TO '{newName}': the column was not found.");
        return 0;
    }

    /// <summary>
    /// RENAME INDEX — renames an index; the name lives in the TDEF's index block. If the index backs a foreign
    /// key, check its coupling to the relationship name (<c>szRelationship</c>). Access cannot do this through
    /// SQL <i>or</i> DAO/ADOX, so there is no ACE oracle for the operation itself — verify via the converged
    /// end state (an index created as the new name should match one created then renamed).
    /// </summary>
    private int RenameIndex(string table, string index, string newName) => throw new NotSupportedException(
        $"ALTER TABLE `{table}` RENAME INDEX `{index}` TO `{newName}` is parsed and routed but the TDEF rewrite is not implemented yet.");

    private int DropConstraint(string table, string name)
    {
        // In Jet/ACE a named constraint is a relationship (FK), an index (PK/unique — a UNIQUE constraint IS a
        // unique index, so DROP CONSTRAINT and DROP INDEX are interchangeable), or a table-level CHECK. Try each
        // in turn (all verified vs ACE); the index path itself rejects an FK-backing index, matching ACE.
        if (_database.DropConstraint(table, name)) return 0;
        if (_database.DropIndex(table, name)) return 0;
        if (_database.DropCheckConstraint(table, name)) return 0;
        throw new InvalidOperationException(
            $"ALTER TABLE '{table}' DROP CONSTRAINT '{name}': no foreign key, primary key, unique, or check " +
            "constraint of that name exists.");
    }

    private int AddColumn(string table, AddColumnAction add)
    {
        ColumnDefinition column = add.Column;
        // NOT NULL and DEFAULT are written to the column's LvProp properties (Required / DefaultValue).
        ColumnSpec spec = AccessTypeMapper.ToColumnSpec(column);
        if (!_database.AddColumn(table, spec, column.Default))
            throw new InvalidOperationException($"ALTER TABLE '{table}' ADD COLUMN '{column.Name}': the column already exists.");
        // The column's constraints apply to it as in CREATE TABLE — a primary key, a unique index, a relationship —
        // in that order, as ACE's ADD COLUMN does. Each fails the whole statement when ACE's would: a second
        // primary key, or a primary key over a column the existing rows leave NULL.
        if (column.PrimaryKey)
            AddPrimaryKey(table, new AddPrimaryKeyAction(add.PrimaryKeyName, [column.Name]));
        if (add.Unique is { } unique)
            AddUnique(table, new AddUniqueAction(unique));
        if (add.References is { } references)
            AddForeignKey(table, references);
        return 0;
    }

    private int DropColumn(string table, string column)
    {
        if (!_database.DropColumn(table, column))
            throw new InvalidOperationException($"ALTER TABLE '{table}' DROP COLUMN '{column}': no such column.");
        return 0;
    }

    private int DropIndex(string table, string index)
    {
        if (!_database.DropIndex(table, index))
            throw new InvalidOperationException($"DROP INDEX '{index}' ON '{table}': no such index.");
        return 0;
    }

    private int DropTable(string table)
    {
        if (!_database.DropTable(table))
            throw new InvalidOperationException($"Table '{table}' does not exist.");   // ACE's message (verified)
        return 0;
    }

    // ACE refuses a DROP VIEW or DROP PROCEDURE of nothing with its DDL message (verified).
    private int DropQueryObject(string name)
    {
        if (!_database.DropQueryObject(name))
            throw new InvalidOperationException("Cannot find table or constraint.");
        return 0;
    }

    private int AddPrimaryKey(string table, AddPrimaryKeyAction pk)
    {
        _database.CreateIndex(
            table,
            pk.Name ?? "PrimaryKey",
            pk.Columns.Select(c => (c, false)).ToList(), // PK columns are ascending
            isUnique: true, isPrimary: true);
        return 0;
    }

    private int AddUnique(string table, AddUniqueAction uq)
    {
        _database.CreateIndex(
            table,
            uq.Unique.Name ?? $"UQ_{table}",
            uq.Unique.Columns.Select(c => (c, false)).ToList(),
            isUnique: true, isPrimary: false);
        return 0;
    }

    private int AddCheck(string table, AddCheckAction chk)
    {
        // A missing table is left for the design path to report, as it always has: it reads nothing.
        TableDefinition? definition = _database.Catalog.FindTable(table);
        _database.AddCheckConstraint(table, chk.Check.Name ?? $"CK_{table}", chk.Check.Expression, definition is null ? []
            : CheckReads(definition.Name, [.. definition.Columns.Select(c => c.Name)], chk.Check.Expression));
        return 0;
    }

    private int AlterColumn(string table, AlterColumnAction alter)
    {
        var colDef = new ColumnDefinition(alter.Field, alter.TypeName, alter.Size, alter.Scale, NotNull: false, PrimaryKey: false,
            Identity: alter.Identity);
        // A DEFAULT the new type refuses fails the statement, and its transaction takes the type change back with it.
        _database.AlterColumn(table, alter.Field, AccessTypeMapper.ToColumnSpec(colDef));
        // Apply DEFAULT before Required so the column's property map keeps ACE's order (DefaultValue, then Required).
        if (alter.Default is not null)   // ALTER COLUMN … DEFAULT: set the column's default after the type change
            _database.SetColumnDefault(table, alter.Field, alter.Default);
        if (alter.NotNull is bool required)   // ALTER COLUMN … NOT NULL / NULL: set or clear the Required property
            _database.SetColumnRequired(table, alter.Field, required);
        return 0;
    }

    private int SetColumnDefault(string table, AlterColumnSetDefaultAction set)
    {
        _database.SetColumnDefault(table, set.Field, set.Default);
        return 0;
    }

    private int DropColumnDefault(string table, AlterColumnDropDefaultAction drop)
    {
        _database.DropColumnDefault(table, drop.Field);
        return 0;
    }

    private int AddForeignKey(string table, ForeignKeyConstraint fk)
    {
        RelationshipSpec relationship = ToRelationshipSpec(table, fk);
        _database.AddForeignKey(table, relationship);

        ForeignKey added = _database.Catalog.Relationships.Single(
            r => string.Equals(r.Name, relationship.Name, StringComparison.OrdinalIgnoreCase));
        RelationshipIdentity identity = Identify(added)!;
        bool Holds() => CurrentRelationship(identity) is not { } current || _database.ExistingRowsHaveParents(current);

        // ACE then holds both tables exclusively until the transaction ends, so nothing can delete a parent the
        // check found (verified: another connection's DELETE is refused). LibRed takes no table locks, so the
        // same check is made again when the transaction commits, for as long as the relationship survives.
        _database.DependOn(
            Holds,
            $"Transaction conflict on foreign key '{added.Name}': a row in '{added.ReferencedTable}' that the "
            + $"existing rows of '{added.Table}' were checked against no longer exists.",
            key: identity);
        return 0;
    }

    private int ExecuteCreateActionProcedure(CreateActionProcedureStatement statement)
    {
        if (statement.Kind == ProcedureActionKind.DataDefinition)
        {
            _database.CreateActionQuery(
                statement.Name, new ActionQuerySpec(ActionQueryKind.DataDefinition, DdlSql: statement.DdlSql));
            return 0;
        }

        // Every other kind stores its sources, joins and WHERE the way a view stores them, so the body maps
        // through the same builder; only the action row and the meaning of the column rows differ.
        var spec = new ActionQuerySpec(
            statement.Kind switch
            {
                ProcedureActionKind.Append => ActionQueryKind.Append,
                ProcedureActionKind.Update => ActionQueryKind.Update,
                ProcedureActionKind.Delete => ActionQueryKind.Delete,
                ProcedureActionKind.MakeTable => ActionQueryKind.MakeTable,
                _ => throw new NotSupportedException($"A {statement.Kind} procedure body is not stored yet."),
            },
            TargetTable: statement.TargetTable,
            Values: statement.AppendColumns?.Select(c => new AppendColumnSpec(c.Column, c.ValueExpression)).ToList(),
            Body: statement.Body is { } body ? BuildViewSpec(body) : null,
            Parameters: BuildParameterSpecs(statement.Parameters),
            DeleteTarget: statement.DeleteTarget,
            OwnerAccess: statement.OwnerAccess,
            TargetDatabase: ExternalSpec(statement.TargetDatabase));

        _database.CreateActionQuery(statement.Name, spec);
        return 0;
    }

    private static ViewSpec BuildViewSpec(ViewDefinition d) => new(
        d.Distinct,
        d.Columns.Select(c => new ViewColumnSpec(c.Expression, c.Alias)).ToList(),
        d.Tables.Select(t => new ViewTableSpec(t.Table, t.Alias, t.SubquerySql)).ToList(),
        d.Joins.Select(j => new ViewJoinSpec(
            j.Kind switch { ViewJoinKind.Left => ViewJoinType.Left, ViewJoinKind.Right => ViewJoinType.Right, _ => ViewJoinType.Inner },
            j.Condition, j.LeftAlias, j.RightAlias)).ToList(),
        d.Where,
        d.GroupBy,
        d.Having,
        Parameters: null,
        OrderBy: d.OrderBy.Select(o => new ViewOrderBySpec(o.Expression, o.Descending)).ToList(),
        Top: d.Top,
        TopPercent: d.TopPercent,
        OwnerAccess: d.OwnerAccess,
        Union: d.Union,
        Database: ExternalSpec(d.Database),
        Crosstab: d.Crosstab is { } c ? new ViewCrosstabSpec(c.Value, c.ValueAlias, c.Pivot, c.PivotColumn) : null,
        AllFields: d.AllFields,
        DistinctRow: d.DistinctRow);

    private static ExternalDatabaseSpec? ExternalSpec(ExternalDatabase? database) =>
        database is null ? null : new ExternalDatabaseSpec(database.Path, database.Connect);

    /// <summary>
    /// A make-table query: <c>SELECT … INTO newtable FROM source</c>.
    /// </summary>
    /// <remarks>
    /// The new table takes the result's column names and types and NOTHING else. Measured against ACE
    /// (<c>SelectIntoShapeProbeTest</c>): a source's PRIMARY KEY and indexes are not copied, so archiving a
    /// keyed table gives an unkeyed copy. An expression column is typed from the expression rather than from
    /// any source column — <c>Qty * 2</c> gives Int32, a concatenation gives Text at the 255-character
    /// maximum, and <c>SUM</c> widens to Double. An empty result still creates the table. An existing name is
    /// an error, which the docs call "a trappable error" and ACE reports as "Table 'X' already exists".
    /// </remarks>
    private int ExecuteSelectInto(SelectStatement statement)
    {
        // A taken name — a table's, a query's or a linked table's — is refused by CreateTable, before anything is
        // written; the read below has no side effect to undo.
        string target = statement.Into!;
        // An IN externaldatabase after INTO makes the table in that file (verified vs ACE).
        JetDatabase into = ExternalDatabases.Open(_database, statement.IntoDatabase, writable: true);
        // Both of these before a row is read (verified vs ACE): the same table as source and destination is reported
        // first, and a read-only database is refused straight after it. A table of that name in another file is
        // another table: ACE reports it as the name being taken.
        if (TablesIn(statement.From).Any(t => string.Equals(t.Name, target, StringComparison.OrdinalIgnoreCase)
                && ExternalDatabases.Open(_database, t.Database) == into))
            throw new InvalidOperationException(
                $"Same table '{target}' referenced as both the source and destination in make-table query.");
        into.RefuseIfReadOnly(ReadOnlyWrite.DatabaseOrObject);

        // Run the query first — with INTO stripped, or planning would recurse back into this method — and
        // materialise it. The rows have to exist before the table does: the source may read a table this
        // statement is about to change, and the row count is not known until the read completes.
        ResultSet source = _scalarRunner.ExecuteQuery(Planning.ColumnPruning.Apply(Planning.IndexSelection.Apply(
            Planning.QueryPlanner.PlanSelect(statement with { Into = null }), _database)));
        var rows = source.Rows.ToList();

        // A result column that IS a source column keeps that column's DEFINITION — its type, its declared
        // width and whether it is an AutoNumber. Only a computed column is typed from its value. Measured: a
        // source Text(30) arrives as Text(60) bytes, not the Text(510) maximum, while a concatenation does get
        // the maximum because there is no declared width to copy; and a COUNTER arrives as a COUNTER — with the
        // default step whatever the source's, its counter left on the last id copied. Every column's ordinal counts
        // from 1 (all verified vs ACE, WholeFileParityProbeTest).
        var sourceColumns = SourceColumnsFor(statement);
        var specs = source.ColumnNames
            .Select((name, i) => (sourceColumns.TryGetValue(name, out ColumnDef? column)
                ? new ColumnSpec(name, column.Type, column.Length, column.IsFixedLength, column.IsAutoNumber,
                                 Precision: column.Precision, Scale: column.Scale)
                : ColumnSpecFor(source.Columns[i])) with
            { Ordinal = i + 1 })
            .ToList();

        // ACE fills in every column's descriptor from one structure, whose collation shares its bytes with a
        // Decimal's precision and scale. So after a Decimal, each later column but a text one, whose collation is
        // its own, carries the latest Decimal's precision and scale there as two words — 0C 00 02 00 after a
        // DECIMAL(12,2) — and the next statement starts afresh (verified vs ACE, copied columns included,
        // AceDecimalPrecisionProbeTest.Make_table_descriptor_union_probe; a Memo is taken to be text, unmeasured).
        ColumnSpec? carried = null;
        for (int i = 0; i < specs.Count; i++)
        {
            if (specs[i].Type == JetDataType.FixedPoint)
                carried = specs[i];
            else if (specs[i].Type is not (JetDataType.Text or JetDataType.Memo) && carried is not null)
                specs[i] = specs[i] with { Precision = carried.Precision, Scale = carried.Scale };
        }

        if (into == _database)
            MakeTable(target, specs, rows);
        else
            // Another file is written as a statement on it would be: under its own exclusive scope and in its own
            // transaction, so a failure part-way leaves no half-made table there either. Its rows are checked against
            // that file's catalog, by an executor of its own.
            into.WriteExclusive(() =>
            {
                into.BeginTransaction();
                try
                {
                    new StatementExecutor(into, null, _parser).MakeTable(target, specs, rows);
                    into.Commit(flush: false);
                }
                catch
                {
                    into.Rollback();
                    throw;
                }
                return 0;
            });

        if (_session is not null) _session.RowCount = rows.Count;
        return rows.Count;
    }

    /// <summary>Creates a make-table query's new table and writes its rows into it, each the way every inserted row is,
    /// so whatever the new table carries is checked as INSERT checks it.</summary>
    private void MakeTable(string target, List<ColumnSpec> specs, List<object?[]> rows)
    {
        _database.CreateTable(target, specs);
        Table table = _database.OpenTable(target);
        RowDefaults defaults = DefaultsOf(table.Definition);
        var provided = Enumerable.Range(0, specs.Count).ToList();
        foreach (object?[] row in rows)
        {
            var values = new object?[specs.Count];
            Array.Copy(row, values, Math.Min(row.Length, values.Length));
            InsertNewRow(target, table, defaults, values, provided);
        }
    }

    /// <summary>
    /// The source columns a make-table's output names can be copied from, by output name.
    /// </summary>
    /// <remarks>
    /// Only projections that ARE a column carry a definition to copy: <c>SELECT Label</c> and
    /// <c>SELECT Label AS L</c> do, <c>SELECT Label &amp; '!'</c> does not. <c>SELECT *</c> takes every
    /// column of every table in the FROM. Anything not found here falls back to typing from the value.
    /// </remarks>
    private Dictionary<string, ColumnDef> SourceColumnsFor(SelectStatement statement)
    {
        var available = new Dictionary<string, ColumnDef>(StringComparer.OrdinalIgnoreCase);
        foreach (NamedTable table in TablesIn(statement.From))
            if (ExternalDatabases.Open(_database, table.Database).Catalog.FindTable(table.Name) is { } def)
                foreach (ColumnDef column in def.Columns)
                    available.TryAdd(column.Name, column);

        if (statement.IsSelectStar) return available;

        var byOutputName = new Dictionary<string, ColumnDef>(StringComparer.OrdinalIgnoreCase);
        foreach (SelectItem item in statement.Projection)
            if (item.Value is ColumnReference reference &&
                available.TryGetValue(reference.Column, out ColumnDef? column))
                byOutputName.TryAdd(item.Alias ?? reference.Column, column);
        return byOutputName;
    }

    /// <summary>The tables a FROM clause reaches, so their column definitions can be found.</summary>
    private static IEnumerable<NamedTable> TablesIn(TableReference? from) => from switch
    {
        NamedTable named => [named],
        JoinTable join => [.. TablesIn(join.Left), .. TablesIn(join.Right)],
        // A derived table has no stored column definitions to copy, so its columns fall back to being typed
        // from their values — as a computed column does.
        _ => [],
    };

    /// <summary>The column a COMPUTED result column becomes. Text takes the 255-character maximum, because an
    /// expression carries no declared width — ACE gives a concatenation Text(255) rather than measuring the
    /// values it produced. A Boolean, Byte or Integer result is a fixed Integer; every other number and a date
    /// keep their type but are variable-length, a Single widening to Double (verified vs ACE on CBool, a
    /// comparison, CByte, CInt, CLng, CSng, CDbl, CCur, CDate, COUNT, SUM and arithmetic,
    /// WholeFileParityProbeTest). A Decimal is a DECIMAL of the precision and scale the result has
    /// (<see cref="ExpressionEvaluator.NumberTypeOf"/>), whose length is the bytes the largest number of that
    /// precision takes and a sign byte — 6 for DECIMAL(12,2), 13 for DECIMAL(28,2) — where a declared DECIMAL's
    /// is 17; its values are 17 bytes whichever (verified vs ACE, AceDecimalPrecisionProbeTest.Make_table_column_type_probe).
    /// A Decimal with no scale of its own is a Double.</summary>
    private static ColumnSpec ColumnSpecFor(ResultColumn column) => Type.GetTypeCode(column.ClrType) switch
    {
        TypeCode.Boolean or TypeCode.Byte or TypeCode.Int16 => new ColumnSpec(column.Name, JetDataType.Int16, 2, IsFixedLength: true),
        TypeCode.Int32 => new ColumnSpec(column.Name, JetDataType.Int32, 4, IsFixedLength: false),
        // A BIGINT, as AccessTypeMapper declares one (verified vs ACE: BIGINT * 2 makes one).
        TypeCode.Int64 => new ColumnSpec(column.Name, JetDataType.Int64, 8, IsFixedLength: false),
        TypeCode.Single or TypeCode.Double => new ColumnSpec(column.Name, JetDataType.Double, 8, IsFixedLength: false),
        TypeCode.Decimal when column.TypeName == "Currency" =>
            new ColumnSpec(column.Name, JetDataType.Currency, 8, IsFixedLength: false),
        TypeCode.Decimal when column is { Precision: int precision, Scale: int scale } => new ColumnSpec(
            column.Name, JetDataType.FixedPoint,
            (System.Numerics.BigInteger.Pow(10, precision) - 1).GetByteCount(isUnsigned: true) + 1,
            IsFixedLength: false, Precision: (byte)precision, Scale: (byte)scale),
        TypeCode.Decimal => new ColumnSpec(column.Name, JetDataType.Double, 8, IsFixedLength: false),
        TypeCode.DateTime => new ColumnSpec(column.Name, JetDataType.DateTime, 8, IsFixedLength: false),
        // Variable, like every GUID column ACE declares — including through SELECT INTO, which is this
        // method's caller (verified: ACE's `SELECT G INTO Dst` gives Dst.G length 16, fixed flag clear).
        // See AccessTypeMapper for the same rule on a declared column, and BIGINT for the same shape.
        _ when column.ClrType == typeof(Guid) => new ColumnSpec(column.Name, JetDataType.Guid, 16, IsFixedLength: false),
        _ when column.ClrType == typeof(byte[]) => new ColumnSpec(column.Name, JetDataType.Binary, 255, IsFixedLength: false),
        _ => new ColumnSpec(column.Name, JetDataType.Text, 255 * 2, IsFixedLength: false),
    };

    /// <summary>Sentinel standing for an explicit <c>DEFAULT</c> row value. Reference identity is what marks
    /// it, so it can never collide with a real value — including a genuine NULL, which DEFAULT is distinct
    /// from: NULL stores NULL, DEFAULT takes whatever the column declares.</summary>
    private static readonly object DefaultRowValue = new();

    /// <summary>A table's columns with a DEFAULT value (parsed once), and its AutoNumber column if it has one — Jet
    /// allows at most one, and its value after an insert is @@IDENTITY.</summary>
    private sealed record RowDefaults(List<(int Index, Expression Expression)> Columns, ColumnDef? AutoNumber);

    /// <summary>
    /// The defaults a new row takes for the columns it omits, matching Access — EF Core relies on the store default
    /// rather than supplying the value itself. AutoNumber columns are excluded: their value is assigned by the row
    /// inserter (sequential counter, or a random Int32 for a GenUniqueID() "Random" AutoNumber), not by evaluating
    /// the DefaultValue — and GenUniqueID() is not a callable expression, so parsing it as a default would fail.
    /// </summary>
    private RowDefaults DefaultsOf(TableDefinition definition) => new(
        definition.Columns
            .Where(c => c.DefaultValue is not null && !c.IsAutoNumber)
            .Select(c => (c.Index, Expression: ParseDefaultExpression(c.DefaultValue!)))
            .ToList(),
        definition.Columns.FirstOrDefault(c => c.IsAutoNumber));

    /// <summary>Writes a new row: every column not in <paramref name="provided"/> takes its default (an explicit
    /// NULL is left as NULL), the row is checked as any insert is, and it is written. Returns its AutoNumber value,
    /// if it has one. <paramref name="provided"/> is in the order the statement names the columns.</summary>
    /// <remarks>A Text column's default is cut to the column's width, where a value supplied for it is refused
    /// (verified vs ACE: <c>DEFAULT 'abcdefghijklmnopqrstuvwxyz'</c> on a <c>TEXT(20)</c> stores its first 20
    /// characters, and <c>DEFAULT Now()</c> the first 20 of the date's text). A supplied column's default is evaluated
    /// too where it can size the record's slack — a variable column's, not a Memo's or OLE's: ACE writes the row
    /// over the record of them all (spec §5).</remarks>
    private object? InsertNewRow(string tableName, Table table, RowDefaults defaults, object?[] values, List<int> provided)
    {
        var evaluator = new ExpressionEvaluator(new EvalScope([], [], null), _scalarRunner, parameters: _parameters);
        var defaultRecord = new object?[values.Length];
        foreach (var (index, expression) in defaults.Columns)
        {
            ColumnDef column = table.Definition.Columns[index];
            bool supplied = provided.Contains(index);
            if (supplied && (column.IsFixedLength || column.IsCalculated || column.Type is JetDataType.Memo or JetDataType.Ole))
                continue;
            object? value;
            if (!supplied) value = evaluator.Evaluate(expression);
            else
                try { value = evaluator.Evaluate(expression); }
#pragma warning disable CA1031 // a supplied column's default only sizes the record's slack: one that cannot be evaluated adds none rather than failing a row that never takes it
                catch (Exception) { value = null; }
#pragma warning restore CA1031
            if (column.Type == JetDataType.Text && value is not null && JetStoreType.MaxLength(column) is int width
                && LibRed.Storage.Types.VbaText.Of(value) is var text && text.Length > width)
                value = text[..width];
            defaultRecord[index] = value;
            if (!supplied) values[index] = value;
        }

        EnforceRequired(tableName, table.Definition.Columns, values);
        EnforceReferentialIntegrity(tableName, table, values);
        table.Insert(values, defaultRecord, provided); // fills values[autoNumber.Index] with the generated id (array mutated in place)
        EnforceCheckConstraints(table.Definition, values, cascaded: false);
        return defaults.AutoNumber is { } autoNumber ? values[autoNumber.Index] : null;
    }

    private int ExecuteInsert(InsertStatement statement)
    {
        // The target is the one table ACE names differently when it is missing (verified).
        if (_database.Catalog.FindTable(statement.Table) is null)
            throw new InvalidOperationException($"Could not find output table '{statement.Table}'.");
        Table table = _database.OpenTable(statement.Table);
        // Before the column list and the values: ACE refuses a read-only database on finding the table (verified).
        _database.RefuseIfReadOnly(ReadOnlyWrite.Row);
        var columns = table.Definition.Columns;

        // Target columns: the explicit list; DEFAULT VALUES provides none (every column takes its default);
        // otherwise a bare VALUES (…) targets all columns in order.
        IReadOnlyList<string> targets = statement.Columns.Count > 0
            ? statement.Columns
            : statement.DefaultValues
                ? []
                : columns.Select(c => c.Name).ToList();

        var evaluator = new ExpressionEvaluator(
            new EvalScope([], [], null), _scalarRunner, parameters: _parameters);
        RowDefaults defaults = DefaultsOf(table.Definition);

        int affected = 0;
        object? lastIdentity = null;

        // One row, given the values already in target order — the two append forms differ only in where
        // those come from: evaluated VALUES expressions, or a row of the source query's output.
        void InsertRow(ReadOnlySpan<object?> supplied)
        {
            if (supplied.Length != targets.Count)
                throw new InvalidOperationException(
                    $"INSERT has {supplied.Length} values but {targets.Count} target columns.");

            var values = new object?[columns.Count];
            var provided = new List<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                ColumnDef column = table.Definition.FindColumn(targets[i])
                    ?? throw new InvalidOperationException($"Column '{targets[i]}' does not exist in '{statement.Table}'.");

                // An explicit DEFAULT is not a value: leaving the column out of `provided` routes it through
                // InsertNewRow's default filling, so it takes its declared default, or stays NULL when it has
                // none — which is what the standard specifies. A NOT NULL column with no default then fails
                // EnforceRequired, as it should. An AutoNumber column takes its generated id, the same as it
                // would from INSERT INTO t DEFAULT VALUES.
                if (ReferenceEquals(supplied[i], DefaultRowValue))
                    continue;
                // An explicit number is kept and the counter carries on after it; an explicit Null is refused
                // (verified vs ACE) — to the row inserter a Null means "generate the next id".
                if (supplied[i] is null && column.IsAutoNumber && column.Type == JetDataType.Int32)
                    throw new InvalidOperationException(
                        $"Cannot insert a Null value into AutoNumber field '{statement.Table}.{column.Name}'.");

                values[column.Index] = supplied[i];
                if (!provided.Contains(column.Index)) provided.Add(column.Index);
            }

            object? identity = InsertNewRow(statement.Table, table, defaults, values, provided);
            if (defaults.AutoNumber is not null)
                lastIdentity = identity;
            affected++;
        }

        if (statement.Source is not null)
        {
            // The multiple-record form. The source is MATERIALISED before a single row is written: appending
            // a table to itself otherwise feeds its own output back into the scan and never terminates.
            // Access's INSERT INTO t SELECT * FROM t doubles the table and stops, so the read completes
            // before the write begins.
            ResultSet source = _scalarRunner.ExecuteQuery(Planning.ColumnPruning.Apply(
                Planning.IndexSelection.Apply(Planning.QueryPlanner.PlanStatement(statement.Source), _database)));
            var rows = source.Rows.ToList();

            // With no column list the source's output NAMES choose the target columns — ACE resolves by name,
            // not by position. Measured, because the two only disagree when they disagree silently:
            //   INSERT INTO PDst SELECT B AS Name, A AS Id FROM PSrc
            // stores Id=7, Name='seven' — the values routed by their aliases, not by the order they appear
            // in. Positionally that would have put 'seven' in Id. ACE rejects a name the target lacks
            // ("unknown field name: 'A'"), including through SELECT *, and FindColumn below does the same.
            //
            // An explicit column list is the other rule entirely: it names the targets and the source's
            // values map positionally onto IT, whatever the source calls them.
            if (statement.Columns.Count == 0)
                targets = source.ColumnNames;

            foreach (object?[] row in rows) InsertRow(row);
        }
        else
        {
            foreach (IReadOnlyList<Expression> rowExprs in statement.Rows)
            {
                var supplied = new object?[rowExprs.Count];
                for (int i = 0; i < rowExprs.Count; i++)
                    // DEFAULT is a marker, not something to evaluate — it is carried through as a sentinel so
                    // InsertRow can tell "take the column's default" apart from any value, NULL included.
                    supplied[i] = rowExprs[i] is DefaultValueExpression
                        ? DefaultRowValue
                        : evaluator.Evaluate(rowExprs[i]);
                InsertRow(supplied);
            }
        }

        // Publish @@ROWCOUNT (rows this insert affected) and @@IDENTITY (the last AutoNumber generated) so a
        // following SELECT in the same batch can read the store-generated key back — the shape EF Core emits.
        // @@IDENTITY is connection-scoped and only overwritten by an insert that actually generates an id,
        // so an insert into a keyless table leaves the previous value intact (matching Access).
        if (_session is not null)
        {
            _session.RowCount = affected;
            if (defaults.AutoNumber is not null)
                _session.LastIdentity = lastIdentity;
        }
        return affected;
    }

    /// <summary>A table participating in an UPDATE/DELETE source: its alias, columns (alias-qualified, for the
    /// combined evaluation scope), and its rows. A physical table exposes <see cref="Table"/> (rows have real
    /// <see cref="RowId"/>s and can be a SET/DELETE target); a derived table (a subquery in the source) has
    /// <see cref="Table"/> null and its already-materialised <see cref="DerivedRows"/> (never a target).</summary>
    /// <param name="Alias">The name the source is reached by in the statement.</param>
    /// <param name="Table">The physical table, or null for a derived one.</param>
    /// <param name="Columns">Its columns, alias-qualified.</param>
    /// <param name="DerivedRows">A derived table's already-materialised rows; null for a physical table.</param>
    /// <param name="Lateral">The plan of a LATERAL source — an APPLY's right side — which is re-executed once
    /// per outer row instead of being materialised, because it may correlate to the rows joined before it.</param>
    /// <remarks>A writable derived table is one source table per table it reads, in a run: the first carries the
    /// joined rows the derived query chose (<see cref="Combos"/>, <see cref="ComboWidth"/> tables wide) and the rest
    /// are <see cref="InCombo"/>, filled from them. <see cref="CacheKey"/> tells a table's rows apart from another
    /// table's under the same alias.</remarks>
    private sealed record SourceTable(string Alias, Table? Table, List<OutputColumn> Columns,
        IReadOnlyList<object?[]>? DerivedRows, Plan.PlanNode? Lateral = null)
    {
        public IReadOnlyList<(RowId Id, object?[] Values)[]>? Combos { get; init; }
        public int ComboWidth { get; init; } = 1;
        public bool InCombo { get; init; }
        public string? Key { get; init; }
        public string CacheKey => Key ?? Alias;
    }

    /// <summary>How a join kind is handled in a DML source.</summary>
    private enum JoinShape
    {
        /// <summary>Left-deep: walk the left subtree, then the right side tagged with this join.</summary>
        Conditional,
        /// <summary>RIGHT JOIN, modelled as the right side preserved with the left LEFT-joined onto it.</summary>
        RightPreserving,
        /// <summary>An APPLY: the right side is correlated and re-executed per outer row.</summary>
        Lateral,
        Unsupported,
    }

    /// <summary>
    ///     Deliberately a switch <b>expression</b> with no discard arm, so adding a <see cref="JoinKind"/> is a
    ///     BUILD error here rather than a runtime refusal for one query shape.
    /// </summary>
    /// <remarks>
    ///     A join kind has to be taught to two executors — the query one and this one — because DML tracks row
    ///     identity that the query pipeline discards (see <c>QueryExecutor.ExecuteCorrelated</c>). The
    ///     duplication is unavoidable; forgetting the second half is not, and this is what makes it so.
    /// </remarks>
    // CS8524 is the "value not a named member" case — a cast like (JoinKind)7, which the parser cannot produce.
    // Silencing only that keeps CS8509, which is the one that matters: with no discard arm, adding a named
    // JoinKind fails THIS build. A `_ =>` arm would satisfy both and quietly defeat the whole point.
#pragma warning disable CS8524
    private static JoinShape ShapeOf(JoinKind kind) => kind switch
    {
        JoinKind.Inner or JoinKind.Cross or JoinKind.Left => JoinShape.Conditional,
        JoinKind.Right => JoinShape.RightPreserving,
        JoinKind.CrossApply or JoinKind.OuterApply => JoinShape.Lateral,
        JoinKind.Full => JoinShape.Unsupported,
    };
#pragma warning restore CS8524

    /// <summary>Flattens the UPDATE/DELETE table source into its tables in order, each paired with the join that
    /// introduced it: its <see cref="JoinKind"/> and ON condition (the first/base table is Inner with a null ON).
    /// INNER/CROSS/LEFT joins over named tables are supported — the left-deep form EF and Access emit — and the
    /// bracketed groups WalkGroup describes. A table's group base, when it has one, is the table it is null-extended
    /// with.</summary>
    private (List<SourceTable> Tables, List<JoinKind> Kinds, List<Expression?> Ons, List<int?> GroupBases) ResolveSource(
        TableReference from)
    {
        var tables = new List<SourceTable>();
        var kinds = new List<JoinKind>();
        var ons = new List<Expression?>();
        var groupBases = new List<int?>();

        void EmitTable(NamedTable n, JoinKind kind, Expression? on)
        {
            Table t = _database.OpenTable(n.Name);
            string alias = n.Alias ?? n.Name;
            tables.Add(new SourceTable(alias, t, t.Definition.Columns.Select(c => OutputColumn.Of(alias, t.Definition, c)).ToList(), null));
            kinds.Add(kind);
            ons.Add(on);
            groupBases.Add(null);
        }

        void EmitDerived(SubqueryTable sq, JoinKind kind, Expression? on)
        {
            if (WritableDerivedTable(sq) is { } writable)
            {
                // The ON and join kind go with the run's first table, which brings in all of the run's rows.
                foreach (SourceTable table in writable)
                {
                    tables.Add(table);
                    kinds.Add(table.InCombo ? JoinKind.Inner : kind);
                    ons.Add(table.InCombo ? null : on);
                    groupBases.Add(null);
                }
                return;
            }

            // Without an alias a derived table can only be the one being written, which this one cannot be.
            string alias = sq.Alias ?? throw new NotSupportedException(
                "Operation must use an updateable query: a derived table written to must select from one table, "
                + "with no grouping or DISTINCT.");
            var (columns, rows) = ExecuteDerivedSource(sq.Query, alias);
            tables.Add(new SourceTable(alias, null, QueryExecutor.DerivedColumns(columns, alias, sq.Columns), rows));
            kinds.Add(kind);
            ons.Add(on);
            groupBases.Add(null);
        }

        void EmitLateral(SubqueryTable sq, JoinKind kind, Expression? on)
        {
            string alias = sq.Alias ?? throw new NotSupportedException("A lateral join in an UPDATE/DELETE source requires an alias.");

            // The tables joined so far are this one's outer scope: pass their aliases to index selection so a
            // correlated predicate becomes a seek rather than a rescan, which matters here more than anywhere
            // because the body runs once per outer row.
            var outerColumns = tables.SelectMany(t => t.Columns).ToList();
            Plan.PlanNode plan = Planning.ColumnPruning.Apply(Planning.IndexSelection.Apply(
                Planning.QueryPlanner.PlanStatement(sq.Query), _database,
                tables.Select(t => t.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase)));

            // The rows vary per outer row but the schema does not, and JoinRows needs it before reading any row.
            // Probe once against an all-null outer row and drop the rows unread, as QueryExecutor.ExecuteApply does.
            var (columns, _) = _scalarRunner.ExecuteCorrelated(
                plan, new EvalScope(outerColumns, new object?[outerColumns.Count], null));

            tables.Add(new SourceTable(alias, null, QueryExecutor.DerivedColumns(columns, alias, sq.Columns), null, plan));
            kinds.Add(kind);
            ons.Add(on);
            groupBases.Add(null);
        }

        void Walk(TableReference r, JoinKind kind, Expression? on)
        {
            switch (r)
            {
                case NamedTable n:
                    EmitTable(n, kind, on);
                    break;
                case SubqueryTable sq:
                    EmitDerived(sq, kind, on);
                    break;
                case JoinTable j:
                    WalkJoin(j);
                    break;
                default:
                    throw new NotSupportedException($"UPDATE/DELETE over a {r.GetType().Name} source is not supported yet.");
            }
        }

        void WalkJoin(JoinTable j)
        {
            switch (ShapeOf(j.Kind))
            {
                // Left-deep: the left subtree carries its own joins (its base is the source's first table, Inner
                // with no ON); the right side is the newly joined named/derived table, tagged with this join.
                case JoinShape.Conditional when j.Right is NamedTable or SubqueryTable:
                    Walk(j.Left, JoinKind.Inner, null);
                    Walk(j.Right, j.Kind, j.On);
                    break;

                case JoinShape.Conditional when j.Right is JoinTable group:
                    WalkGroup(j, group);
                    break;

                // RIGHT JOIN keeps the right side: model it as the right side preserved (the base) with the left
                // side LEFT-joined onto it. The SET/DELETE target is usually that left side — which then becomes
                // the nullable side, so a right row with no match yields a null target that the WHERE drops.
                case JoinShape.RightPreserving when j.Left is NamedTable or SubqueryTable:
                    Walk(j.Right, JoinKind.Inner, null);
                    Walk(j.Left, JoinKind.Left, j.On);
                    break;

                // A named table on the right of an APPLY cannot correlate to anything, so it is exactly a CROSS
                // (or, for OUTER APPLY, a LEFT with no condition) and takes the ordinary path.
                case JoinShape.Lateral when j.Right is NamedTable:
                    Walk(j.Left, JoinKind.Inner, null);
                    Walk(j.Right, j.Kind == JoinKind.OuterApply ? JoinKind.Left : JoinKind.Cross, null);
                    break;

                case JoinShape.Lateral when j.Right is SubqueryTable sq:
                    Walk(j.Left, JoinKind.Inner, null);
                    EmitLateral(sq, j.Kind, j.On);
                    break;

                default:
                    throw new NotSupportedException(
                        $"UPDATE/DELETE over a {j.Kind} join of this shape is not supported yet.");
            }
        }

        // A join onto a bracketed group — UPDATE a, b INNER JOIN c ON … parses as a CROSS (b INNER c) — lays the
        // group's tables out after the left side's, which gives the same rows in the shapes ACE accepts (verified):
        //  - an inner or cross join whose ON reads only the tables before the group's first outer join, checked once
        //    those have joined (a INNER JOIN (b LEFT JOIN c) ON a.x = b.x);
        //  - a LEFT join onto a table followed only by LEFT joins, whose ON reads only that first table; when the
        //    first table is null-extended so is the rest of the group (a LEFT JOIN (b LEFT JOIN c) ON a.x = b.x).
        // ACE refuses the others ("JOIN expression not supported"), a LEFT join onto an inner join among them.
        void WalkGroup(JoinTable j, JoinTable group)
        {
            Walk(j.Left, JoinKind.Inner, null);
            int first = tables.Count;
            Walk(group, JoinKind.Inner, null);
            int end = tables.Count;

            // The group's tables as units: a writable derived table's run is one, joined and null-extended whole
            // (verified vs ACE, which accepts a derived join anywhere a table can be in these shapes).
            List<int> units = Enumerable.Range(first, end - first).Where(t => !tables[t].InCombo).ToList();
            int innerUnits = 1;
            while (innerUnits < units.Count && kinds[units[innerUnits]] is JoinKind.Inner or JoinKind.Cross)
                innerUnits++;
            int innerEnd = innerUnits < units.Count ? units[innerUnits] : end;
            bool left = j.Kind == JoinKind.Left;
            HashSet<string> readable = tables.Take(left ? first + tables[first].ComboWidth : innerEnd)
                .Select(t => t.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool onFits = j.On is null || Planning.IndexSelection.ReferencesOnly(j.On, readable);

            if (!left && onFits)
            {
                int last = units[innerUnits - 1];
                if (j.On is not null)
                {
                    ons[last] = ons[last] is { } own ? new BinaryExpression(BinaryOperator.And, own, j.On) : j.On;
                    kinds[last] = JoinKind.Inner;
                }
            }
            else if (left && onFits && innerUnits == 1 && units.Skip(1).All(u => kinds[u] == JoinKind.Left))
            {
                kinds[first] = JoinKind.Left;
                ons[first] = j.On;
                for (int i = first + tables[first].ComboWidth; i < end; i++)
                    groupBases[i] ??= first;
            }
            else
            {
                throw new NotSupportedException($"UPDATE/DELETE over a {j.Kind} join onto this group is not supported.");
            }
        }

        Walk(from, JoinKind.Inner, null);
        return (tables, kinds, ons, groupBases);
    }

    /// <summary>
    /// A derived table an UPDATE or DELETE can write through, as ACE allows (verified: UPDATE (SELECT TOP 2 * FROM t
    /// ORDER BY Id DESC) SET x = 1 updates those two rows of t, and DELETE * FROM (SELECT …) deletes them): a SELECT
    /// filtered, ordered and cut by its TOP or OFFSET, with no grouping or DISTINCT, over a table or a join. Its rows
    /// are its tables' rows, so they can be written back: it becomes one source table per table it reads, whose
    /// rows come together, as the query chose them. Without an alias it takes the name of the one table it reads.
    /// <para>Only the columns it selects can be read or set, under the names it gives them (verified vs ACE: SELECT X
    /// AS Y makes SET Y write X, and a column not selected is unknown). A computed item neither stops the rest being
    /// written nor can itself be read. Null for any other query.</para>
    /// </summary>
    private List<SourceTable>? WritableDerivedTable(SubqueryTable sq)
    {
        // A column list (which ACE has no syntax for) and WITH TIES leave it a query that is only read.
        if (sq.Columns is not null
            || sq.Query is not SelectStatement
            {
                From: NamedTable or JoinTable, GroupBy.Count: 0, Having: null, Distinct: false, Into: null, WithTies: false,
            } select
            || select.Projection.Any(item => item.Value is StarExpression or QualifiedStarExpression && item.Alias is not null))
            return null;

        var (inner, kinds, ons, groupBases) = ResolveSource(select.From);
        var innerColumns = inner.SelectMany(t => t.Columns).ToList();

        // The name each column goes by, per table and by position; a column the query does not select has none.
        var names = inner.Select(t => new string?[t.Columns.Count]).ToArray();
        void Name(int table, int column, string name) => names[table][column] = name;
        if (select.IsSelectStar)
            for (int t = 0; t < inner.Count; t++)
                for (int c = 0; c < inner[t].Columns.Count; c++) Name(t, c, inner[t].Columns[c].Name);
        foreach (SelectItem item in select.Projection)
        {
            if (item.Value is ColumnReference reference)
            {
                // The one table's column it names; an ambiguous or unknown one exposes nothing.
                var found = FindColumns(inner, reference.Table, reference.Column);
                if (found.Count == 1)
                    Name(found[0].Table, found[0].Position, item.Alias ?? inner[found[0].Table].Columns[found[0].Position].Name);
                continue;
            }
            for (int t = 0; t < inner.Count; t++)
            {
                if (item.Value is StarExpression
                    || item.Value is QualifiedStarExpression star && string.Equals(star.Table, inner[t].Alias, StringComparison.OrdinalIgnoreCase))
                {
                    for (int c = 0; c < inner[t].Columns.Count; c++) names[t][c] ??= inner[t].Columns[c].Name;
                }
            }
        }
        ExpressionEvaluator Over(object?[] values) =>
            new(new EvalScope(innerColumns, values, null), _scalarRunner, _parameters, _session);

        var rows = JoinRows(inner, kinds, ons, groupBases, select.Where, innerColumns);
        object?[] Flat((RowId Id, object?[] Values)[] combo) => combo.SelectMany(c => c.Values).ToArray();
        if (select.OrderBy.Count > 0)
        {
            // Stable, as a SELECT's ORDER BY is: rows with equal keys keep the order they were read in.
            rows = rows
                .Select(row => (Row: row, Keys: select.OrderBy.Select(o => Over(Flat(row)).Evaluate(o.Value)).ToArray()))
                .OrderBy(keyed => keyed.Keys, Comparer<object?[]>.Create((a, b) =>
                {
                    for (int i = 0; i < a.Length; i++)
                    {
                        int order = ExpressionEvaluator.CompareForSort(a[i], b[i], _scalarRunner.TextComparer);
                        if (order != 0)
                            return select.OrderBy[i].Direction == SortDirection.Descending ? -order : order;
                    }
                    return 0;
                }))
                .Select(keyed => keyed.Row)
                .ToList();
        }

        var counts = new ExpressionEvaluator(new EvalScope([], [], null), _scalarRunner, _parameters, _session);
        int Count(Expression e) => Convert.ToInt32(counts.Evaluate(e), System.Globalization.CultureInfo.InvariantCulture);
        if (select.Offset is { } offset)
            rows = rows.Skip(Math.Max(0, Count(offset))).ToList();
        if (select.Top is { } top)
        {
            // TOP n PERCENT takes ceil(rows × n / 100), as a SELECT does.
            int n = Count(top);
            rows = rows.Take(select.TopPercent ? (int)(((long)rows.Count * n + 99) / 100) : Math.Max(0, n)).ToList();
        }

        // A column without a name stays in the row, where writing it back needs it, under a name no SQL can write.
        string alias = sq.Alias ?? (inner.Count == 1 ? inner[0].Alias : "\0derived");
        return inner.Select((t, i) => t with
        {
            Alias = alias,
            Key = $"{alias}\0{t.CacheKey}",
            Columns = t.Columns.Select((c, position) => c with { Qualifier = alias, Name = names[i][position] ?? HiddenColumn }).ToList(),
            Combos = i == 0 ? rows : null,
            ComboWidth = i == 0 ? inner.Count : 1,
            InCombo = i > 0,
        }).ToList();
    }

    private const string HiddenColumn = "\0hidden";

    /// <summary>The table and position of every source column named <paramref name="column"/>: among the tables
    /// <paramref name="qualifier"/> names — by alias, or failing that by table name — or among all of them.</summary>
    private static List<(int Table, int Position)> FindColumns(List<SourceTable> tables, string? qualifier, string column)
    {
        IEnumerable<int> candidates = qualifier is null ? Enumerable.Range(0, tables.Count) : NamedTables(tables, qualifier);
        var found = new List<(int, int)>();
        foreach (int t in candidates)
            for (int c = 0; c < tables[t].Columns.Count; c++)
                if (string.Equals(tables[t].Columns[c].Name, column, StringComparison.OrdinalIgnoreCase))
                    found.Add((t, c));
        return found;
    }

    /// <summary>Runs a derived-table subquery (uncorrelated — a FROM/JOIN source) through the full query
    /// pipeline (index selection included) and materialises its rows, with columns qualified by the derived
    /// table's alias for the combined evaluation scope.</summary>
    private (List<OutputColumn> Columns, List<object?[]> Rows) ExecuteDerivedSource(SqlStatement query, string alias)
    {
        // PlanStatement, not PlanSelect: EF puts a set operation here for Union/Except/Intersect/Concat before
        // an ExecuteUpdate, and a table value constructor for an inline collection. Same widening as the
        // subquery predicates needed, for the same reason - a derived table is a query expression.
        var plan = Planning.ColumnPruning.Apply(
            Planning.IndexSelection.Apply(Planning.QueryPlanner.PlanStatement(query), _database));
        ResultSet result = _scalarRunner.ExecuteQuery(plan);
        var columns = result.ColumnNames.Select((name, i) => new OutputColumn(alias, name, result.ColumnTypes[i])).ToList();
        return (columns, result.Rows.ToList());
    }

    /// <summary>
    /// Materialises the join rows of the source: each is the per-table (row id + <b>shared</b> value array),
    /// in table order, that satisfies all ON conditions and the WHERE. A physical row's value array is shared
    /// across every join row it appears in (cached by alias+row id), so a SET that references the row's own
    /// value accumulates across matches — matching Access (e.g. a "one"-side counter incremented per match).
    /// </summary>
    private List<(RowId Id, object?[] Values)[]> JoinRows(
        List<SourceTable> tables, List<JoinKind> kinds, List<Expression?> ons, List<int?> groupBases, Expression? where,
        IReadOnlyList<OutputColumn> columns)
    {
        // A physical row's value array is shared across every join row it appears in (see the method summary).
        var cache = new Dictionary<(string, RowId), object?[]>();
        object?[] Shared(string alias, RowId id, object?[] values)
        {
            var key = (alias, id);
            if (!cache.TryGetValue(key, out object?[]? s)) cache[key] = s = values;
            return s;
        }

        // Access's multi-table UPDATE/DELETE joins are equi-joins on keys; for each table after the first, if its
        // ON equates one of its single-column-indexed columns to a column of an earlier table, seek it by that
        // key instead of scanning it in full — turning an O(∏ rows) cartesian product into an index-nested-loop.
        var seekPlan = new (IndexDef Index, Expression Key)?[tables.Count];
        for (int i = 1; i < tables.Count; i++)
            seekPlan[i] = SeekPlanFor(i, tables, ons[i]);

        // The first table has no ON to seek from, but a single-table UPDATE/DELETE's WHERE is the same shape:
        // a conjunct equating one of its indexed columns to a constant is an access path. Safe because the
        // WHERE is re-checked on the assembled row below, so an over-returning (collation-lossy) seek stays
        // correct, and a row the seek skips is one the WHERE would have rejected anyway. Restricted to the
        // single-table case: with a join, an UNQUALIFIED column in the WHERE could belong to another table.
        if (tables.Count == 1)
            seekPlan[0] = SeekPlanFor(0, tables, where);

        // Only the columns the statement names are decoded; the rows it then writes are read again in full by
        // CompleteRows before anything is written.
        bool[]?[] decode = [.. tables.Select(t => t.Table is { } table ? Planning.ColumnPruning.Mask(table.Definition, _readNames) : null)];

        // Precompute the accumulated columns visible when seeking/evaluating an ON at each depth.
        var colsUpTo = new List<OutputColumn>[tables.Count + 1];
        colsUpTo[0] = [];
        for (int i = 0; i < tables.Count; i++)
            colsUpTo[i + 1] = [.. colsUpTo[i], .. tables[i].Columns];

        var result = new List<(RowId, object?[])[]>();
        var acc = new (RowId, object?[])[tables.Count];
        var nullExtended = new bool[tables.Count];

        // A writable derived table's run of tables is null-extended, like its rows are joined, together.
        void NullExtend(int i)
        {
            int width = tables[i].ComboWidth;
            for (int k = i; k < i + width; k++)
            {
                acc[k] = (default, new object?[tables[k].Columns.Count]);
                nullExtended[k] = true;
            }
            Recurse(i + width);
        }

        bool Holds(Expression? predicate, int depth) =>
            predicate is null || new ExpressionEvaluator(
                new EvalScope(colsUpTo[depth], Flatten(acc, depth), null), _scalarRunner, _parameters, _session)
                .Evaluate(predicate) is true;

        void Recurse(int i)
        {
            if (i == tables.Count)
            {
                if (Holds(where, tables.Count))
                    result.Add(((RowId, object?[])[])acc.Clone());
                return;
            }

            // A table inside a LEFT-joined group has no row when the group's first table has none.
            if (groupBases[i] is { } groupBase && nullExtended[groupBase])
            {
                NullExtend(i);
                return;
            }
            nullExtended[i] = false;

            // A writable derived table's rows, already joined, filtered, ordered and cut: each fills its run of
            // tables at once, and this table's ON is checked once all of them are in.
            if (tables[i].Combos is { } combos)
            {
                int width = tables[i].ComboWidth;
                bool comboMatched = false;
                foreach ((RowId, object?[])[] combo in combos)
                {
                    for (int k = 0; k < width; k++)
                    {
                        acc[i + k] = combo[k];
                        nullExtended[i + k] = false;
                    }
                    if (Holds(ons[i], i + width))
                    {
                        comboMatched = true;
                        Recurse(i + width);
                    }
                }
                if (kinds[i] is JoinKind.Left or JoinKind.OuterApply && !comboMatched)
                    NullExtend(i);
                return;
            }

            // A derived table's rows are already materialised and have no RowId (never a target). A physical
            // table is seeked when its ON allows (index-nested-loop), else scanned.
            IEnumerable<(RowId Id, object?[] Values)> rows;
            if (tables[i].Lateral is { } lateral)
            {
                // What makes it lateral: re-executed for THIS outer row, with the rows joined so far in scope,
                // rather than materialised once. Checked before the derived case, which it also looks like.
                var (_, lateralRows) = _scalarRunner.ExecuteCorrelated(
                    lateral, new EvalScope(colsUpTo[i], Flatten(acc, i), null));
                rows = lateralRows.Select(v => (default(RowId), v));
            }
            else if (tables[i].Table is null)
            {
                rows = tables[i].DerivedRows!.Select(v => (default(RowId), v));
            }
            else if (seekPlan[i] is { } p)
            {
                var keyEval = new ExpressionEvaluator(new EvalScope(colsUpTo[i], Flatten(acc, i), null), _scalarRunner, _parameters, _session);
                var keyValues = new object?[tables[i].Table!.Definition.Columns.Count];
                keyValues[p.Index.Columns[0].Column.Index] = keyEval.Evaluate(p.Key);
                rows = tables[i].Table!.SeekRowsWithIds(p.Index, keyValues, decode[i]);
            }
            else
            {
                rows = tables[i].Table!.Rows(decode[i]).WithIds();
            }

            // The ON (also the seek's residual re-check — index keys can over-return) gates each candidate.
            bool matched = false;
            foreach ((RowId id, object?[] values) in rows)
            {
                // Share a physical row's array across the combos it appears in (counter-accumulation semantics);
                // a derived row has no identity to share on, so use it directly.
                acc[i] = tables[i].Table is null ? (id, values) : (id, Shared(tables[i].CacheKey, id, values));
                if (Holds(ons[i], i + 1))
                {
                    matched = true;
                    Recurse(i + 1);
                }
            }

            // LEFT join, and OUTER APPLY for the same reason: an outer row with no matching inner row is still
            // emitted, with the inner side all-null.
            if (kinds[i] is JoinKind.Left or JoinKind.OuterApply && !matched)
                NullExtend(i);
        }

        Recurse(0);
        return result;
    }

    // The column names the UPDATE or DELETE being executed reads anywhere (ON, WHERE, SET values, subqueries, a
    // derived source), which is all JoinRows decodes; null decodes every column.
    private HashSet<string>? _readNames;

    /// <summary>
    /// Reads each row the statement is about to write in full. JoinRows decoded only the columns the statement
    /// names, which is all that choosing the rows and evaluating the SETs look at — but a row is rewritten, its
    /// constraints checked and its index entries moved from all of its values. The rest are filled into the same
    /// array, which every joined row of that physical row shares, before any of it is read.
    /// </summary>
    private void CompleteRows(List<(RowId Id, object?[] Values)[]> joinRows, List<SourceTable> tables, IEnumerable<int> targets)
    {
        int[] pruned = [.. targets.Distinct().Where(t =>
            tables[t].Table is { } table && Planning.ColumnPruning.Mask(table.Definition, _readNames) is not null)];
        if (pruned.Length == 0) return;

        var done = new HashSet<object?[]>(ReferenceEqualityComparer.Instance);
        foreach (var combo in joinRows)
            foreach (int ti in pruned)
            {
                if (IsNullExtended(combo[ti]) || !done.Add(combo[ti].Values)) continue;
                object?[] full = tables[ti].Table!.GetRow(combo[ti].Id)
                    ?? throw new InvalidOperationException($"Row {combo[ti].Id} of '{tables[ti].Table!.Name}' vanished while it was being read.");
                Array.Copy(full, combo[ti].Values, full.Length);
            }
    }

    /// <summary>Concatenates the value arrays of the first <paramref name="count"/> accumulated join rows.</summary>
    private static object?[] Flatten((RowId, object?[])[] acc, int count)
    {
        var flat = new List<object?>();
        for (int i = 0; i < count; i++) flat.AddRange(acc[i].Item2);
        return [.. flat];
    }

    /// <summary>If table <paramref name="i"/>'s own join <paramref name="on"/> has an equality between one of its
    /// single-column-indexed columns and an expression over the earlier tables, returns that index and key
    /// expression (to seek it per outer row); else null (scan it).</summary>
    private static (IndexDef Index, Expression Key)? SeekPlanFor(int i, List<SourceTable> tables, Expression? on)
    {
        // A derived table has no index to seek, and a writable one's rows are already chosen.
        if (on is null || tables[i].Table is null || tables[i].Combos is not null || tables[i].InCombo) return null;
        TableDefinition def = tables[i].Table!.Definition;
        string alias = tables[i].Alias;
        HashSet<string> earlier = tables.Take(i).Select(t => t.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (Expression conjunct in Conjuncts(on))
        {
            if (conjunct is not BinaryExpression { Operator: BinaryOperator.Equal } eq)
                continue;
            if (MatchSeek(eq.Left, eq.Right, alias, def, earlier) is { } a) return a;
            if (MatchSeek(eq.Right, eq.Left, alias, def, earlier) is { } b) return b;
        }
        return null;
    }

    private static IEnumerable<Expression> Conjuncts(Expression e) =>
        e is BinaryExpression { Operator: BinaryOperator.And } b
            ? Conjuncts(b.Left).Concat(Conjuncts(b.Right))
            : [e];

    private static (IndexDef Index, Expression Key)? MatchSeek(
        Expression colSide, Expression keySide, string alias, TableDefinition def, HashSet<string> earlier)
    {
        if (colSide is not ColumnReference c
            || (c.Table is { } t && !string.Equals(t, alias, StringComparison.OrdinalIgnoreCase))
            || !def.Columns.Any(cd => string.Equals(cd.Name, c.Column, StringComparison.OrdinalIgnoreCase)))
            return null;

        IndexDef? index = def.Indexes.FirstOrDefault(ix => ix.RootPage > 0 && ix.Columns.Count == 1
            && string.Equals(ix.Columns[0].Column.Name, c.Column, StringComparison.OrdinalIgnoreCase));
        return index is not null && IndexSelection.ReferencesOnly(keySide, earlier) ? (index, keySide) : null;
    }

    /// <summary>The tables a qualifier names: by alias, or failing that by table name.</summary>
    private static List<int> NamedTables(List<SourceTable> tables, string qualifier)
    {
        var byAlias = Enumerable.Range(0, tables.Count)
            .Where(t => string.Equals(tables[t].Alias, qualifier, StringComparison.OrdinalIgnoreCase)).ToList();
        return byAlias.Count > 0
            ? byAlias
            : Enumerable.Range(0, tables.Count)
                .Where(t => string.Equals(tables[t].Table?.Name, qualifier, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The source table a DELETE removes rows from: the one its <c>target.*</c> names, or the only one (a
    /// join without a target is ambiguous). A writable derived table over a join is never one, as in ACE (verified:
    /// "Could not delete from specified tables").</summary>
    private static int DeleteTarget(List<SourceTable> tables, string? qualifier)
    {
        List<int> named = qualifier is null ? [.. Enumerable.Range(0, tables.Count)] : NamedTables(tables, qualifier);
        if (named.Any(t => tables[t].ComboWidth > 1 || tables[t].InCombo))
            throw new InvalidOperationException("Could not delete from specified tables.");
        if (qualifier is null)
            return tables.Count == 1 ? 0
                : throw new InvalidOperationException("DELETE target must be table-qualified when the statement joins several tables.");
        return named.Count > 0
            ? named[0]
            : throw new InvalidOperationException($"DELETE target '{qualifier}' is not one of the statement's tables.");
    }

    /// <summary>The physical <see cref="Table"/> a SET/DELETE targets — a derived table (subquery source) has no
    /// rows to write back, so targeting one is rejected.</summary>
    private static Table TargetTable(List<SourceTable> tables, int ti) =>
        tables[ti].Table ?? throw new NotSupportedException($"Cannot UPDATE/DELETE the derived table '{tables[ti].Alias}'.");

    /// <summary>
    /// Executes UPDATE tableexpression SET col = expr, … [WHERE criteria]. The table expression may be a join,
    /// and each SET target may name a specific joined table (Access's multi-table update). The WHERE is an
    /// ordinary expression (correlated EXISTS included). Rows are rewritten in place (row id preserved).
    /// @@ROWCOUNT = matched join rows.
    /// <para>Every SET expression reads the joined row as it was before any of its SETs, so SET x = y, y = x
    /// swaps the two (verified vs ACE). A target row that several joined rows match is updated once per match, each
    /// match reading what the one before left (verified vs ACE: SET a.x = a.x + 1 over two matches adds 2). A column
    /// set twice is an error, as in ACE ("Duplicate output destination").</para>
    /// <para>A SET on the side of an outer join that a joined row has no match in writes a new row into that table,
    /// one per such joined row, holding the SET values and the table's defaults (verified vs ACE: SET b.v = 3 over
    /// a LEFT JOIN b adds a row with v = 3 for each unmatched row of the left table, even when the value is
    /// Null).</para>
    /// </summary>
    private int ExecuteUpdate(UpdateStatement statement)
    {
        _readNames = Planning.ColumnPruning.ReferencedNames(statement);
        try
        {
            return Update(statement);
        }
        finally
        {
            _readNames = null;
        }
    }

    private int Update(UpdateStatement statement)
    {
        var (tables, kinds, ons, groupBases) = ResolveSource(statement.From);
        var columns = tables.SelectMany(t => t.Columns).ToList();

        // Resolve each assignment to its (table index, column) once.
        var targets = statement.Assignments.Select(a =>
        {
            // By the name the source gives the column, which a writable derived table may change or leave out. An
            // unqualified name is looked for in every table, as ACE does, and must be in exactly one.
            var found = FindColumns(tables, a.Table, a.Column);
            if (found.Count > 1)
                throw new InvalidOperationException(
                    $"The specified field '{a.Column}' could refer to more than one table listed in the FROM clause.");
            if (found.Count == 0)
                throw new InvalidOperationException(a.Table is not null && NamedTables(tables, a.Table).Count == 0
                    ? $"UPDATE SET column '{a.Table}' is not one of the statement's tables."
                    : $"Column '{(a.Table is null ? "" : a.Table + ".")}{a.Column}' does not exist.");
            (int ti, int position) = found[0];
            Table tt = TargetTable(tables, ti);
            ColumnDef col = tt.Definition.Columns[position];
            // An AutoNumber takes no UPDATE at all, even to its own value (verified vs ACE for the Int32
            // counter; a ReplicationID AutoNumber is not measured, so it is left alone).
            if (col.IsAutoNumber && col.Type == JetDataType.Int32)
                throw new InvalidOperationException(
                    $"Cannot update '{tt.Name}.{col.Name}': an AutoNumber field cannot be updated.");
            return (TableIndex: ti, Column: col, a.Value);
        }).ToList();
        if (targets.GroupBy(t => (t.TableIndex, t.Column.Index)).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            throw new InvalidOperationException(
                $"Duplicate output destination '{tables[duplicate.Key.TableIndex].Alias}.{duplicate.First().Column.Name}'.");

        // Once the SET targets are resolved and before the joined rows are read: ACE refuses a read-only database
        // after a missing table or column and before the values it would write (verified).
        _database.RefuseIfReadOnly(ReadOnlyWrite.Row);
        List<(RowId Id, object?[] Values)[]> joinRows = JoinRows(tables, kinds, ons, groupBases, statement.Where, columns);
        CompleteRows(joinRows, tables, targets.Select(t => t.TableIndex));

        // Apply SETs to the shared value arrays; snapshot each touched row's original bytes on first touch.
        var dirty = new Dictionary<(string, RowId), (Table Table, RowId Id, object?[] Original, object?[] Values)>();
        // A null-extended side's value array is the joined row's own, so it keys the new row it becomes.
        var newRows = new Dictionary<object?[], (Table Table, List<int> Provided)>(ReferenceEqualityComparer.Instance);
        foreach (var combo in joinRows)
        {
            // Every SET of this joined row reads the row as it was before any of them.
            object?[] flat = combo.SelectMany(c => c.Values).ToArray();
            var eval = new ExpressionEvaluator(new EvalScope(columns, flat, null), _scalarRunner, _parameters, _session);
            object?[] results = targets.Select(t => eval.Evaluate(t.Value)).ToArray();

            for (int i = 0; i < targets.Count; i++)
            {
                (int ti, ColumnDef col, _) = targets[i];
                object?[] shared = combo[ti].Values;
                if (IsNullExtended(combo[ti]))
                {
                    if (!newRows.TryGetValue(shared, out var newRow))
                        newRows[shared] = newRow = (TargetTable(tables, ti), []);
                    if (!newRow.Provided.Contains(col.Index)) newRow.Provided.Add(col.Index);
                }
                else
                {
                    var key = (tables[ti].CacheKey, combo[ti].Id);
                    if (!dirty.ContainsKey(key)) dirty[key] = (TargetTable(tables, ti), combo[ti].Id, (object?[])shared.Clone(), shared);
                }
                shared[col.Index] = results[i];
            }
        }

        // The rows this statement is still going to write, by table and row id. A cascade that reaches one of
        // them must fold the new key into the values waiting here rather than write the row itself: the write
        // waiting here was built from the snapshot taken before the cascade, so it would put the old key back
        // while the index kept the cascaded one. A table whose parent and child ends are the same table is the
        // shape that does this. An entry is dropped once its row has been written, after which a cascade
        // reaching it writes the row as usual.
        var stillToWrite = new Dictionary<(int Tdef, RowId Id), object?[]>();
        foreach (var (table, id, _, values) in dirty.Values)
            stillToWrite[(table.Definition.DefinitionPage, id)] = values;

        foreach (var (table, id, original, values) in dirty.Values)
            UpdateRow(table, id, original, values, stillToWrite, cascaded: false);

        var newRowDefaults = new Dictionary<int, RowDefaults>();
        foreach (var (values, (table, provided)) in newRows)
        {
            if (!newRowDefaults.TryGetValue(table.Definition.DefinitionPage, out RowDefaults? defaults))
                newRowDefaults[table.Definition.DefinitionPage] = defaults = DefaultsOf(table.Definition);
            InsertNewRow(table.Name, table, defaults, values, provided);
        }

        int affected = joinRows.Count;
        if (_session is not null) _session.RowCount = affected;
        return affected;
    }

    /// <summary>Whether a joined row's entry for a table is the null row an outer join supplies where the table has
    /// no match. Row id 0/0 is never a data row: page 0 is the database header.</summary>
    private static bool IsNullExtended((RowId Id, object?[] Values) entry) => entry.Id.Equals(default(RowId));

    /// <summary>
    /// Executes DELETE [target.*] FROM tableexpression [WHERE criteria]. The table expression may be a join;
    /// <c>target.*</c> selects which joined table's rows to delete (defaults to the single table). Each
    /// matched target row's index entries are removed and the row is soft-deleted (row id kept, TDEF row
    /// count decremented). @@ROWCOUNT = distinct rows deleted, plus the joined rows where an outer join left the
    /// target without a row, which delete nothing but which ACE counts (verified).
    /// </summary>
    private int ExecuteDelete(DeleteStatement statement)
    {
        _readNames = Planning.ColumnPruning.ReferencedNames(statement);
        try
        {
            return Delete(statement);
        }
        finally
        {
            _readNames = null;
        }
    }

    private int Delete(DeleteStatement statement)
    {
        var (tables, kinds, ons, groupBases) = ResolveSource(statement.From);
        var columns = tables.SelectMany(t => t.Columns).ToList();

        int ti = DeleteTarget(tables, statement.TargetTable);
        Table target = TargetTable(tables, ti);
        // Once the target is resolved and before the joined rows are read, as ACE refuses a read-only database after
        // a missing table (verified).
        _database.RefuseIfReadOnly(ReadOnlyWrite.RowDelete);

        // With no WHERE over one table, ACE empties the table in one go rather than row by row — unless an enforced
        // relationship names it as the parent, when it deletes row by row as below (measured, whole-file diff:
        // WholeFileParityProbeTest). The test is the WHERE's absence, not every row matching: WHERE 1 = 1 deletes
        // row by row. So does a table with a CHECK constraint, pages and all, its checks evaluated as each row goes
        // (verified: whole-file diff, and a check that fails only once some rows are gone). A table holding a complex column
        // keeps the row path that follows its flat table's rows. No row is read but to be counted.
        if (statement.Where is null && statement.From is NamedTable
            && ChildRelationshipsOf(target.Name).Length == 0
            && target.Definition.CheckConstraints.Count == 0
            && !_database.Catalog.ComplexColumns.Any(c =>
                c.OwnerTable.DefinitionPage == target.Definition.DefinitionPage))
        {
            int rows = target.Rows(target.DecodeOnly([])).Count();
            target.Truncate();
            if (_session is not null) _session.RowCount = rows;
            return rows;
        }

        List<(RowId Id, object?[] Values)[]> joinRows = JoinRows(tables, kinds, ons, groupBases, statement.Where, columns);
        CompleteRows(joinRows, tables, [ti]);

        var deleted = new Dictionary<RowId, object?[]>();
        int withoutRow = 0;
        foreach (var combo in joinRows)
        {
            if (IsNullExtended(combo[ti]))
                withoutRow++;
            else
                deleted.TryAdd(combo[ti].Id, combo[ti].Values); // one delete per distinct target row
        }

        // Delete the distinct target rows and everything ON DELETE CASCADE reaches, children before parents.
        CascadeDelete(target, deleted.Select(kv => (kv.Key, kv.Value)));

        int affected = deleted.Count + withoutRow;
        if (_session is not null) _session.RowCount = affected;
        return affected;
    }
}