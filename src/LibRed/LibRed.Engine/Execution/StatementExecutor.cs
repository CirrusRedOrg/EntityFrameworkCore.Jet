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
        SelectStatement { Into: not null } makeTable => ExecuteSelectInto(makeTable),
        InsertStatement insert => ExecuteInsert(insert),
        UpdateStatement update => ExecuteUpdate(update),
        DeleteStatement delete => ExecuteDelete(delete),
        _ => throw new NotSupportedException($"{statement.GetType().Name} cannot be executed as a non-query."),
    };

    /// <summary>
    /// Maps a declared column type to its storage spec, first raising the database's format version if the
    /// type needs a newer one than the file currently is.
    /// </summary>
    /// <remarks>
    /// Access does the upgrade itself rather than refusing the DDL — adding a Date/Time Extended column to an
    /// ACE 12 database moves its version byte to 0x06, and for DATETIME2 that byte is the entire upgrade
    /// (docs/format/page-00-database.md). Refusing instead would leave LibRed unable to do something the
    /// engine it mirrors does routinely.
    /// <para>The raise joins this statement's transaction, so a failed CREATE/ALTER takes its format bump
    /// back down with it. It is still one-way in the sense that matters: once committed, an Access older than
    /// the new format cannot open the file — unavoidable, since the column it would find is one it cannot
    /// read either.</para>
    /// </remarks>
    private ColumnSpec MapColumn(ColumnDefinition column)
    {
        if (AccessTypeMapper.RequiredVersion(column.TypeName) is { } required)
            _database.EnsureFormatAtLeast(required.Min);

        return AccessTypeMapper.ToColumnSpec(column, _database.Format.Version);
    }

    private int ExecuteCreateTable(CreateTableStatement statement)
    {
        var columns = statement.Columns.Select(MapColumn).ToList();
        foreach (var (spec, def) in columns.Zip(statement.Columns))
            ValidateColumnDefault(spec, def.Default);
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

        _database.CreateTable(statement.Table, columns, primaryKey, relationships, uniques, defaults, checks,
            statement.PrimaryKeyName);
        return 0;
    }

    private static readonly System.Text.RegularExpressions.Regex BareWord =
        new(@"^[A-Za-z_][A-Za-z_0-9]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Parses a column DEFAULT into an expression. In Access an **unquoted single word** is a LITERAL
    /// STRING (verified vs ACE: <c>DEFAULT Unknown</c> → "Unknown", <c>DEFAULT K</c> → "K" even when K is a
    /// column) — it is never a column reference. The one exception is the niladic <c>Now</c> function. A
    /// bracketed <c>[X]</c> stays a column reference (rejected in a default); a quoted literal, function call, or
    /// compound expression parses normally. (A multi-word bare default is a syntax error at CREATE, matching ACE.)</summary>
    private Expression ParseDefaultExpression(string text)
    {
        string t = text.Trim();
        return BareWord.IsMatch(t) && !t.Equals("Now", StringComparison.OrdinalIgnoreCase)
            ? new LiteralExpression(t)
            : _parser.ParseExpression(text);
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

    /// <summary>Rejects a row that violates any of the table's CHECK constraints — evaluated against the full
    /// row. A CHECK is violated only when its expression is explicitly FALSE; NULL/unknown passes (SQL
    /// three-valued CHECK semantics). The expression may reference the row's own columns and use (uncorrelated)
    /// subqueries. Matches Access's validation-rule enforcement.</summary>
    private void EnforceCheckConstraints(TableDef definition, object?[] values)
    {
        if (definition.CheckConstraints.Count == 0) return;
        var schema = definition.Columns
            .Select(c => new OutputColumn(definition.Name, c.Name, Schema.JetClrTypeMap.ToClrType(c.Type))).ToList();
        var evaluator = new ExpressionEvaluator(new EvalScope(schema, values, null), _scalarRunner, _parameters, _session);
        foreach (var (name, expression) in definition.CheckConstraints)
            if (evaluator.Evaluate(_parser.ParseExpression(expression)) is false)
                throw new InvalidOperationException(
                    $"One or more values are prohibited by the validation rule '{name}' set for '{definition.Name}'. " +
                    "Enter a value that the expression for this field can accept.");
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
    /// Rejects an insert whose foreign-key columns reference a non-existent parent row. NULL handling follows
    /// ACE's **MATCH FULL** rule (verified vs ACE, and unlike SQL Server's MATCH SIMPLE): a composite FK is
    /// skipped only when **every** column is null; a **partial** null (some null, some not) can never match a
    /// parent key and is rejected. Only enforced relationships (grbit without "don't enforce") are checked.
    /// </summary>
    private void EnforceReferentialIntegrity(string childTable, Table table, object?[] values)
    {
        foreach (ForeignKey fk in _database.Catalog.ForeignKeysOf(childTable))
        {
            if (!fk.IsEnforced) continue;

            var target = new object?[fk.Columns.Count];
            int nullCount = 0;
            for (int i = 0; i < fk.Columns.Count; i++)
            {
                ColumnDef col = table.Definition.FindColumn(fk.Columns[i].Column)
                    ?? throw new InvalidOperationException($"Column '{fk.Columns[i].Column}' does not exist in '{childTable}'.");
                object? v = values[col.Index];
                if (v is null) nullCount++;
                else target[i] = v;
            }
            if (nullCount == fk.Columns.Count) continue; // all FK columns null → the FK is not applied to this row

            // MATCH FULL: a partial null can never reference a full parent key, so it's a violation — treated
            // like a missing parent (ACE gives the same "a related record is required" error).
            bool partialNull = nullCount > 0;

            // A **self-referencing** FK: a fully-specified row that points at its own key (the root of a
            // required self-ref, e.g. Inverse1Id = Id) satisfies the FK even though it isn't on disk yet — the
            // parent scan runs before the insert. Access allows this (EF's ComplexNavigations seed relies on it).
            if (!partialNull
                && string.Equals(fk.ReferencedTable, childTable, StringComparison.OrdinalIgnoreCase)
                && RowSatisfiesOwnKey(fk, table, values, target))
                continue;

            if (partialNull || !ParentRowExists(fk, target))
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

        foreach (var (id, values) in roots)
            if (scheduled.Add((rootTable.Name, id)))
                stack.Push((rootTable, id, values, false));

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

        foreach (var (table, id, values) in order)
        {
            foreach (IndexDef index in table.Definition.Indexes.Where(i => i.RootPage > 0)
                .GroupBy(i => i.RootPage).Select(g => g.First()))
                table.RemoveIndexEntry(index, values, id);
            table.Delete(id);
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
            object?[] key = ReferencedKey(fk, parentValues);
            if (key.Any(k => k is null)) continue; // a null parent key is referenced by nobody
            var children = FindChildRows(fk, key);
            if (children.Count == 0) continue;

            if (fk.CascadeDelete)
            {
                Table child = _database.OpenTable(fk.Table);
                foreach (var (cid, cvals) in children)
                    if (scheduled.Add((child.Name, cid)))
                        stack.Push((child, cid, cvals, false));
            }
            else if (fk.DeleteSetNull)
                foreach (var (cid, cvals) in children) SetChildKey(fk, cid, cvals, newKey: null);
            else
                throw new InvalidOperationException(
                    $"The record cannot be deleted or changed because table '{fk.Table}' includes related records.");
        }
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
        //
        // Deliberately NOT MapColumn: this declares no storage, so there is no column forcing the file's
        // hand, and a BIGINT/DATETIME2 parameter on an older format is left to fail. Upgrading a whole
        // database for a saved query's parameter type is a bigger claim than anything measured — what ACE
        // does with a new-type parameter in MSysQueries has not been probed, unlike the column case.
        var parameters = statement.Parameters
            .Select(p => new ViewParameterSpec(
                p.Name,
                (byte)AccessTypeMapper.ToColumnSpec(
                    new ColumnDefinition(p.Name, p.TypeName, null, null, false, false), _database.Format.Version).Type))
            .ToList();
        _database.CreateView(statement.Name, BuildViewSpec(statement.Definition) with { Parameters = parameters });
        return 0;
    }

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
        AddColumnAction add => AddColumn(statement.Table, add.Column),
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

    private int AddColumn(string table, ColumnDefinition column)
    {
        // NOT NULL and DEFAULT are written to the column's LvProp properties (Required / DefaultValue).
        ColumnSpec spec = MapColumn(column);
        ValidateColumnDefault(spec, column.Default);
        if (!_database.AddColumn(table, spec, column.Default))
            throw new InvalidOperationException($"ALTER TABLE '{table}' ADD COLUMN '{column.Name}': the column already exists.");
        return 0;
    }

    /// <summary>Rejects a DEFAULT expression the storage engine can't accept — matching ACE's CREATE-time
    /// validation. Currently: <c>GenUniqueID()</c> is only valid on a <c>LONG</c> (Int32) column; every other
    /// type raises "Cannot place this validation expression on this field" (verified across BYTE/SHORT/SINGLE/
    /// DOUBLE/CURRENCY/DECIMAL/GUID/DATETIME/BIT/TEXT).</summary>
    private static void ValidateColumnDefault(ColumnSpec spec, string? defaultSql)
    {
        if (defaultSql is not null
            && defaultSql.Trim().Equals("GenUniqueID()", StringComparison.OrdinalIgnoreCase)
            && spec.Type != JetDataType.Int32)
            throw new InvalidOperationException(
                $"Cannot place this validation expression on this field. GenUniqueID() is only valid as the " +
                $"DEFAULT of a LONG (Int32) column (column '{spec.Name}').");
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
        _database.AddCheckConstraint(table, chk.Check.Name ?? $"CK_{table}", chk.Check.Expression);
        return 0;
    }

    private int AlterColumn(string table, AlterColumnAction alter)
    {
        var colDef = new ColumnDefinition(alter.Field, alter.TypeName, alter.Size, alter.Scale, NotNull: false, PrimaryKey: false);
        _database.AlterColumn(table, alter.Field, MapColumn(colDef));
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
        string target = statement.Into!;
        if (_database.Catalog.Tables.Any(t => string.Equals(t.Name, target, StringComparison.OrdinalIgnoreCase)))
            throw new SchemaObjectExistsException($"Table '{target}' already exists.", target);

        // Run the query first — with INTO stripped, or planning would recurse back into this method — and
        // materialise it. The rows have to exist before the table does: the source may read a table this
        // statement is about to change, and the row count is not known until the read completes.
        ResultSet source = _scalarRunner.ExecuteQuery(Planning.IndexSelection.Apply(
            Planning.QueryPlanner.PlanSelect(statement with { Into = null }), _database.Catalog));
        var rows = source.Rows.ToList();

        // A result column that IS a source column keeps that column's DEFINITION — its type and its declared
        // width. Only a computed column is typed from its value. Measured: a source Text(30) arrives as
        // Text(60) bytes, not the Text(510) maximum, while a concatenation does get the maximum because
        // there is no declared width to copy.
        var sourceColumns = SourceColumnsFor(statement);
        var specs = source.ColumnNames
            .Select((name, i) => sourceColumns.TryGetValue(name, out ColumnDef? column)
                ? new ColumnSpec(name, column.Type, column.Length, column.IsFixedLength,
                                 Precision: column.Precision, Scale: column.Scale)
                : ColumnSpecFor(name, source.ColumnTypes[i]))
            .ToList();
        _database.CreateTable(target, specs);

        Table table = _database.OpenTable(target);
        foreach (object?[] row in rows)
        {
            var values = new object?[specs.Count];
            Array.Copy(row, values, Math.Min(row.Length, values.Length));
            table.Insert(values);
        }

        if (_session is not null) _session.RowCount = rows.Count;
        return rows.Count;
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
        foreach (string table in TablesIn(statement.From))
            if (_database.Catalog.Tables.FirstOrDefault(
                    t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase)) is { } def)
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

    /// <summary>The table names a FROM clause reaches, so their column definitions can be found.</summary>
    private static IEnumerable<string> TablesIn(TableReference? from) => from switch
    {
        NamedTable named => [named.Name],
        JoinTable join => [.. TablesIn(join.Left), .. TablesIn(join.Right)],
        // A derived table has no stored column definitions to copy, so its columns fall back to being typed
        // from their values — as a computed column does.
        _ => [],
    };

    /// <summary>The column a COMPUTED result column becomes. Text takes the 255-character maximum, because an
    /// expression carries no declared width — ACE gives a concatenation Text(255) rather than measuring the
    /// values it produced.</summary>
    private static ColumnSpec ColumnSpecFor(string name, Type clrType) => Type.GetTypeCode(clrType) switch
    {
        TypeCode.Boolean => new ColumnSpec(name, JetDataType.Boolean, 1, IsFixedLength: true),
        TypeCode.Byte => new ColumnSpec(name, JetDataType.Byte, 1, IsFixedLength: true),
        TypeCode.Int16 => new ColumnSpec(name, JetDataType.Int16, 2, IsFixedLength: true),
        TypeCode.Int32 => new ColumnSpec(name, JetDataType.Int32, 4, IsFixedLength: true),
        TypeCode.Single => new ColumnSpec(name, JetDataType.Single, 4, IsFixedLength: true),
        TypeCode.Double => new ColumnSpec(name, JetDataType.Double, 8, IsFixedLength: true),
        TypeCode.Decimal => new ColumnSpec(name, JetDataType.Currency, 8, IsFixedLength: true),
        TypeCode.DateTime => new ColumnSpec(name, JetDataType.DateTime, 8, IsFixedLength: true),
        _ when clrType == typeof(Guid) => new ColumnSpec(name, JetDataType.Guid, 16, IsFixedLength: true),
        _ when clrType == typeof(byte[]) => new ColumnSpec(name, JetDataType.Binary, 255, IsFixedLength: false),
        _ => new ColumnSpec(name, JetDataType.Text, 255 * 2, IsFixedLength: false),
    };

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
        // AutoNumber columns are excluded: their value is assigned by the row inserter (sequential counter, or
        // a random Int32 for a GenUniqueID() "Random" AutoNumber), not by evaluating the DefaultValue — and
        // GenUniqueID() is not a callable expression, so parsing it as a default would fail.
        var defaultColumns = columns
            .Where(c => c.DefaultValue is not null && !c.IsAutoNumber)
            .Select(c => (c.Index, Expression: ParseDefaultExpression(c.DefaultValue!)))
            .ToList();

        // Jet allows at most one AutoNumber column; its post-insert value is @@IDENTITY.
        ColumnDef? autoNumber = columns.FirstOrDefault(c => c.IsAutoNumber);

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
            var provided = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                ColumnDef column = table.Definition.FindColumn(targets[i])
                    ?? throw new InvalidOperationException($"Column '{targets[i]}' does not exist in '{statement.Table}'.");
                values[column.Index] = supplied[i];
                provided.Add(column.Index);
            }

            // Fill defaults for columns the insert didn't mention (an explicit NULL is left as NULL).
            foreach (var (index, expression) in defaultColumns)
                if (!provided.Contains(index))
                    values[index] = evaluator.Evaluate(expression);

            EnforceRequired(statement.Table, columns, values);
            EnforceReferentialIntegrity(statement.Table, table, values);
            EnforceCheckConstraints(table.Definition, values);
            table.Insert(values); // fills values[autoNumber.Index] with the generated id (array mutated in place)
            if (autoNumber is not null)
                lastIdentity = values[autoNumber.Index];
            affected++;
        }

        if (statement.Source is not null)
        {
            // The multiple-record form. The source is MATERIALISED before a single row is written: appending
            // a table to itself otherwise feeds its own output back into the scan and never terminates.
            // Access's INSERT INTO t SELECT * FROM t doubles the table and stops, so the read completes
            // before the write begins.
            ResultSet source = _scalarRunner.ExecuteQuery(
                Planning.IndexSelection.Apply(Planning.QueryPlanner.PlanStatement(statement.Source), _database.Catalog));
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
                for (int i = 0; i < rowExprs.Count; i++) supplied[i] = evaluator.Evaluate(rowExprs[i]);
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
            if (autoNumber is not null)
                _session.LastIdentity = lastIdentity;
        }
        return affected;
    }

    /// <summary>A table participating in an UPDATE/DELETE source: its alias, columns (alias-qualified, for the
    /// combined evaluation scope), and its rows. A physical table exposes <see cref="Table"/> (rows have real
    /// <see cref="RowId"/>s and can be a SET/DELETE target); a derived table (a subquery in the source) has
    /// <see cref="Table"/> null and its already-materialised <see cref="DerivedRows"/> (never a target).</summary>
    private sealed record SourceTable(string Alias, Table? Table, IReadOnlyList<OutputColumn> Columns,
        IReadOnlyList<object?[]>? DerivedRows);

    /// <summary>Flattens the UPDATE/DELETE table source into its tables in order, each paired with the join that
    /// introduced it: its <see cref="JoinKind"/> and ON condition (the first/base table is Inner with a null ON).
    /// INNER/CROSS/LEFT joins over named tables are supported — the left-deep form EF and Access emit.</summary>
    private (List<SourceTable> Tables, List<JoinKind> Kinds, List<Expression?> Ons) ResolveSource(TableReference from)
    {
        var tables = new List<SourceTable>();
        var kinds = new List<JoinKind>();
        var ons = new List<Expression?>();

        void EmitTable(NamedTable n, JoinKind kind, Expression? on)
        {
            Table t = _database.OpenTable(n.Name);
            string alias = n.Alias ?? n.Name;
            tables.Add(new SourceTable(alias, t, t.Definition.Columns
                .Select(c => new OutputColumn(alias, c.Name, Schema.JetClrTypeMap.ToClrType(c.Type))).ToList(), null));
            kinds.Add(kind);
            ons.Add(on);
        }

        void EmitDerived(SubqueryTable sq, JoinKind kind, Expression? on)
        {
            string alias = sq.Alias ?? throw new NotSupportedException("A derived table in an UPDATE/DELETE source requires an alias.");
            var (columns, rows) = ExecuteDerivedSource(sq.Query, alias);
            tables.Add(new SourceTable(alias, null, columns, rows));
            kinds.Add(kind);
            ons.Add(on);
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
                // Left-deep: the left subtree carries its own joins (its base is the source's first table, Inner
                // with no ON); the right side is the newly joined named/derived table, tagged with this join.
                case JoinTable { Kind: JoinKind.Inner or JoinKind.Cross or JoinKind.Left, Right: NamedTable or SubqueryTable } j:
                    Walk(j.Left, JoinKind.Inner, null);
                    Walk(j.Right, j.Kind, j.On);
                    break;
                // RIGHT JOIN keeps the right side: model it as the right side preserved (the base) with the left
                // side LEFT-joined onto it. The SET/DELETE target is usually that left side — which then becomes
                // the nullable side, so a right row with no match yields a null target that the WHERE drops.
                case JoinTable { Kind: JoinKind.Right, Left: NamedTable or SubqueryTable } j:
                    Walk(j.Right, JoinKind.Inner, null);
                    Walk(j.Left, JoinKind.Left, j.On);
                    break;
                default:
                    throw new NotSupportedException($"UPDATE/DELETE over a {r.GetType().Name} source is not supported yet.");
            }
        }

        Walk(from, JoinKind.Inner, null);
        return (tables, kinds, ons);
    }

    /// <summary>Runs a derived-table subquery (uncorrelated — a FROM/JOIN source) through the full query
    /// pipeline (index selection included) and materialises its rows, with columns qualified by the derived
    /// table's alias for the combined evaluation scope.</summary>
    private (List<OutputColumn> Columns, List<object?[]> Rows) ExecuteDerivedSource(SqlStatement query, string alias)
    {
        if (query is not SelectStatement select)
            throw new NotSupportedException("Only a SELECT is supported as a derived table in an UPDATE/DELETE source.");
        var plan = Planning.IndexSelection.Apply(Planning.QueryPlanner.PlanSelect(select), _database.Catalog);
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
        List<SourceTable> tables, List<JoinKind> kinds, List<Expression?> ons, Expression? where, IReadOnlyList<OutputColumn> columns)
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

        // Precompute the accumulated columns visible when seeking/evaluating an ON at each depth.
        var colsUpTo = new List<OutputColumn>[tables.Count + 1];
        colsUpTo[0] = [];
        for (int i = 0; i < tables.Count; i++)
            colsUpTo[i + 1] = [.. colsUpTo[i], .. tables[i].Columns];

        var result = new List<(RowId, object?[])[]>();
        var acc = new (RowId, object?[])[tables.Count];

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

            // A derived table's rows are already materialised and have no RowId (never a target). A physical
            // table is seeked when its ON allows (index-nested-loop), else scanned.
            IEnumerable<(RowId Id, object?[] Values)> rows;
            if (tables[i].Table is null)
            {
                rows = tables[i].DerivedRows!.Select(v => (default(RowId), v));
            }
            else if (seekPlan[i] is { } p)
            {
                var keyEval = new ExpressionEvaluator(new EvalScope(colsUpTo[i], Flatten(acc, i), null), _scalarRunner, _parameters, _session);
                var keyValues = new object?[tables[i].Table!.Definition.Columns.Count];
                keyValues[p.Index.Columns[0].Column.Index] = keyEval.Evaluate(p.Key);
                rows = tables[i].Table!.SeekRowsWithIds(p.Index, keyValues);
            }
            else
            {
                rows = tables[i].Table!.Rows().WithIds();
            }

            // The ON (also the seek's residual re-check — index keys can over-return) gates each candidate.
            bool matched = false;
            foreach ((RowId id, object?[] values) in rows)
            {
                // Share a physical row's array across the combos it appears in (counter-accumulation semantics);
                // a derived row has no identity to share on, so use it directly.
                acc[i] = tables[i].Table is null ? (id, values) : (id, Shared(tables[i].Alias, id, values));
                if (Holds(ons[i], i + 1))
                {
                    matched = true;
                    Recurse(i + 1);
                }
            }

            // LEFT join: an outer row with no matching inner row is still emitted, with the inner side all-null.
            if (kinds[i] == JoinKind.Left && !matched)
            {
                acc[i] = (default, new object?[tables[i].Columns.Count]);
                Recurse(i + 1);
            }
        }

        Recurse(0);
        return result;
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
        if (on is null || tables[i].Table is null) return null; // a derived table has no index to seek
        TableDef def = tables[i].Table!.Definition;
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
        Expression colSide, Expression keySide, string alias, TableDef def, HashSet<string> earlier)
    {
        if (colSide is not ColumnReference c
            || (c.Table is { } t && !string.Equals(t, alias, StringComparison.OrdinalIgnoreCase))
            || !def.Columns.Any(cd => string.Equals(cd.Name, c.Column, StringComparison.OrdinalIgnoreCase)))
            return null;

        IndexDef? index = def.Indexes.FirstOrDefault(ix => ix.RootPage > 0 && ix.Columns.Count == 1
            && string.Equals(ix.Columns[0].Column.Name, c.Column, StringComparison.OrdinalIgnoreCase));
        return index is not null && ReferencesOnly(keySide, earlier) ? (index, keySide) : null;
    }

    private static bool ReferencesOnly(Expression e, HashSet<string> aliases) => e switch
    {
        ColumnReference { Table: { } t } => aliases.Contains(t),
        ColumnReference => false, // unqualified — can't attribute it to an earlier table safely
        LiteralExpression or ParameterExpression or SystemVariableExpression => true,
        BinaryExpression b => ReferencesOnly(b.Left, aliases) && ReferencesOnly(b.Right, aliases),
        UnaryExpression u => ReferencesOnly(u.Operand, aliases),
        FunctionCall f => f.Arguments.All(a => ReferencesOnly(a, aliases)),
        _ => false,
    };

    /// <summary>The source-table index a SET assignment (or a delete target) applies to: the alias/table-name
    /// qualifier if given, else the single table (ambiguous when there are several).</summary>
    private static int TargetIndex(List<SourceTable> tables, string? qualifier, string what)
    {
        if (qualifier is null)
            return tables.Count == 1 ? 0
                : throw new InvalidOperationException($"{what} must be table-qualified when the statement joins several tables.");
        int i = tables.FindIndex(t =>
            string.Equals(t.Alias, qualifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Table?.Name, qualifier, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : throw new InvalidOperationException($"{what} '{qualifier}' is not one of the statement's tables.");
    }

    /// <summary>The physical <see cref="Table"/> a SET/DELETE targets — a derived table (subquery source) has no
    /// rows to write back, so targeting one is rejected.</summary>
    private static Table TargetTable(List<SourceTable> tables, int ti) =>
        tables[ti].Table ?? throw new NotSupportedException($"Cannot UPDATE/DELETE the derived table '{tables[ti].Alias}'.");

    /// <summary>
    /// Executes UPDATE tableexpression SET col = expr, … [WHERE criteria]. The table expression may be a join,
    /// and each SET target may name a specific joined table (Access's multi-table update). Each SET expression
    /// may reference the current values; the WHERE is an ordinary expression (correlated EXISTS included).
    /// Rows are rewritten in place (row id preserved). @@ROWCOUNT = matched join rows.
    /// </summary>
    private int ExecuteUpdate(UpdateStatement statement)
    {
        var (tables, kinds, ons) = ResolveSource(statement.From);
        var columns = tables.SelectMany(t => t.Columns).ToList();
        List<(RowId Id, object?[] Values)[]> joinRows = JoinRows(tables, kinds, ons, statement.Where, columns);

        // Resolve each assignment to its (table index, column) once.
        var targets = statement.Assignments.Select(a =>
        {
            int ti = TargetIndex(tables, a.Table, "UPDATE SET column");
            Table tt = TargetTable(tables, ti);
            ColumnDef col = tt.Definition.FindColumn(a.Column)
                ?? throw new InvalidOperationException($"Column '{a.Column}' does not exist in '{tt.Name}'.");
            return (TableIndex: ti, Column: col, a.Value);
        }).ToList();

        // Apply SETs to the shared value arrays; snapshot each touched row's original bytes on first touch.
        var dirty = new Dictionary<(string, RowId), (Table Table, RowId Id, object?[] Original, object?[] Values)>();
        foreach (var combo in joinRows)
        {
            foreach ((int ti, ColumnDef col, Expression valueExpr) in targets)
            {
                object?[] shared = combo[ti].Values;
                var key = (tables[ti].Alias, combo[ti].Id);
                if (!dirty.ContainsKey(key)) dirty[key] = (TargetTable(tables, ti), combo[ti].Id, (object?[])shared.Clone(), shared);

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

            // UPDATE must preserve the same Required/NOT NULL invariant as INSERT. Check the complete
            // post-assignment row before any referential action, row rewrite, or index mutation occurs.
            EnforceRequired(table.Name, table.Definition.Columns, values);

            // Child side: a changed FK column must still reference an existing parent (like an insert).
            if (_database.Catalog.ForeignKeysOf(table.Name).Any(f => f.IsEnforced &&
                    f.Columns.Any(c => changed.Contains(table.Definition.FindColumn(c.Column)!.Index))))
                EnforceReferentialIntegrity(table.Name, table, values);

            // A changed UNIQUE/PRIMARY key must not collide with another row (null keys are distinct — a
            // unique index permits multiple nulls, so they're skipped, matching the insert rule).
            foreach (IndexDef index in table.Definition.Indexes
                .Where(i => i.IsUnique && i.RootPage > 0 && i.Columns.Any(c => changed.Contains(c.Column.Index)))
                .GroupBy(i => i.RootPage).Select(g => g.First()))
                if (!index.Columns.Any(c => values[c.Column.Index] is null) && table.HasDuplicateKey(index, values, id))
                    throw new ConstraintViolationException(
                        $"Cannot update '{table.Name}': a row with the same " +
                        $"{(index.IsPrimaryKey ? "primary key" : "unique key")} already exists (index '{index.Name}').",
                        index.Name,
                        index.IsPrimaryKey);

            // The updated row must still satisfy every CHECK constraint (evaluated against the full new row).
            EnforceCheckConstraints(table.Definition, values);

            // Parent side: a changed referenced-key column triggers each relationship's ON UPDATE action
            // (CASCADE rewrites children, NO ACTION rejects if children exist).
            CascadeParentKeyUpdate(table.Name, original, values);

            table.Update(id, values, changed);
            foreach (IndexDef index in table.Definition.Indexes
                .Where(i => i.RootPage > 0 && i.Columns.Any(c => changed.Contains(c.Column.Index)))
                .GroupBy(i => i.RootPage).Select(g => g.First()))
                table.MoveIndexEntry(index, original, values, id);
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
        var (tables, kinds, ons) = ResolveSource(statement.From);
        var columns = tables.SelectMany(t => t.Columns).ToList();
        List<(RowId Id, object?[] Values)[]> joinRows = JoinRows(tables, kinds, ons, statement.Where, columns);

        int ti = TargetIndex(tables, statement.TargetTable, "DELETE target");
        Table target = TargetTable(tables, ti);

        var deleted = new Dictionary<RowId, object?[]>();
        foreach (var combo in joinRows)
            deleted.TryAdd(combo[ti].Id, combo[ti].Values); // one delete per distinct target row

        // Delete the distinct target rows and everything ON DELETE CASCADE reaches, children before parents.
        CascadeDelete(target, deleted.Select(kv => (kv.Key, kv.Value)));

        int affected = deleted.Count;
        if (_session is not null) _session.RowCount = affected;
        return affected;
    }
}
