using LibRed.Catalog;
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

    public int Execute(SqlStatement statement) => statement switch
    {
        CreateTableStatement create => ExecuteCreateTable(create),
        CreateIndexStatement createIndex => ExecuteCreateIndex(createIndex),
        CreateViewStatement createView => ExecuteCreateView(createView),
        CreateProcedureStatement createProc => ExecuteCreateProcedure(createProc),
        CreateActionProcedureStatement actionProc => ExecuteCreateActionProcedure(actionProc),
        AlterTableStatement alter => ExecuteAlterTable(alter),
        InsertStatement insert => ExecuteInsert(insert),
        UpdateStatement update => ExecuteUpdate(update),
        DeleteStatement delete => ExecuteDelete(delete),
        _ => throw new NotSupportedException($"{statement.GetType().Name} cannot be executed as a non-query."),
    };

    private int ExecuteCreateTable(CreateTableStatement statement)
    {
        var columns = statement.Columns.Select(AccessTypeMapper.ToColumnSpec).ToList();
        IReadOnlyList<string>? primaryKey = statement.PrimaryKey.Count > 0 ? statement.PrimaryKey : null;

        var relationships = statement.ForeignKeys.Select(fk => new RelationshipSpec(
            Name: fk.Name ?? DefaultRelationshipName(statement.Table, fk),
            ReferencedTable: fk.ReferencedTable,
            Columns: PairColumns(fk),
            IsEnforced: true,
            CascadeUpdate: fk.OnUpdate == ReferentialAction.Cascade,
            CascadeDelete: fk.OnDelete == ReferentialAction.Cascade,
            NoIndex: fk.NoIndex)).ToList();

        var uniques = statement.UniqueConstraints.Select((u, i) => new UniqueIndexSpec(
            Name: u.Name ?? $"UQ_{statement.Table}_{i}",
            Columns: u.Columns)).ToList();

        var defaults = statement.Columns
            .Where(c => c.Default is not null)
            .Select(c => (c.Name, DefaultSql: c.Default!))
            .ToList();

        var checks = statement.CheckConstraints
            .Select((ck, i) => (Name: ck.Name ?? $"CK_{statement.Table}_{i}", ck.Expression))
            .ToList();

        _database.CreateTable(statement.Table, columns, primaryKey, relationships, uniques, defaults, checks);
        return 0;
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

    /// <summary>Pairs each child FK column with its referenced parent column, in key order.</summary>
    private static List<(string Column, string ReferencedColumn)> PairColumns(ForeignKeyConstraint fk)
    {
        if (fk.ReferencedColumns.Count != fk.Columns.Count)
            throw new InvalidOperationException(
                $"Foreign key on '{fk.ReferencedTable}' has {fk.Columns.Count} columns but references {fk.ReferencedColumns.Count}.");
        return fk.Columns.Zip(fk.ReferencedColumns).ToList();
    }

    /// <summary>Access-style fallback name when the constraint is unnamed: "childparent".</summary>
    private static string DefaultRelationshipName(string childTable, ForeignKeyConstraint fk) =>
        $"{fk.ReferencedTable}{childTable}";

    /// <summary>
    /// Rejects an insert whose foreign-key columns reference a non-existent parent row. Follows the SQL
    /// rule that a NULL in any FK column disables the check for that key. Only enforced relationships
    /// (grbit without "don't enforce") are checked.
    /// </summary>
    private void EnforceReferentialIntegrity(string childTable, Table table, object?[] values)
    {
        // TODO(self-pointing row): the parent is scanned BEFORE the new row is inserted, so a row that
        // references itself on a self-referencing FK (e.g. Mgr = its own Id) is wrongly rejected — the
        // referenced row does not exist yet. Access allows this. Fixing it needs the row's own key to be
        // considered part of the parent set for a self-reference (or deferred/post-insert checking).
        foreach (ForeignKey fk in _database.Catalog.ForeignKeysOf(childTable))
        {
            if (!fk.IsEnforced) continue;

            var target = new object?[fk.Columns.Count];
            bool anyNull = false;
            for (int i = 0; i < fk.Columns.Count; i++)
            {
                ColumnDef col = table.Definition.FindColumn(fk.Columns[i].Column)
                    ?? throw new InvalidOperationException($"Column '{fk.Columns[i].Column}' does not exist in '{childTable}'.");
                object? v = values[col.Index];
                if (v is null) { anyNull = true; break; }
                target[i] = v;
            }
            if (anyNull) continue;

            if (!ParentRowExists(fk, target))
                throw new InvalidOperationException(
                    $"INSERT into '{childTable}' violates foreign key '{fk.Name}': no matching row in '{fk.ReferencedTable}'.");
        }
    }

    /// <summary>Scans the parent table for a row whose referenced columns equal the child key values.</summary>
    private bool ParentRowExists(ForeignKey fk, object?[] target)
    {
        Table parent = _database.OpenTable(fk.ReferencedTable);
        int[] parentCols = fk.Columns.Select(c =>
            (parent.Definition.FindColumn(c.ReferencedColumn)
                ?? throw new InvalidOperationException($"Column '{c.ReferencedColumn}' does not exist in '{fk.ReferencedTable}'.")).Index)
            .ToArray();

        foreach (object?[] row in parent.Rows())
        {
            bool match = true;
            for (int i = 0; i < parentCols.Length; i++)
                if (ExpressionEvaluator.CompareForSort(row[parentCols[i]], target[i]) != 0) { match = false; break; }
            if (match) return true;
        }
        return false;
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
        _database.CreateView(statement.Name, BuildViewSpec(statement.Definition));
        return 0;
    }

    private int ExecuteCreateProcedure(CreateProcedureStatement statement)
    {
        // A procedure is a parameterized stored query: a view spec plus a parameter row per declared
        // parameter (name + Jet type code, resolved from the declared Access type name).
        var parameters = statement.Parameters
            .Select(p => new ViewParameterSpec(
                p.Name,
                (byte)AccessTypeMapper.ToColumnSpec(
                    new ColumnDefinition(p.Name, p.TypeName, null, null, false, false)).Type))
            .ToList();
        _database.CreateView(statement.Name, BuildViewSpec(statement.Definition) with { Parameters = parameters });
        return 0;
    }

    private int ExecuteAlterTable(AlterTableStatement statement) => statement.Action switch
    {
        // ADD CONSTRAINT … PRIMARY KEY (cols): a primary key is a unique, primary index named after the
        // constraint (verified vs ACE) — the same write path as CREATE INDEX … WITH PRIMARY.
        AddPrimaryKeyAction pk => AddPrimaryKey(statement.Table, pk),
        // ADD CONSTRAINT … FOREIGN KEY: add a relationship to the existing table (child index + parent
        // incoming block + MSysRelationships), the same write path as an inline CREATE TABLE foreign key.
        AddForeignKeyAction fk => AddForeignKey(statement.Table, fk.ForeignKey),
        // DROP CONSTRAINT name: drop a foreign key (relationship) by name. LibRed enforces FKs from
        // MSysRelationships, so removing those rows disables it.
        DropConstraintAction drop => DropConstraint(statement.Table, drop.Name),
        // The remaining actions land in their own follow-up steps.
        _ => throw new NotSupportedException(
            $"ALTER TABLE {statement.Action.GetType().Name} is parsed but not executed yet."),
    };

    private int DropConstraint(string table, string name)
    {
        if (!_database.DropConstraint(table, name))
            throw new NotSupportedException(
                $"ALTER TABLE '{table}' DROP CONSTRAINT '{name}': only foreign-key constraints can be dropped yet " +
                "(no matching relationship found — dropping a primary-key/unique index is not implemented).");
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

    private int AddForeignKey(string table, ForeignKeyConstraint fk)
    {
        _database.AddForeignKey(table, new RelationshipSpec(
            Name: fk.Name ?? DefaultRelationshipName(table, fk),
            ReferencedTable: fk.ReferencedTable,
            Columns: PairColumns(fk),
            IsEnforced: true,
            CascadeUpdate: fk.OnUpdate == ReferentialAction.Cascade,
            CascadeDelete: fk.OnDelete == ReferentialAction.Cascade,
            NoIndex: fk.NoIndex));
        return 0;
    }

    private int ExecuteCreateActionProcedure(CreateActionProcedureStatement statement)
    {
        ActionQuerySpec spec = statement.Kind == ProcedureActionKind.DataDefinition
            ? new ActionQuerySpec(ActionQueryKind.DataDefinition, DdlSql: statement.DdlSql)
            : new ActionQuerySpec(ActionQueryKind.Append, TargetTable: statement.TargetTable,
                Values: statement.AppendColumns!.Select(c => new AppendColumnSpec(c.Column, c.ValueExpression)).ToList());
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
        Parameters: null,
        OrderBy: d.OrderBy.Select(o => new ViewOrderBySpec(o.Expression, o.Descending)).ToList(),
        Top: d.Top);

    private int ExecuteInsert(InsertStatement statement)
    {
        Table table = _database.OpenTable(statement.Table);
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

        // Columns with a DEFAULT value (parsed once): applied to any row that omits the column, matching
        // Access — EF Core relies on the store default rather than supplying the value itself.
        var defaultColumns = columns
            .Where(c => c.DefaultValue is not null)
            .Select(c => (c.Index, Expression: _parser.ParseExpression(c.DefaultValue!)))
            .ToList();

        // Jet allows at most one AutoNumber column; its post-insert value is @@IDENTITY.
        ColumnDef? autoNumber = columns.FirstOrDefault(c => c.IsAutoNumber);

        int affected = 0;
        object? lastIdentity = null;
        foreach (IReadOnlyList<Expression> rowExprs in statement.Rows)
        {
            if (rowExprs.Count != targets.Count)
                throw new InvalidOperationException(
                    $"INSERT has {rowExprs.Count} values but {targets.Count} target columns.");

            var values = new object?[columns.Count];
            var provided = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                ColumnDef column = table.Definition.FindColumn(targets[i])
                    ?? throw new InvalidOperationException($"Column '{targets[i]}' does not exist in '{statement.Table}'.");
                values[column.Index] = evaluator.Evaluate(rowExprs[i]);
                provided.Add(column.Index);
            }

            // Fill defaults for columns the insert didn't mention (an explicit NULL is left as NULL).
            foreach (var (index, expression) in defaultColumns)
                if (!provided.Contains(index))
                    values[index] = evaluator.Evaluate(expression);

            EnforceRequired(statement.Table, columns, values);
            EnforceReferentialIntegrity(statement.Table, table, values);
            table.Insert(values); // fills values[autoNumber.Index] with the generated id (array mutated in place)
            if (autoNumber is not null)
                lastIdentity = values[autoNumber.Index];
            affected++;
        }

        // Publish @@ROWCOUNT (rows this insert affected) and @@IDENTITY (the last AutoNumber generated) so a
        // following SELECT in the same batch can read the store-generated key back — the shape EF Core emits.
        // @@IDENTITY is connection-scoped and only overwritten by an insert that actually generates an id,
        // so an insert into a keyless table leaves the previous value intact (matching Access).
        if (_session is not null)
        {
            _session.RowCount = affected;
            if (autoNumber is not null)
                _session.LastIdentity = lastIdentity;
        }
        return affected;
    }

    /// <summary>
    /// Executes UPDATE table SET col = expr, … [WHERE criteria]. The WHERE is an ordinary expression (the
    /// same as a SELECT's), evaluated per row; each SET expression may reference the row's current values.
    /// Matching rows are rewritten in place (row id preserved). Publishes @@ROWCOUNT = rows matched. Not yet
    /// supported (throws): changing an indexed column (needs index-entry maintenance) and a row that grows
    /// past its page (needs relocation).
    /// </summary>
    private int ExecuteUpdate(UpdateStatement statement)
    {
        Table table = _database.OpenTable(statement.Table);
        var columns = table.Definition.Columns;

        var targets = statement.Assignments
            .Select(a => (Column: table.Definition.FindColumn(a.Column)
                ?? throw new InvalidOperationException($"Column '{a.Column}' does not exist in '{statement.Table}'."),
                a.Value))
            .ToList();

        var outputColumns = columns.Select(c => new OutputColumn(statement.Table, c.Name)).ToList();

        ExpressionEvaluator RowEvaluator(object?[] row) =>
            new(new EvalScope(outputColumns, row, null), _scalarRunner, _parameters, _session);

        // Snapshot the matching rows first — don't mutate the table while its cursor is open.
        var matches = new List<(RowId Id, object?[] Values)>();
        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
            if (statement.Where is null || RowEvaluator(values).Evaluate(statement.Where) is true)
                matches.Add((id, values));

        int affected = 0;
        foreach ((RowId id, object?[] oldValues) in matches)
        {
            var newValues = (object?[])oldValues.Clone();
            var changed = new HashSet<int>();
            ExpressionEvaluator eval = RowEvaluator(oldValues);
            foreach ((ColumnDef column, Expression expr) in targets)
            {
                object? value = eval.Evaluate(expr);
                if (!Equals(value, oldValues[column.Index]))
                {
                    newValues[column.Index] = value;
                    changed.Add(column.Index);
                }
            }

            if (changed.Count > 0)
            {
                table.Update(id, newValues); // in place — row id preserved (may throw if the row must relocate)

                // Maintain every index whose key columns changed: move the entry (old key → new key), keeping
                // the same row id. Dedup by root page (a relationship index shares a real index's B-tree).
                foreach (IndexDef index in table.Definition.Indexes
                    .Where(i => i.RootPage > 0 && i.Columns.Any(c => changed.Contains(c.Column.Index)))
                    .GroupBy(i => i.RootPage).Select(g => g.First()))
                    table.MoveIndexEntry(index, oldValues, newValues, id);
            }
            affected++; // @@ROWCOUNT counts matched rows, changed or not
        }

        if (_session is not null) _session.RowCount = affected;
        return affected;
    }

    /// <summary>
    /// Executes DELETE [table.*] FROM table [WHERE criteria]. The WHERE is an ordinary expression (the same
    /// as a SELECT's), evaluated per row. Each matching row's index entries are removed and the row is
    /// soft-deleted (row id kept, TDEF row count decremented — matching Access). Publishes @@ROWCOUNT = rows
    /// deleted. (The rows' LVAL pages are not reclaimed yet.)
    /// </summary>
    private int ExecuteDelete(DeleteStatement statement)
    {
        Table table = _database.OpenTable(statement.Table);
        var outputColumns = table.Definition.Columns.Select(c => new OutputColumn(statement.Table, c.Name)).ToList();

        // Snapshot the matching rows first — don't mutate the table while its cursor is open.
        var matches = new List<(RowId Id, object?[] Values)>();
        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
        {
            var eval = new ExpressionEvaluator(new EvalScope(outputColumns, values, null), _scalarRunner, _parameters, _session);
            if (statement.Where is null || eval.Evaluate(statement.Where) is true)
                matches.Add((id, values));
        }

        var indexes = table.Definition.Indexes
            .Where(i => i.RootPage > 0).GroupBy(i => i.RootPage).Select(g => g.First()).ToList();
        foreach ((RowId id, object?[] values) in matches)
        {
            foreach (IndexDef index in indexes)
                table.RemoveIndexEntry(index, values, id);
            table.Delete(id);
        }

        int affected = matches.Count;
        if (_session is not null) _session.RowCount = affected;
        return affected;
    }
}
