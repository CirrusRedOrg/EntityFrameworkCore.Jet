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
        DropIndexStatement dropIndex => DropIndex(dropIndex.Table, dropIndex.Index),
        DropTableStatement dropTable => DropTable(dropTable.Table),
        DropViewStatement dropView => DropQueryObject(dropView.View, "view"),
        DropProcedureStatement dropProc => DropQueryObject(dropProc.Procedure, "procedure"),
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
            NoIndex: fk.NoIndex,
            DeleteSetNull: fk.OnDelete == ReferentialAction.SetNull,
            UpdateSetNull: fk.OnUpdate == ReferentialAction.SetNull)).ToList();

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

            // A **self-referencing** FK: the row being inserted is itself a candidate parent, so a row that
            // points at its own key (the root of a required self-ref, e.g. Inverse1Id = Id) satisfies the FK
            // even though it isn't on disk yet — the parent scan runs before the insert. Access allows this
            // (and EF's ComplexNavigations seed relies on it). Check the new row's own referenced-key first.
            if (string.Equals(fk.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase)
                && RowSatisfiesOwnKey(fk, table, values, target))
                continue;

            if (!ParentRowExists(fk, target))
                throw new InvalidOperationException(
                    $"INSERT into '{childTable}' violates foreign key '{fk.Name}': no matching row in '{fk.ReferencedTable}'.");
        }
    }

    /// <summary>For a self-referencing FK, whether the row's own referenced-column values equal the FK
    /// target — i.e. the row points at itself (or at its own composite key), which satisfies the FK.</summary>
    private static bool RowSatisfiesOwnKey(ForeignKey fk, Table table, object?[] values, object?[] target)
    {
        for (int i = 0; i < fk.Columns.Count; i++)
        {
            ColumnDef refCol = table.Definition.FindColumn(fk.Columns[i].ReferencedColumn)
                ?? throw new InvalidOperationException($"Column '{fk.Columns[i].ReferencedColumn}' does not exist in '{table.Name}'.");
            if (ExpressionEvaluator.CompareForSort(values[refCol.Index], target[i]) != 0) return false;
        }
        return true;
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

    /// <summary>The enforced relationships for which <paramref name="parentTable"/> is the referenced
    /// (parent) side — i.e. those whose child rows a delete/key-update of a parent row must handle.</summary>
    private IEnumerable<ForeignKey> ChildRelationshipsOf(string parentTable) =>
        _database.Catalog.Relationships.Where(r =>
            r.IsEnforced && string.Equals(r.ReferencedTable, parentTable, StringComparison.OrdinalIgnoreCase));

    /// <summary>The referenced-column values from a parent row (the key children point at).</summary>
    private object?[] ReferencedKey(ForeignKey fk, object?[] parentValues)
    {
        Table parent = _database.OpenTable(fk.ReferencedTable);
        return fk.Columns.Select(c => parentValues[parent.Definition.FindColumn(c.ReferencedColumn)!.Index]).ToArray();
    }

    /// <summary>Child rows whose FK columns equal <paramref name="key"/> (a null FK column never matches).</summary>
    private List<(RowId Id, object?[] Values)> FindChildRows(ForeignKey fk, object?[] key)
    {
        Table child = _database.OpenTable(fk.Table);
        int[] childCols = fk.Columns.Select(c => child.Definition.FindColumn(c.Column)!.Index).ToArray();
        var result = new List<(RowId, object?[])>();
        foreach ((RowId id, object?[] values) in child.Rows().WithIds())
        {
            bool match = true;
            for (int i = 0; i < childCols.Length; i++)
                if (values[childCols[i]] is null || ExpressionEvaluator.CompareForSort(values[childCols[i]], key[i]) != 0)
                { match = false; break; }
            if (match) result.Add((id, values));
        }
        return result;
    }

    /// <summary>Applies each enforced relationship's ON DELETE action to a parent row being deleted: CASCADE
    /// deletes the children (recursively), SET NULL nulls their FK columns, and NO ACTION rejects the delete
    /// if any child exists (matching Access's "record cannot be deleted… includes related records").</summary>
    private void CascadeParentDelete(string parentTable, object?[] parentValues)
    {
        foreach (ForeignKey fk in ChildRelationshipsOf(parentTable))
        {
            object?[] key = ReferencedKey(fk, parentValues);
            if (key.Any(k => k is null)) continue; // a null parent key is referenced by nobody
            var children = FindChildRows(fk, key);
            if (children.Count == 0) continue;

            if (fk.CascadeDelete)
            {
                Table child = _database.OpenTable(fk.Table);
                foreach (var (cid, cvals) in children) DeleteRowCascading(child, cid, cvals);
            }
            else if (fk.DeleteSetNull)
                foreach (var (cid, cvals) in children) SetChildKey(fk, cid, cvals, newKey: null);
            else
                throw new InvalidOperationException(
                    $"The record cannot be deleted or changed because table '{fk.Table}' includes related records.");
        }
    }

    /// <summary>Deletes a row after handling its children (cascade/set-null/reject), removing its index
    /// entries and soft-deleting it. Recurses for a cascade chain.</summary>
    private void DeleteRowCascading(Table table, RowId id, object?[] values)
    {
        CascadeParentDelete(table.Name, values);
        foreach (IndexDef index in table.Definition.Indexes.Where(i => i.RootPage > 0).GroupBy(i => i.RootPage).Select(g => g.First()))
            table.RemoveIndexEntry(index, values, id);
        table.Delete(id);
    }

    /// <summary>Applies each enforced relationship's ON UPDATE action when a parent row's referenced key
    /// changes: CASCADE rewrites the children's FK to the new key; NO ACTION rejects if any child exists.
    /// (Jet has no ON UPDATE SET NULL.)</summary>
    private void CascadeParentKeyUpdate(string parentTable, object?[] oldValues, object?[] newValues)
    {
        foreach (ForeignKey fk in ChildRelationshipsOf(parentTable))
        {
            object?[] oldKey = ReferencedKey(fk, oldValues);
            object?[] newKey = ReferencedKey(fk, newValues);
            if (oldKey.Any(k => k is null) || KeyEquals(oldKey, newKey)) continue;
            var children = FindChildRows(fk, oldKey);
            if (children.Count == 0) continue;

            if (fk.CascadeUpdate)
                foreach (var (cid, cvals) in children) SetChildKey(fk, cid, cvals, newKey);
            else
                throw new InvalidOperationException(
                    $"The record cannot be deleted or changed because table '{fk.Table}' includes related records.");
        }
    }

    private static bool KeyEquals(object?[] a, object?[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (ExpressionEvaluator.CompareForSort(a[i], b[i]) != 0) return false;
        return true;
    }

    /// <summary>Rewrites a child row's FK columns to <paramref name="newKey"/> (or NULL for SET NULL),
    /// maintaining any index over them.</summary>
    private void SetChildKey(ForeignKey fk, RowId childId, object?[] childValues, object?[]? newKey)
    {
        Table child = _database.OpenTable(fk.Table);
        var newValues = (object?[])childValues.Clone();
        var changed = new HashSet<int>();
        for (int i = 0; i < fk.Columns.Count; i++)
        {
            int idx = child.Definition.FindColumn(fk.Columns[i].Column)!.Index;
            object? nv = newKey?[i];
            if (!Equals(nv, childValues[idx])) { newValues[idx] = nv; changed.Add(idx); }
        }
        if (changed.Count == 0) return;

        child.Update(childId, newValues, changed);
        foreach (IndexDef index in child.Definition.Indexes
            .Where(i => i.RootPage > 0 && i.Columns.Any(c => changed.Contains(c.Column.Index)))
            .GroupBy(i => i.RootPage).Select(g => g.First()))
            child.MoveIndexEntry(index, childValues, newValues, childId);
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
        // ADD COLUMN: append the column's descriptor/name to the TDEF (existing rows read it as NULL).
        AddColumnAction add => AddColumn(statement.Table, add.Column),
        // DROP COLUMN: a metadata-only TDEF edit (remove the descriptor + name, decrement ColumnCount).
        DropColumnAction dropCol => DropColumn(statement.Table, dropCol.Field),
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

    private int AddColumn(string table, ColumnDefinition column)
    {
        // NOT NULL and DEFAULT are written to the column's LvProp properties (Required / DefaultValue).
        if (!_database.AddColumn(table, AccessTypeMapper.ToColumnSpec(column), column.Default))
            throw new InvalidOperationException($"ALTER TABLE '{table}' ADD COLUMN '{column.Name}': the column already exists.");
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
            throw new InvalidOperationException($"DROP TABLE '{table}': no such table.");
        return 0;
    }

    private int DropQueryObject(string name, string kind)
    {
        if (!_database.DropQueryObject(name))
            throw new InvalidOperationException($"DROP {kind.ToUpperInvariant()} '{name}': no such {kind}.");
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
            NoIndex: fk.NoIndex,
            DeleteSetNull: fk.OnDelete == ReferentialAction.SetNull,
            UpdateSetNull: fk.OnUpdate == ReferentialAction.SetNull));
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

    /// <summary>A table participating in an UPDATE/DELETE source: its alias, the opened table, and its
    /// columns as alias-qualified output columns (for the combined evaluation scope).</summary>
    private sealed record SourceTable(string Alias, Table Table, IReadOnlyList<OutputColumn> Columns);

    /// <summary>Flattens the UPDATE/DELETE table source into its tables (in order) plus the join ON
    /// conditions. Only INNER/CROSS joins over named tables are supported (Access's multi-table form).</summary>
    private (List<SourceTable> Tables, List<Expression> Ons) ResolveSource(TableReference from)
    {
        var tables = new List<SourceTable>();
        var ons = new List<Expression>();

        void Walk(TableReference r)
        {
            switch (r)
            {
                case NamedTable n:
                    Table t = _database.OpenTable(n.Name);
                    string alias = n.Alias ?? n.Name;
                    tables.Add(new SourceTable(alias, t, t.Definition.Columns.Select(c => new OutputColumn(alias, c.Name)).ToList()));
                    break;
                case JoinTable { Kind: JoinKind.Inner or JoinKind.Cross } j:
                    Walk(j.Left);
                    Walk(j.Right);
                    if (j.On is not null) ons.Add(j.On);
                    break;
                default:
                    throw new NotSupportedException($"UPDATE/DELETE over a {r.GetType().Name} source is not supported yet.");
            }
        }

        Walk(from);
        return (tables, ons);
    }

    /// <summary>
    /// Materialises the join rows of the source: each is the per-table (row id + <b>shared</b> value array),
    /// in table order, that satisfies all ON conditions and the WHERE. A physical row's value array is shared
    /// across every join row it appears in (cached by alias+row id), so a SET that references the row's own
    /// value accumulates across matches — matching Access (e.g. a "one"-side counter incremented per match).
    /// </summary>
    private List<(RowId Id, object?[] Values)[]> JoinRows(
        List<SourceTable> tables, List<Expression> ons, Expression? where, IReadOnlyList<OutputColumn> columns)
    {
        var cache = new Dictionary<(string, RowId), object?[]>();

        IEnumerable<(RowId, object?[])[]> Combine(int i, (RowId, object?[])[] acc)
        {
            if (i == tables.Count) { yield return (((RowId, object?[])[])acc.Clone()); yield break; }
            foreach ((RowId id, object?[] values) in tables[i].Table.Rows().WithIds())
            {
                var key = (tables[i].Alias, id);
                if (!cache.TryGetValue(key, out object?[]? shared)) cache[key] = shared = values;
                acc[i] = (id, shared);
                foreach (var r in Combine(i + 1, acc)) yield return r;
            }
        }

        var result = new List<(RowId, object?[])[]>();
        foreach (var combo in Combine(0, new (RowId, object?[])[tables.Count]))
        {
            object?[] flat = combo.SelectMany(c => c.Item2).ToArray();
            var eval = new ExpressionEvaluator(new EvalScope(columns, flat, null), _scalarRunner, _parameters, _session);
            if (ons.All(o => eval.Evaluate(o) is true) && (where is null || eval.Evaluate(where) is true))
                result.Add(combo);
        }
        return result;
    }

    /// <summary>The source-table index a SET assignment (or a delete target) applies to: the alias/table-name
    /// qualifier if given, else the single table (ambiguous when there are several).</summary>
    private static int TargetIndex(List<SourceTable> tables, string? qualifier, string what)
    {
        if (qualifier is null)
            return tables.Count == 1 ? 0
                : throw new InvalidOperationException($"{what} must be table-qualified when the statement joins several tables.");
        int i = tables.FindIndex(t =>
            string.Equals(t.Alias, qualifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Table.Name, qualifier, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : throw new InvalidOperationException($"{what} '{qualifier}' is not one of the statement's tables.");
    }

    /// <summary>
    /// Executes UPDATE tableexpression SET col = expr, … [WHERE criteria]. The table expression may be a join,
    /// and each SET target may name a specific joined table (Access's multi-table update). Each SET expression
    /// may reference the current values; the WHERE is an ordinary expression (correlated EXISTS included).
    /// Rows are rewritten in place (row id preserved). @@ROWCOUNT = matched join rows.
    /// </summary>
    private int ExecuteUpdate(UpdateStatement statement)
    {
        var (tables, ons) = ResolveSource(statement.From);
        var columns = tables.SelectMany(t => t.Columns).ToList();
        List<(RowId Id, object?[] Values)[]> joinRows = JoinRows(tables, ons, statement.Where, columns);

        // Resolve each assignment to its (table index, column) once.
        var targets = statement.Assignments.Select(a =>
        {
            int ti = TargetIndex(tables, a.Table, "UPDATE SET column");
            ColumnDef col = tables[ti].Table.Definition.FindColumn(a.Column)
                ?? throw new InvalidOperationException($"Column '{a.Column}' does not exist in '{tables[ti].Table.Name}'.");
            return (TableIndex: ti, Column: col, a.Value);
        }).ToList();

        // Apply SETs to the shared value arrays; snapshot each touched row's original bytes on first touch.
        var dirty = new Dictionary<(string, RowId), (SourceTable Table, RowId Id, object?[] Original, object?[] Values)>();
        foreach (var combo in joinRows)
        {
            foreach ((int ti, ColumnDef col, Expression valueExpr) in targets)
            {
                object?[] shared = combo[ti].Values;
                var key = (tables[ti].Alias, combo[ti].Id);
                if (!dirty.ContainsKey(key)) dirty[key] = (tables[ti], combo[ti].Id, (object?[])shared.Clone(), shared);

                object?[] flat = combo.SelectMany(c => c.Item2).ToArray();
                var eval = new ExpressionEvaluator(new EvalScope(columns, flat, null), _scalarRunner, _parameters, _session);
                shared[col.Index] = eval.Evaluate(valueExpr);
            }
        }

        foreach (var (table, id, original, values) in dirty.Values)
        {
            var changed = new HashSet<int>();
            for (int i = 0; i < values.Length; i++)
                if (!Equals(original[i], values[i])) changed.Add(i);
            if (changed.Count == 0) continue; // unchanged after all

            // Child side: a changed FK column must still reference an existing parent (like an insert).
            if (_database.Catalog.ForeignKeysOf(table.Table.Name).Any(f => f.IsEnforced &&
                    f.Columns.Any(c => changed.Contains(table.Table.Definition.FindColumn(c.Column)!.Index))))
                EnforceReferentialIntegrity(table.Table.Name, table.Table, values);

            // A changed UNIQUE/PRIMARY key must not collide with another row (null keys are distinct — a
            // unique index permits multiple nulls, so they're skipped, matching the insert rule).
            foreach (IndexDef index in table.Table.Definition.Indexes
                .Where(i => i.IsUnique && i.RootPage > 0 && i.Columns.Any(c => changed.Contains(c.Column.Index)))
                .GroupBy(i => i.RootPage).Select(g => g.First()))
                if (!index.Columns.Any(c => values[c.Column.Index] is null) && table.Table.HasDuplicateKey(index, values, id))
                    throw new InvalidOperationException(
                        $"Cannot update '{table.Table.Name}': a row with the same " +
                        $"{(index.IsPrimaryKey ? "primary key" : "unique key")} already exists (index '{index.Name}').");

            // Parent side: a changed referenced-key column triggers each relationship's ON UPDATE action
            // (CASCADE rewrites children, NO ACTION rejects if children exist).
            CascadeParentKeyUpdate(table.Table.Name, original, values);

            table.Table.Update(id, values, changed);
            foreach (IndexDef index in table.Table.Definition.Indexes
                .Where(i => i.RootPage > 0 && i.Columns.Any(c => changed.Contains(c.Column.Index)))
                .GroupBy(i => i.RootPage).Select(g => g.First()))
                table.Table.MoveIndexEntry(index, original, values, id);
        }

        int affected = joinRows.Count;
        if (_session is not null) _session.RowCount = affected;
        return affected;
    }

    /// <summary>
    /// Executes DELETE [target.*] FROM tableexpression [WHERE criteria]. The table expression may be a join;
    /// <c>target.*</c> selects which joined table's rows to delete (defaults to the single table). Each
    /// matched target row's index entries are removed and the row is soft-deleted (row id kept, TDEF row
    /// count decremented). @@ROWCOUNT = distinct rows deleted.
    /// </summary>
    private int ExecuteDelete(DeleteStatement statement)
    {
        var (tables, ons) = ResolveSource(statement.From);
        var columns = tables.SelectMany(t => t.Columns).ToList();
        List<(RowId Id, object?[] Values)[]> joinRows = JoinRows(tables, ons, statement.Where, columns);

        int ti = TargetIndex(tables, statement.TargetTable, "DELETE target");
        SourceTable target = tables[ti];

        var deleted = new Dictionary<RowId, object?[]>();
        foreach (var combo in joinRows)
            deleted.TryAdd(combo[ti].Id, combo[ti].Values); // one delete per distinct target row

        // DeleteRowCascading applies each row's ON DELETE actions (cascade/set-null/reject) to its children,
        // then removes its index entries and soft-deletes it.
        foreach ((RowId id, object?[] values) in deleted)
            DeleteRowCascading(target.Table, id, values);

        int affected = deleted.Count;
        if (_session is not null) _session.RowCount = affected;
        return affected;
    }
}
