using LibRed.Catalog;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using LibRed.Storage;

namespace LibRed.Engine.Execution;

/// <summary>
/// Executes non-query statements (DDL/DML) against the storage layer: CREATE TABLE and INSERT.
/// Returns the number of affected rows (0 for DDL).
/// </summary>
internal sealed class StatementExecutor(JetDatabase database, IReadOnlyDictionary<string, object?>? parameters, ISqlParser parser)
{
    private readonly JetDatabase _database = database;
    private readonly ParameterBag _parameters = new(parameters);
    private readonly ISqlParser _parser = parser;
    // For evaluating VALUES expressions (literals, parameters, and any scalar subqueries).
    private readonly QueryExecutor _scalarRunner = new(database, parameters);

    public int Execute(SqlStatement statement) => statement switch
    {
        CreateTableStatement create => ExecuteCreateTable(create),
        CreateIndexStatement createIndex => ExecuteCreateIndex(createIndex),
        CreateViewStatement createView => ExecuteCreateView(createView),
        InsertStatement insert => ExecuteInsert(insert),
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
        ViewDefinition d = statement.Definition;
        var spec = new ViewSpec(
            d.Distinct,
            d.Columns,
            d.Tables.Select(t => new ViewTableSpec(t.Table, t.Alias, t.SubquerySql)).ToList(),
            d.Joins.Select(j => new ViewJoinSpec(
                j.Kind switch { ViewJoinKind.Left => ViewJoinType.Left, ViewJoinKind.Right => ViewJoinType.Right, _ => ViewJoinType.Inner },
                j.Condition, j.LeftAlias, j.RightAlias)).ToList(),
            d.Where);

        _database.CreateView(statement.Name, spec);
        return 0;
    }

    private int ExecuteInsert(InsertStatement statement)
    {
        Table table = _database.OpenTable(statement.Table);
        var columns = table.Definition.Columns;

        // Target columns: the explicit list, or all columns in order.
        IReadOnlyList<string> targets = statement.Columns.Count > 0
            ? statement.Columns
            : columns.Select(c => c.Name).ToList();

        var evaluator = new ExpressionEvaluator(
            new EvalScope([], [], null), _scalarRunner, parameters: _parameters);

        // Columns with a DEFAULT value (parsed once): applied to any row that omits the column, matching
        // Access — EF Core relies on the store default rather than supplying the value itself.
        var defaultColumns = columns
            .Where(c => c.DefaultValue is not null)
            .Select(c => (c.Index, Expression: _parser.ParseExpression(c.DefaultValue!)))
            .ToList();

        int affected = 0;
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

            EnforceReferentialIntegrity(statement.Table, table, values);
            table.Insert(values);
            affected++;
        }

        // TODO(last-insert-id): surface the generated AutoNumber id to the caller (Jet's @@IDENTITY /
        // SCOPE_IDENTITY). RowInserter now assigns it (into `values[autoNumberColumn.Index]`) but we
        // only return the affected-row count, so it is discarded. EF Core needs the key back after an
        // insert, so this must be plumbed Engine -> Ado (LibRedCommand) -> EFCore before the provider
        // can support store-generated keys. See memory: libred-last-insert-id-todo.
        return affected;
    }
}
