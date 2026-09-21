using System.Globalization;
using LibRed.Sql.Ast;
using static LibRed.Sql.Grammar.AccessSqlParser;

namespace LibRed.Sql.Parsing;

/// <summary>
/// Lowers an ANTLR parse tree into the engine's <see cref="SqlNode"/> AST, so nothing
/// downstream depends on the generated grammar types.
/// </summary>
internal static class AstBuilder
{
    public static SqlStatement Build(StatementContext ctx)
    {
        SqlStatement statement = BuildBody(ctx);

        // A leading PARAMETERS clause (Access) declares the query's parameters. References to a declared
        // name in the body are lowered from column references to parameters, so the engine binds them from
        // the supplied values. Emitted when a stored parameterized query is read back.
        if (ctx.parametersClause() is { } pc)
        {
            var names = pc.procParam()
                .Select(ParamName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            statement = LowerParameters(statement, names);
        }
        return statement;
    }

    private static SqlStatement BuildBody(StatementContext ctx)
    {
        if (ctx.ifThenStatement() is { } ifThen) return BuildIfThen(ifThen);
        if (ctx.createTableStatement() is { } create) return BuildCreateTable(create);
        if (ctx.createIndexStatement() is { } createIndex) return BuildCreateIndex(createIndex);
        if (ctx.createViewStatement() is { } createView) return BuildCreateView(createView);
        if (ctx.createProcedureStatement() is { } createProc) return BuildCreateProcedure(createProc);
        if (ctx.alterTableStatement() is { } alter) return BuildAlterTable(alter);
        if (ctx.dropStatement() is { } drop) return BuildDrop(drop);
        if (ctx.insertStatement() is { } insert) return BuildInsert(insert);
        if (ctx.updateStatement() is { } update) return BuildUpdate(update);
        if (ctx.deleteStatement() is { } delete) return BuildDelete(delete);
        if (ctx.transactionStatement() is { } txn) return BuildTransaction(txn);
        if (ctx.executeStatement() is { } exec) return BuildExecute(exec);
        if (ctx.systemVariableSelect() is { } sysSelect) return BuildSystemVariableSelect(sysSelect);
        return BuildQueryExpression(ctx.queryExpression());
    }

    private static TransactionControlStatement BuildTransaction(TransactionStatementContext ctx) => ctx switch
    {
        BeginTransactionStatementContext => new TransactionControlStatement(TransactionControlKind.Begin),
        CommitTransactionStatementContext => new TransactionControlStatement(TransactionControlKind.Commit),
        RollbackTransactionStatementContext => new TransactionControlStatement(TransactionControlKind.Rollback),
        _ => throw new NotSupportedException($"Unsupported transaction statement: {ctx.GetText()}"),
    };

    private static IfThenStatement BuildIfThen(IfThenStatementContext ctx) =>
        new IfThenStatement(ctx.not is not null, BuildQueryExpression(ctx.queryExpression()), BuildThenBody(ctx.thenBody()));

    private static SqlStatement BuildThenBody(ThenBodyContext ctx)
    {
        if (ctx.createTableStatement() is { } create) return BuildCreateTable(create);
        if (ctx.createIndexStatement() is { } createIndex) return BuildCreateIndex(createIndex);
        if (ctx.createViewStatement() is { } createView) return BuildCreateView(createView);
        if (ctx.createProcedureStatement() is { } createProc) return BuildCreateProcedure(createProc);
        if (ctx.alterTableStatement() is { } alter) return BuildAlterTable(alter);
        if (ctx.dropStatement() is { } drop) return BuildDrop(drop);
        if (ctx.insertStatement() is { } insert) return BuildInsert(insert);
        if (ctx.updateStatement() is { } update) return BuildUpdate(update);
        if (ctx.deleteStatement() is { } delete) return BuildDelete(delete);
        if (ctx.executeStatement() is { } exec) return BuildExecute(exec);
        return BuildQueryExpression(ctx.queryExpression());
    }

    private static ExecuteStatement BuildExecute(ExecuteStatementContext ctx) =>
        new(Identifier(ctx.name), ctx.expression().Select(BuildExpression).ToList());

    private static SqlStatement BuildDrop(DropStatementContext ctx) => ctx switch
    {
        DropTableStatementContext t => new DropTableStatement(Identifier(t.table)),
        DropIndexStatementContext i => new DropIndexStatement(Identifier(i.index), Identifier(i.table)),
        DropProcedureStatementContext p => new DropProcedureStatement(Identifier(p.proc)),
        DropViewStatementContext v => new DropViewStatement(Identifier(v.view)),
        _ => throw new NotSupportedException($"Unsupported DROP statement: {ctx.GetText()}"),
    };

    private static DeleteStatement BuildDelete(DeleteStatementContext ctx) =>
        new(OptionalIdentifier(ctx.target),
            BuildTableSources(ctx.tableSource()),
            ctx.whereClause() is { } w ? BuildExpression(w.expression()) : null);

    private static UpdateStatement BuildUpdate(UpdateStatementContext ctx)
    {
        var assignments = ctx.assignment()
            .Select(a => new Assignment(
                OptionalIdentifier(a.target.qualifier), Identifier(a.target.name), BuildExpression(a.expression())))
            .ToList();
        Expression? where = ctx.whereClause() is { } w ? BuildExpression(w.expression()) : null;
        return new UpdateStatement(BuildTableSources(ctx.tableSource()), assignments, where);
    }

    private static SystemVariableSelectStatement BuildSystemVariableSelect(SystemVariableSelectContext ctx)
    {
        var items = ctx.sysVarItem().Select(i => new SelectItem(
            new SystemVariableExpression(i.SYSVAR().GetText().TrimStart('@')),
            i.alias is null ? null : Identifier(i.alias))).ToList();
        return new SystemVariableSelectStatement(items);
    }

    private static CreateTableStatement BuildCreateTable(CreateTableStatementContext ctx)
    {
        if (ctx.temp is not null)
            throw new NotSupportedException("CREATE TEMPORARY TABLE is not supported.");

        var columns = ctx.columnDefinition().Select(BuildColumnDefinition).ToList();

        // Primary key, foreign keys and unique constraints come from both column-level (single-field)
        // and table-level (multi-field) constraints.
        var primaryKey = columns.Where(c => c.PrimaryKey).Select(c => c.Name).ToList();
        var foreignKeys = new List<ForeignKeyConstraint>();
        var uniques = new List<UniqueConstraint>();
        var checks = new List<CheckConstraint>();
        // The PRIMARY KEY constraint's name, from whichever form declared it (column- or table-level).
        string? primaryKeyName = null;

        // Where each foreign key and the primary key sit in the statement's text, for a self-reference below.
        var foreignKeyTokens = new List<int>();
        int primaryKeyToken = int.MaxValue;

        // Column-level UNIQUE and REFERENCES (the single-field forms) apply to the column they follow.
        foreach (ColumnDefinitionContext cd in ctx.columnDefinition())
        {
            ColumnKeys keys = ColumnKeysOf(cd);
            foreach (PrimaryKeyConstraintContext p in keys.PrimaryKeys)
            {
                if (p.cname is not null) primaryKeyName = Identifier(p.cname);
                primaryKeyToken = Math.Min(primaryKeyToken, p.Start.TokenIndex);
            }
            uniques.AddRange(keys.Uniques);
            foreach ((ForeignKeyConstraint fk, int token) in keys.References)
            {
                foreignKeys.Add(fk);
                foreignKeyTokens.Add(token);
            }
        }

        foreach (TableConstraintContext tc in ctx.tableConstraint())
        {
            switch (tc)
            {
                case PrimaryKeyTableConstraintContext pk:
                    primaryKey.AddRange(pk._columns.Select(Identifier));
                    if (pk.name is not null) primaryKeyName = Identifier(pk.name);
                    primaryKeyToken = Math.Min(primaryKeyToken, pk.Start.TokenIndex);
                    break;
                case UniqueTableConstraintContext uq:
                    uniques.Add(new UniqueConstraint(uq.name is null ? null : Identifier(uq.name), uq._columns.Select(Identifier).ToList()));
                    break;
                case ForeignKeyTableConstraintContext fk:
                    foreignKeys.Add(BuildForeignKey(fk));
                    foreignKeyTokens.Add(fk.Start.TokenIndex);
                    break;
                case CheckTableConstraintContext ck:
                    checks.Add(new CheckConstraint(
                        ck.name is null ? null : Identifier(ck.name), OriginalText(ck.checkBody())));
                    break;
            }
        }

        // REFERENCES with no column list names the parent's primary key. For a table referencing itself that is
        // the key this statement declares, but only if it is declared earlier in the text: ACE resolves each
        // reference as it reaches it, so against a key declared after it there is none yet (verified — ACE then
        // reports that the table has no primary key). Resolved here, where the text order is known; a reference
        // left without columns is refused when the statement runs.
        string table = Identifier(ctx.table);
        for (int i = 0; i < foreignKeys.Count; i++)
        {
            ForeignKeyConstraint fk = foreignKeys[i];
            if (fk.ReferencedColumns.Count == 0
                && string.Equals(fk.ReferencedTable, table, StringComparison.OrdinalIgnoreCase)
                && primaryKeyToken < foreignKeyTokens[i])
                foreignKeys[i] = fk with { ReferencedColumns = primaryKey.ToList() };
        }

        return new CreateTableStatement(table, columns, primaryKey, foreignKeys, uniques, checks, primaryKeyName);
    }

    /// <summary>The verbatim source text of a parse context (preserving spacing), via the input stream —
    /// unlike <c>GetText()</c>, which concatenates token text with no whitespace. Used to store a CHECK
    /// expression exactly as written (matching Access).</summary>
    private static string OriginalText(Antlr4.Runtime.ParserRuleContext ctx) =>
        ctx.Start is null || ctx.Stop is null
            ? ctx.GetText()
            : ctx.Start.InputStream.GetText(Antlr4.Runtime.Misc.Interval.Of(ctx.Start.StartIndex, ctx.Stop.StopIndex));

    private static AlterTableStatement BuildAlterTable(AlterTableStatementContext ctx)
    {
        AlterTableAction action = ctx.alterTableAction() switch
        {
            AddColumnActionContext a => BuildAddColumn(a.columnDefinition()),
            AddConstraintActionContext a => BuildAddConstraint(a.tableConstraint()),
            AlterColumnActionContext a => new AlterColumnAction(
                Identifier(a.field), TypeName(a.dataType()), Size(a.dataType()), Scale(a.dataType()),
                a.columnConstraint().OfType<DefaultConstraintContext>().FirstOrDefault()?.expression().GetText(),
                // NOT NULL → true, NULL → false, neither → null (leave the column's nullability unchanged).
                a.columnConstraint().OfType<NotNullConstraintContext>().Any() ? true
                    : a.columnConstraint().OfType<NullableConstraintContext>().Any() ? false : null,
                IdentityOf(a.columnConstraint())),
            AlterColumnSetDefaultActionContext a => new AlterColumnSetDefaultAction(
                Identifier(a.field), OriginalText(a.expression())),
            AlterColumnDropDefaultActionContext a => new AlterColumnDropDefaultAction(Identifier(a.field)),
            DropColumnActionContext a => new DropColumnAction(Identifier(a.field)),
            DropConstraintActionContext a => new DropConstraintAction(Identifier(a.cname)),
            RenameTableActionContext a => new RenameTableAction(Identifier(a.newName)),
            RenameColumnActionContext a => new RenameColumnAction(Identifier(a.field), Identifier(a.newName)),
            RenameIndexActionContext a => new RenameIndexAction(Identifier(a.index), Identifier(a.newName)),
            _ => throw new SqlParseException("Unsupported ALTER TABLE action."),
        };
        return new AlterTableStatement(Identifier(ctx.table), action);
    }

    /// <summary>ADD COLUMN with the constraints its column carries: they apply to the new column, as in CREATE
    /// TABLE.</summary>
    private static AddColumnAction BuildAddColumn(ColumnDefinitionContext ctx)
    {
        ColumnKeys keys = ColumnKeysOf(ctx);
        return new AddColumnAction(
            BuildColumnDefinition(ctx),
            keys.References.FirstOrDefault().Constraint,
            keys.Uniques.FirstOrDefault(),
            keys.PrimaryKeys.FirstOrDefault()?.cname is { } name ? Identifier(name) : null);
    }

    /// <summary>The keys a column's own constraints declare, for CREATE TABLE and ADD COLUMN alike: its PRIMARY KEY
    /// constraints, a unique constraint per UNIQUE, and a foreign key per REFERENCES with where each sits in the
    /// statement's text.</summary>
    private readonly record struct ColumnKeys(
        List<PrimaryKeyConstraintContext> PrimaryKeys,
        List<UniqueConstraint> Uniques,
        List<(ForeignKeyConstraint Constraint, int Token)> References);

    private static ColumnKeys ColumnKeysOf(ColumnDefinitionContext column)
    {
        string name = Identifier(column.name);
        ColumnConstraintContext[] constraints = column.columnConstraint();
        return new ColumnKeys(
            constraints.OfType<PrimaryKeyConstraintContext>().ToList(),
            constraints.OfType<UniqueColumnConstraintContext>()
                .Select(u => new UniqueConstraint(u.cname is null ? null : Identifier(u.cname), [name])).ToList(),
            constraints.OfType<ColumnReferencesConstraintContext>()
                .Select(r => (BuildColumnReferences(r, name), r.Start.TokenIndex)).ToList());
    }

    private static AlterTableAction BuildAddConstraint(TableConstraintContext tc) => tc switch
    {
        PrimaryKeyTableConstraintContext pk =>
            new AddPrimaryKeyAction(pk.name is null ? null : Identifier(pk.name), pk._columns.Select(Identifier).ToList()),
        UniqueTableConstraintContext uq =>
            new AddUniqueAction(new UniqueConstraint(uq.name is null ? null : Identifier(uq.name), uq._columns.Select(Identifier).ToList())),
        ForeignKeyTableConstraintContext fk => new AddForeignKeyAction(BuildForeignKey(fk)),
        CheckTableConstraintContext ck =>
            new AddCheckAction(new CheckConstraint(ck.name is null ? null : Identifier(ck.name), OriginalText(ck.checkBody()))),
        _ => throw new SqlParseException("Unsupported ALTER TABLE ADD CONSTRAINT."),
    };

    private static int? Size(DataTypeContext type) => SignedInteger(type.size);
    private static int? Scale(DataTypeContext type) => SignedInteger(type.scale);

    private static int? SignedInteger(SignedIntegerContext? value) =>
        value is null ? null : int.Parse(value.GetText(), CultureInfo.InvariantCulture);

    private static ForeignKeyConstraint BuildForeignKey(ForeignKeyTableConstraintContext ctx)
    {
        var columns = ctx._columns.Select(Identifier).ToList();
        var refColumns = ctx._refColumns.Select(Identifier).ToList();
        var (onUpdate, onDelete) = ReadForeignKeyActions(ctx.foreignKeyAction());
        return new ForeignKeyConstraint(
            ctx.name is null ? null : Identifier(ctx.name),
            columns,
            Identifier(ctx.refTable),
            refColumns,
            onDelete,
            onUpdate,
            NoIndex: ctx.noIndex is not null);
    }

    /// <summary>Builds a foreign key from a column-level REFERENCES constraint (child column = the
    /// column it follows). A column-level FK never carries the NO INDEX modifier.</summary>
    private static ForeignKeyConstraint BuildColumnReferences(ColumnReferencesConstraintContext ctx, string childColumn)
    {
        var refColumns = ctx._refColumns.Select(Identifier).ToList();
        var (onUpdate, onDelete) = ReadForeignKeyActions(ctx.foreignKeyAction());
        return new ForeignKeyConstraint(
            ctx.cname is null ? null : Identifier(ctx.cname),
            [childColumn],
            Identifier(ctx.refTable),
            refColumns,
            onDelete,
            onUpdate);
    }

    /// <summary>Reads the ON UPDATE / ON DELETE clauses in either order (each optional).</summary>
    private static (ReferentialAction OnUpdate, ReferentialAction OnDelete) ReadForeignKeyActions(
        IEnumerable<ForeignKeyActionContext> actions)
    {
        var onUpdate = ReferentialAction.NoAction;
        var onDelete = ReferentialAction.NoAction;
        foreach (ForeignKeyActionContext action in actions)
        {
            switch (action)
            {
                case OnUpdateActionContext u: onUpdate = ReferentialActionOf(u.referentialAction()); break;
                case OnDeleteActionContext d: onDelete = ReferentialActionOf(d.referentialAction()); break;
            }
        }
        return (onUpdate, onDelete);
    }

    private static ReferentialAction ReferentialActionOf(ReferentialActionContext? ctx) => ctx switch
    {
        CascadeActionContext => ReferentialAction.Cascade,
        SetNullActionContext => ReferentialAction.SetNull,
        SetDefaultActionContext => ReferentialAction.SetDefault,
        _ => ReferentialAction.NoAction, // null, NO ACTION, RESTRICT
    };

    private static ColumnDefinition BuildColumnDefinition(ColumnDefinitionContext ctx)
    {
        DataTypeContext type = ctx.dataType();
        int? size = Size(type);
        int? scale = Scale(type);

        string typeName = TypeName(type);

        bool compressed = ctx.columnConstraint().OfType<CompressionConstraintContext>().Any();
        bool notNull = ctx.columnConstraint().OfType<NotNullConstraintContext>().Any();
        bool primaryKey = ctx.columnConstraint().OfType<PrimaryKeyConstraintContext>().Any();
        // Capture the DEFAULT expression's source text (stored verbatim as the DefaultValue property,
        // matching Access — e.g. "42", "'hi'"). Re-parsed and evaluated when a column is omitted on insert.
        string? defaultSql = ctx.columnConstraint().OfType<DefaultConstraintContext>()
            .FirstOrDefault()?.expression().GetText();
        // OriginalText, not GetText: the expression is stored verbatim and re-read by whichever engine opens
        // the file, so its spacing and quoting have to survive the round trip through the parser.
        string? calculated = ctx.calculatedClause() is { } calc ? OriginalText(calc.expression()) : null;

        return new ColumnDefinition(
            Identifier(ctx.name), typeName, size, scale, notNull, primaryKey, defaultSql, compressed,
            calculated, IdentityOf(ctx.columnConstraint()));
    }

    /// <summary>A column's IDENTITY attribute — the last one written, when there are several — or null. ACE takes
    /// it only straight after the type, NULL/NOT NULL or another IDENTITY: after DEFAULT, PRIMARY KEY or any
    /// other constraint it is a syntax error there (verified), so it is one here too.</summary>
    private static IdentitySpec? IdentityOf(IEnumerable<ColumnConstraintContext> constraints)
    {
        IdentitySpec? identity = null;
        bool afterOtherConstraint = false;
        foreach (ColumnConstraintContext constraint in constraints)
        {
            switch (constraint)
            {
                case IdentityConstraintContext id:
                    if (afterOtherConstraint)
                        throw new SqlParseException(
                            "Syntax error in field definition: IDENTITY must come before DEFAULT, PRIMARY KEY and the " +
                            "column's other constraints.");
                    identity = new IdentitySpec(SignedInteger(id.seed), SignedInteger(id.increment));
                    break;
                case NotNullConstraintContext or NullableConstraintContext:
                    break;
                default:
                    afterOtherConstraint = true;
                    break;
            }
        }
        return identity;
    }

    private static CreateIndexStatement BuildCreateIndex(CreateIndexStatementContext ctx)
    {
        var columns = ctx.indexColumn()
            .Select(ic => (Identifier(ic.col), Descending: ic.dir is { } d && d.Type == DESC))
            .ToList();
        IndexWithOption withOption = ctx.withOption() switch
        {
            WithPrimaryContext => IndexWithOption.Primary,
            WithDisallowNullContext => IndexWithOption.DisallowNull,
            WithIgnoreNullContext => IndexWithOption.IgnoreNull,
            _ => IndexWithOption.None,
        };
        return new CreateIndexStatement(
            Identifier(ctx.name), Identifier(ctx.table), ctx.unique is not null, columns, withOption);
    }

    private static CreateViewStatement BuildCreateView(CreateViewStatementContext ctx)
    {
        var columns = ctx._columns.Select(Identifier).ToList();
        ViewDefinition definition = BuildViewDefinition(ctx.query);
        return new CreateViewStatement(Identifier(ctx.name), columns, definition, OriginalText(ctx.query));
    }

    private static SqlStatement BuildCreateProcedure(CreateProcedureStatementContext ctx)
    {
        var parameters = (ctx.procParamList()?.procParam() ?? [])
            .Select(p => new ProcedureParameter(
                ParamName(p), TypeName(p.dataType()), Size(p.dataType()), Scale(p.dataType())))
            .ToList();

        // A procedure body is a SELECT (stored as a parameterized query, like a view) or an action query
        // (CREATE TABLE / INSERT — stored byte-faithfully in their own MSysQueries shape).
        ProcedureBodyContext body = ctx.body;
        string name = Identifier(ctx.name);

        if (body.createTableStatement() is { } ddl)
        {
            // A data-definition query is stored as its verbatim text, with nothing decomposed — there is no
            // row for a parameter to reach, so one declared here could never bind.
            if (parameters.Count > 0)
                throw new NotSupportedException(
                    "Parameters on a data-definition procedure are not supported: its SQL is stored verbatim.");
            return new CreateActionProcedureStatement(
                name, ProcedureActionKind.DataDefinition, OriginalText(ddl), null, null);
        }
        if (body.insertStatement() is { } insert) return BuildAppendProcedure(name, insert, parameters);
        if (body.updateStatement() is { } update) return BuildUpdateProcedure(name, update, parameters);
        if (body.deleteStatement() is { } delete) return BuildDeleteProcedure(name, delete, parameters);

        QueryExpressionContext query = body.queryExpression();
        // A SELECT with an INTO is a make-table query — an action query that stores its target on the action
        // row — not a view of the SELECT.
        if (MakeTableTarget(query) is { } target)
            return new CreateActionProcedureStatement(
                name, ProcedureActionKind.MakeTable, null, target, null,
                BuildViewDefinition(query), null, parameters);

        ViewDefinition definition = BuildViewDefinition(query);
        return new CreateProcedureStatement(name, parameters, definition, OriginalText(query));
    }

    /// <summary>The table a <c>SELECT … INTO t</c> body writes into, or null for an ordinary SELECT.</summary>
    private static string? MakeTableTarget(QueryExpressionContext ctx) =>
        ctx.setOperator().Length == 0 && ctx.queryTerm(0) is SelectTermContext term
        && term.querySpecification().into is { } into
            ? Identifier(into)
            : null;

    private static CreateActionProcedureStatement BuildAppendProcedure(
        string name, InsertStatementContext insert, IReadOnlyList<ProcedureParameter> parameters)
    {
        var columns = insert._columns;
        if (columns.Count == 0)
            throw new NotSupportedException("An INSERT procedure body must list its target columns.");

        // The multiple-record form: the values come from a SELECT, which is stored as the query's own source
        // — the same table / join / where rows a view stores — with each column row naming what it reads.
        if (insert.source is { } source)
        {
            ViewDefinition body = BuildViewDefinition(source);
            if (body.Columns.Count != columns.Count)
                throw new SqlParseException(
                    $"INSERT lists {columns.Count} columns but its SELECT returns {body.Columns.Count}.");

            var sourced = columns
                .Select((col, i) => new AppendColumn(Identifier(col), body.Columns[i].Expression))
                .ToList();
            return new CreateActionProcedureStatement(
                name, ProcedureActionKind.Append, null, Identifier(insert.table), sourced, body, null, parameters);
        }

        // A stored append query keeps its columns and values as text pairs, which has room for exactly one
        // row — so a multi-row table value constructor cannot be stored as a procedure even though it is
        // perfectly valid in a plain INSERT.
        var rows = insert.rowValues();
        if (rows.Length != 1)
            throw new NotSupportedException(
                "An INSERT procedure body must supply exactly one VALUES row.");

        // Each value is stored as its original text, so an explicit DEFAULT has nothing to store — it is a
        // marker rather than an expression. Rejected here for the same reason a multi-row constructor is.
        var rowValues = rows[0].rowValue();
        if (rowValues.Any(v => v.DEFAULT() is not null))
            throw new NotSupportedException(
                "An INSERT procedure body cannot use DEFAULT as a value.");

        var values = rowValues.Select(v => v.expression()).ToArray();
        if (columns.Count != values.Length)
            throw new SqlParseException(
                $"INSERT lists {columns.Count} columns but {values.Length} values.");

        var appendColumns = columns
            .Select((col, i) => new AppendColumn(Identifier(col), OriginalText(values[i])))
            .ToList();
        return new CreateActionProcedureStatement(
            name, ProcedureActionKind.Append, null, Identifier(insert.table), appendColumns,
            null, null, parameters);
    }

    /// <summary>An UPDATE body: its sources and WHERE are stored exactly as a view's are, and each SET
    /// assignment becomes a column row naming its target (qualified, over a join) and holding the new
    /// value's verbatim text.</summary>
    private static CreateActionProcedureStatement BuildUpdateProcedure(
        string name, UpdateStatementContext update, IReadOnlyList<ProcedureParameter> parameters)
    {
        var assignments = update.assignment()
            .Select(a => new AppendColumn(OriginalText(a.target), OriginalText(a.expression())))
            .ToList();
        return new CreateActionProcedureStatement(
            name, ProcedureActionKind.Update, null, null, assignments,
            ActionBody(update.tableSource(), update.whereClause()), null, parameters);
    }

    /// <summary>A DELETE body: its sources and WHERE, plus the <c>table.*</c> target when the statement names
    /// one — which Access stores verbatim, and omits entirely for a bare <c>DELETE FROM</c>.</summary>
    private static CreateActionProcedureStatement BuildDeleteProcedure(
        string name, DeleteStatementContext delete, IReadOnlyList<ProcedureParameter> parameters)
    {
        string? target = delete.target is { } t ? $"{Identifier(t)}.*" : null;
        return new CreateActionProcedureStatement(
            name, ProcedureActionKind.Delete, null, null, null,
            ActionBody(delete.tableSource(), delete.whereClause()), target, parameters);
    }

    /// <summary>The sources, joins and WHERE of an UPDATE or DELETE body, in the shape a view stores them —
    /// they carry no output columns of their own.</summary>
    private static ViewDefinition ActionBody(TableSourceContext[] sources, WhereClauseContext? where)
    {
        var tables = new List<ViewSource>();
        var joins = new List<ViewJoin>();
        foreach (TableSourceContext ts in sources) CollectSources(ts, tables, joins);

        return new ViewDefinition(
            Distinct: false, Columns: [], tables, joins,
            where is null ? null : OriginalText(where.expression()),
            GroupBy: [], OrderBy: [], Top: null);
    }

    /// <summary>A declared parameter's name, with any leading <c>@</c> stripped — Access stores the bare
    /// name (e.g. <c>@Beginning_Date</c> is stored as <c>Beginning_Date</c>).</summary>
    private static string ParamName(ProcParamContext p) => p.pname.PARAM() is { } at
        ? at.GetText().TrimStart('@')
        : Identifier(p.pname.identifier());

    /// <summary>The declared type name of a data type — up to three words (e.g. "national character varying")
    /// joined by single spaces.</summary>
    private static string TypeName(DataTypeContext type) => string.Join(' ',
        new[] { type.identityType is null ? null : "IDENTITY" }
            .Concat(new[] { type.typeName, type.extra, type.extra2 }.Where(t => t is not null).Select(Identifier))
            .Where(t => t is not null));

    // ---- PARAMETERS-clause lowering: unqualified references to a declared parameter become parameters ----

    private static SqlStatement LowerParameters(SqlStatement s, HashSet<string> names) => s switch
    {
        SelectStatement sel => LowerSelect(sel, names),
        SetOperationStatement set => set with
        {
            Left = LowerParameters(set.Left, names),
            Right = LowerParameters(set.Right, names),
        },
        InsertStatement ins => ins with
        {
            Rows = ins.Rows
                .Select(r => (IReadOnlyList<Expression>)r.Select(e => LowerExpr(e, names)).ToList())
                .ToList(),
            // The multiple-record form's source is a query in its own right, so a declared parameter can
            // appear in its WHERE just as it can in a VALUES list.
            Source = ins.Source is null ? null : LowerParameters(ins.Source, names),
        },
        // An action query declares parameters exactly as a SELECT does, and Access stores UPDATE and DELETE
        // ones — a stored `UPDATE t SET c = pValue WHERE k = pKey` reads back through here.
        UpdateStatement upd => upd with
        {
            From = LowerFrom(upd.From, names)!,
            Assignments = upd.Assignments.Select(a => a with { Value = LowerExpr(a.Value, names) }).ToList(),
            Where = upd.Where is null ? null : LowerExpr(upd.Where, names),
        },
        DeleteStatement del => del with
        {
            From = LowerFrom(del.From, names)!,
            Where = del.Where is null ? null : LowerExpr(del.Where, names),
        },
        _ => s,
    };

    private static SelectStatement LowerSelect(SelectStatement sel, HashSet<string> names) => sel with
    {
        Projection = sel.Projection.Select(i => i with { Value = LowerExpr(i.Value, names) }).ToList(),
        From = LowerFrom(sel.From, names),
        Where = sel.Where is null ? null : LowerExpr(sel.Where, names),
        GroupBy = sel.GroupBy.Select(e => LowerExpr(e, names)).ToList(),
        Having = sel.Having is null ? null : LowerExpr(sel.Having, names),
        OrderBy = sel.OrderBy.Select(o => o with { Value = LowerExpr(o.Value, names) }).ToList(),
    };

    private static TableReference? LowerFrom(TableReference? t, HashSet<string> names) => t switch
    {
        null => null, // FROM-less SELECT
        JoinTable j => j with
        {
            Left = LowerFrom(j.Left, names)!,   // a join always has both sides
            Right = LowerFrom(j.Right, names)!,
            On = j.On is null ? null : LowerExpr(j.On, names),
        },
        SubqueryTable sub => sub with { Query = LowerParameters(sub.Query, names) },
        _ => t,
    };

    private static Expression LowerExpr(Expression e, HashSet<string> names) => e switch
    {
        ColumnReference { Table: null, Column: var c } when names.Contains(c) => new ParameterExpression(c),
        // A window function lowers like any other call — arguments AND the OVER clause, since a PARAMETERS name
        // can appear in a PARTITION BY or ORDER BY expression just as readily as in an argument.
        WindowFunction w => w with
        {
            Arguments = w.Arguments.Select(a => LowerExpr(a, names)).ToList(),
            Filter = w.Filter is null ? null : LowerExpr(w.Filter, names),
            Over = w.Over with
            {
                PartitionBy = w.Over.PartitionBy.Select(p => LowerExpr(p, names)).ToList(),
                OrderBy = w.Over.OrderBy.Select(o => o with { Value = LowerExpr(o.Value, names) }).ToList(),
                Frame = w.Over.Frame is { } frame
                    ? frame with { Start = LowerBound(frame.Start, names), End = LowerBound(frame.End, names) }
                    : null,
            },
        },
        // LowerParameters, not LowerSelect: a subquery may be a set operation or a table value constructor.
        ScalarSubquery s => new ScalarSubquery(LowerParameters(s.Query, names)),
        ExistsExpression x => new ExistsExpression(LowerParameters(x.Query, names)),
        InSubqueryExpression i => i with { Value = LowerExpr(i.Value, names), Query = LowerParameters(i.Query, names) },
        _ => e.MapOperands(o => LowerExpr(o, names)),
    };

    private static FrameBound LowerBound(FrameBound bound, HashSet<string> names) =>
        bound.Offset is null ? bound : bound with { Offset = LowerExpr(bound.Offset, names) };

    /// <summary>Decomposes a view's "simple SELECT" into the columns/tables/joins/where Access stores as
    /// MSysQueries rows. Rejects anything Access itself rejects in a view (UNION, GROUP BY/aggregates,
    /// HAVING, ORDER BY) or that we can't decompose (a derived-table/subquery source).</summary>
    private static ViewDefinition BuildViewDefinition(QueryExpressionContext ctx)
    {
        if (ctx.setOperator().Length > 0)
            throw new NotSupportedException("A UNION query is not a valid (simple) view.");
        if (ctx.queryTerm(0) is not SelectTermContext term)
            throw new NotSupportedException("A parenthesised query is not a valid (simple) view.");

        QuerySpecificationContext select = term.querySpecification();
        if (select.havingClause() is not null)
            throw new NotSupportedException("A view with HAVING is not stored yet.");
        var groupBy = select.groupByClause() is { } g
            ? g.expression().Select(OriginalText).ToList() : (IReadOnlyList<string>)[];
        // The view's ORDER BY comes off the query expression, not the SELECT: it orders the view's result.
        var orderBy = ctx.orderByClause() is { } ob
            ? ob.orderByItem().Select(i => new ViewOrderBy(OriginalText(i.expression()), i.dir?.Type == DESC)).ToList()
            : (IReadOnlyList<ViewOrderBy>)[];
        // A stored view can only carry a literal TOP (Access stores it as text); reject a parameterized one.
        int? top = select.topClause() is { } t
            ? BuildTop(t) is LiteralExpression { Value: int n }
                ? n
                : throw new NotSupportedException("A view's TOP must be a literal integer.")
            : null;

        // Output columns; SELECT * becomes a single "*", a qualified star stays "Table.*".
        var columns = select.selectList().STAR() is not null && select.selectList().selectItem().Length == 0
            ? (IReadOnlyList<ViewColumn>)[new ViewColumn("*", null)]
            : select.selectList().selectItem().Select(BuildViewColumn).ToList();

        // Flatten the FROM into a flat list of source tables and joins, descending through any parenthesised
        // join groups (Access stores them flat — one Attribute=5 per table, one Attribute=7 per join). A
        // body with no FROM at all (`SELECT 1 AS n`) is a query Access stores and runs, and it stores it the
        // same way minus the table rows — so it decomposes to no sources rather than being rejected.
        var tables = new List<ViewSource>();
        var joins = new List<ViewJoin>();
        foreach (TableSourceContext ts in select.fromClause()?.tableSource() ?? [])
            CollectSources(ts, tables, joins);

        string? where = select.whereClause() is { } w ? OriginalText(w.expression()) : null;
        return new ViewDefinition(select.predicate?.DISTINCT() is not null, columns, tables, joins, where, groupBy, orderBy, top);
    }

    private static void CollectSources(TableSourceContext ts, List<ViewSource> tables, List<ViewJoin> joins)
    {
        CollectPrimary(ts.tablePrimary(), tables, joins);
        foreach (JoinClauseContext jc in ts.joinClause())
        {
            switch (jc)
            {
                case ConditionalJoinContext c:
                    CollectPrimary(c.tablePrimary(), tables, joins);
                    // Access records the join by the two tables named in its condition (Name1/Name2), not the
                    // structural left/right (which for a nested group is a whole subtree).
                    (string left, string right) = JoinSides(c.expression());
                    joins.Add(new ViewJoin(ViewJoinKindOf(c.joinType()), OriginalText(c.expression()), left, right));
                    break;

                // A CROSS JOIN contributes a source and no join: Access has no CROSS JOIN keyword and records
                // joins by their condition, so a cartesian product is stored exactly as the comma form is —
                // another table with nothing joining it. The two spellings decompose identically.
                case CrossJoinContext x:
                    CollectPrimary(x.tablePrimary(), tables, joins);
                    break;

                default:
                    throw new SqlParseException($"Unsupported join in a view: {jc.GetText()}");
            }
        }
    }

    private static void CollectPrimary(TablePrimaryContext tp, List<ViewSource> tables, List<ViewJoin> joins)
    {
        if (tp is ParenJoinPrimaryContext p)
            CollectSources(p.tableSource(), tables, joins); // a parenthesised join group flattens in place
        else
            tables.Add(BuildViewSource(tp).Source);
    }

    /// <summary>The two table qualifiers of a join condition (<c>T1.c = T2.c</c>) — the first two distinct
    /// column qualifiers, in order — which Access stores as the join's Name1/Name2.</summary>
    private static (string Left, string Right) JoinSides(ExpressionContext condition)
    {
        var qualifiers = new List<string>();
        void Walk(Expression e)
        {
            if (e is ColumnReference { Table: { } q })
            {
                if (!qualifiers.Contains(q)) qualifiers.Add(q);
                return;
            }
            foreach (Expression operand in e.Operands() ?? [])
                Walk(operand);
        }
        Walk(BuildExpression(condition));
        return (qualifiers.ElementAtOrDefault(0) ?? "", qualifiers.ElementAtOrDefault(1) ?? "");
    }

    private static ViewColumn BuildViewColumn(SelectItemContext ctx) => ctx switch
    {
        ExpressionSelectItemContext e => new ViewColumn(OriginalText(e.expression()), OptionalIdentifier(e.alias)),
        _ => new ViewColumn(OriginalText(ctx), null), // qualified star: "Table.*"
    };

    /// <summary>A view FROM source and the alias other clauses reference it by. A named table uses its
    /// name as the alias when unaliased; a derived table (subquery) stores its verbatim inner SQL and
    /// requires an explicit alias (as Access does).</summary>
    private static (ViewSource Source, string Alias) BuildViewSource(TablePrimaryContext ctx)
    {
        switch (ctx)
        {
            case NamedTablePrimaryContext n:
                string alias = n.alias is null ? Identifier(n.table) : Identifier(n.alias);
                return (new ViewSource(Identifier(n.table), n.alias is null ? null : Identifier(n.alias)), alias);
            case SubqueryPrimaryContext s when s.alias is not null:
                string subAlias = Identifier(s.alias);
                return (new ViewSource(Table: null, subAlias, OriginalText(s.queryExpression())), subAlias);
            case SubqueryPrimaryContext:
                throw new NotSupportedException("A derived-table source in a view requires an alias.");
            default:
                throw new NotSupportedException($"Unsupported view FROM source: {ctx.GetText()}");
        }
    }

    private static ViewJoinKind ViewJoinKindOf(JoinTypeContext ctx) => ctx switch
    {
        LeftJoinContext => ViewJoinKind.Left,
        RightJoinContext => ViewJoinKind.Right,
        // A view is stored in Access's own query format, which has no full outer join to encode - so unlike a
        // FULL JOIN executed directly, this one cannot be represented on disk. Refuse rather than silently
        // storing the INNER the fall-through would otherwise pick.
        FullJoinContext => throw new NotSupportedException(
            "A FULL JOIN cannot be stored in a view: the Access query format has no representation for it."),
        _ => ViewJoinKind.Inner,
    };

    private static InsertStatement BuildInsert(InsertStatementContext ctx)
    {
        string table = Identifier(ctx.table);
        if (ctx.DEFAULT() is not null)
            return new InsertStatement(table, [], [(IReadOnlyList<Expression>)[]], DefaultValues: true);

        var columns = ctx._columns.Select(Identifier).ToList();

        // The multiple-record form: the rows come from a query rather than a VALUES list.
        if (ctx.source is not null)
            return new InsertStatement(table, columns, [], Source: BuildQueryExpression(ctx.source));

        // A table value constructor: one or more parenthesised rows. The AST and executor were already
        // row-list shaped, so a multi-row insert needs nothing beyond handing them every row.
        var rows = ctx.rowValues()
            .Select(r => (IReadOnlyList<Expression>)r.rowValue().Select(BuildRowValue).ToList())
            .ToList();
        return new InsertStatement(table, columns, rows);
    }

    /// <summary>One row value of a table value constructor: the <c>DEFAULT</c> keyword, or any expression
    /// (which covers the NULL the standard lists separately, since NULL is already a literal).</summary>
    private static Expression BuildRowValue(RowValueContext ctx)
        => ctx.DEFAULT() is not null ? new DefaultValueExpression() : BuildExpression(ctx.expression());

    private static SqlStatement BuildQueryExpression(QueryExpressionContext ctx)
    {
        QueryTermContext[] terms = ctx.queryTerm();
        SetOperatorContext[] operators = ctx.setOperator();

        // The ordering and paging of the WHOLE expression — the grammar admits them here and nowhere else, so
        // there is nothing to disentangle: a leading TOP sits on its operand's own querySpecification, a FETCH
        // sits here, and the two can no longer be mistaken for each other.
        List<OrderByItem> orderBy = ctx.orderByClause() is { } ob
            ? ob.orderByItem().Select(BuildOrderByItem).ToList()
            : [];
        Expression? top = null, offset = null;
        if (ctx.offsetFetchClause() is { } paging)
        {
            offset = paging.offset is { } off ? BuildExpression(off) : null;
            top = paging.limit is { } lim ? BuildExpression(lim) : null;
        }
        bool ordered = orderBy.Count > 0 || top is not null || offset is not null;

        if (operators.Length == 0)
        {
            SqlStatement single = BuildQueryTerm(terms[0]);
            // A single term folds the clauses back into it, so an ordinary `SELECT … ORDER BY x` builds exactly
            // the AST it always did and nothing downstream sees this restructuring at all.
            return ordered && single is SelectStatement s
                ? s with { OrderBy = orderBy, Top = top ?? s.Top, Offset = offset }
                : single;
        }

        // A long chain of one associative operator — e.g. EF's 5000-way `UNION ALL` for a huge primitive
        // collection — must be built as a *balanced* tree. A left-nested tree is O(n) deep, and the binder,
        // planner and executor all recurse over it (plus the runtime nested LINQ Concat), overflowing the stack
        // (an uncatchable StackOverflowException that kills the process). Balancing makes every walk O(log n).
        // Set operators are left-associative, so this is only sound when every operator is the same and
        // associative (UNION / UNION ALL / INTERSECT — EXCEPT is not); mixed/EXCEPT chains left-fold as before
        // (they are correct that way and never occur thousands deep).
        var operands = new SqlStatement[terms.Length];
        for (int i = 0; i < terms.Length; i++)
            operands[i] = BuildQueryTerm(terms[i]);

        SetOperator first = SetOperatorOf(operators[0]);
        SetOperationStatement result;
        if (IsAssociative(first) && operators.All(o => SetOperatorOf(o) == first))
        {
            result = (SetOperationStatement)BuildBalanced(operands, first, 0, operands.Length - 1);
        }
        else
        {
            result = new SetOperationStatement(operands[0], SetOperatorOf(operators[0]), operands[1]);
            for (int i = 1; i < operators.Length; i++)
                result = new SetOperationStatement(result, SetOperatorOf(operators[i]), operands[i + 1]);
        }

        // Only the outermost node carries them: the ordering is the expression's, not that of any inner pair.
        return ordered ? result with { OrderBy = orderBy, Top = top, Offset = offset } : result;
    }


    // UNION / UNION ALL / INTERSECT are associative (can be regrouped); EXCEPT is not.
    private static bool IsAssociative(SetOperator op) =>
        op is SetOperator.Union or SetOperator.UnionAll or SetOperator.Intersect;

    // Builds a balanced binary set-operation tree over operands[lo..hi] — O(log n) depth, preserving the
    // left-to-right operand order (so UNION ALL row order is unchanged).
    private static SqlStatement BuildBalanced(SqlStatement[] operands, SetOperator op, int lo, int hi)
    {
        if (lo == hi)
            return operands[lo];
        int mid = lo + (hi - lo) / 2;
        return new SetOperationStatement(
            BuildBalanced(operands, op, lo, mid), op, BuildBalanced(operands, op, mid + 1, hi));
    }

    /// <summary>A set-operation operand: a SELECT, or a parenthesised (possibly nested) query expression.</summary>
    private static SqlStatement BuildQueryTerm(QueryTermContext ctx) => ctx switch
    {
        SelectTermContext s => BuildQuerySpecification(s.querySpecification()),
        ParenTermContext p => BuildQueryExpression(p.queryExpression()),
        ValuesTermContext v => BuildValuesQuery(v),
        _ => throw new SqlParseException($"Unsupported query term: {ctx.GetText()}"),
    };

    /// <summary>A table value constructor used as a query. Every row must be the same width, and DEFAULT is
    /// rejected: it means "the column's default", which only has a meaning when there is a target column —
    /// so the standard allows it in an INSERT alone.</summary>
    private static ValuesStatement BuildValuesQuery(ValuesTermContext ctx)
    {
        var rows = new List<IReadOnlyList<Expression>>();
        foreach (RowValuesContext row in ctx.rowValues())
        {
            var values = new List<Expression>();
            foreach (RowValueContext value in row.rowValue())
            {
                if (value.DEFAULT() is not null)
                    throw new SqlParseException("DEFAULT is only allowed in an INSERT's VALUES clause.");
                values.Add(BuildExpression(value.expression()));
            }

            if (rows.Count > 0 && values.Count != rows[0].Count)
                throw new SqlParseException(
                    $"VALUES rows must all have the same number of values ({rows[0].Count} then {values.Count}).");

            rows.Add(values);
        }

        return new ValuesStatement(rows);
    }

    private static SetOperator SetOperatorOf(SetOperatorContext ctx)
    {
        if (ctx.INTERSECT() != null) return SetOperator.Intersect;
        if (ctx.EXCEPT() != null) return SetOperator.Except;
        return ctx.ALL() != null ? SetOperator.UnionAll : SetOperator.Union;
    }

    /// <summary>The standard's &lt;query specification&gt;: an order-less SELECT. ORDER BY and OFFSET/FETCH are
    /// not part of it — the grammar puts them on the enclosing <c>queryExpression</c>, and
    /// <see cref="BuildQueryExpression"/> folds them back onto this statement when there is no set operation for
    /// them to belong to.</summary>
    private static SelectStatement BuildQuerySpecification(QuerySpecificationContext ctx)
    {
        SelectListContext list = ctx.selectList();
        bool star = list.STAR() != null;
        var projection = star
            ? (IReadOnlyList<SelectItem>)[]
            : list.selectItem().Select(BuildSelectItem).ToList();

        TableReference? from = ctx.fromClause() is { } fc ? BuildFrom(fc) : null;
        Expression? where = ctx.whereClause() is { } w ? BuildExpression(w.expression()) : null;
        var groupBy = ctx.groupByClause() is { } g
            ? g.expression().Select(BuildExpression).ToList()
            : (IReadOnlyList<Expression>)[];
        Expression? having = ctx.havingClause() is { } h ? BuildExpression(h.expression()) : null;
        Expression? top = ctx.topClause() is { } t ? BuildTop(t) : null;
        bool topPercent = ctx.topClause()?.percent is not null;

        var predicate = ctx.predicate;

        return new SelectStatement(projection, star, from, where, groupBy, having, OrderBy: [], top,
            Distinct: predicate?.DISTINCT() is not null,
            DistinctRow: predicate?.DISTINCTROW() is not null,
            TopPercent: topPercent,
            Into: ctx.into is null ? null : Identifier(ctx.into));
    }

    /// <summary>The TOP count expression: a single operand, or a left-associative +/- chain of them (each
    /// operand a literal, a parameter, or a parenthesised expression). Evaluated at execution.</summary>
    private static Expression BuildTop(TopClauseContext ctx)
    {
        Expression Operand(TopOperandContext o) =>
            o.INTEGER_LITERAL() is { } lit ? new LiteralExpression(ParseInteger(lit.GetText()))
            : o.PARAM() is { } p ? new ParameterExpression(p.GetText())
            : BuildExpression(o.expression());

        var operands = ctx.topOperand();
        Expression result = Operand(operands[0]);
        var ops = ctx.children.OfType<Antlr4.Runtime.Tree.ITerminalNode>()
            .Where(t => t.Symbol.Type is PLUS or MINUS).ToList();
        for (int i = 1; i < operands.Length; i++)
            result = new BinaryExpression(
                ops[i - 1].Symbol.Type == PLUS ? BinaryOperator.Add : BinaryOperator.Subtract,
                result, Operand(operands[i]));
        return result;
    }

    private static SelectItem BuildSelectItem(SelectItemContext ctx) => ctx switch
    {
        QualifiedStarSelectItemContext q => new SelectItem(new QualifiedStarExpression(Identifier(q.qualifier)), null),
        ExpressionSelectItemContext e => new SelectItem(BuildExpression(e.expression()), OptionalIdentifier(e.alias)),
        _ => throw new SqlParseException($"Unsupported select item: {ctx.GetText()}"),
    };

    private static TableReference BuildFrom(FromClauseContext ctx) => BuildTableSources(ctx.tableSource());

    /// <summary>A comma list of table sources, which is an implicit cross join (no ON).</summary>
    private static TableReference BuildTableSources(TableSourceContext[] sources)
    {
        TableReference table = BuildTableSource(sources[0]);
        foreach (TableSourceContext src in sources.Skip(1))
            table = new JoinTable(table, BuildTableSource(src), JoinKind.Cross, null);
        return table;
    }

    private static TableReference BuildTableSource(TableSourceContext ctx)
    {
        TableReference table = BuildTablePrimary(ctx.tablePrimary());
        foreach (JoinClauseContext join in ctx.joinClause())
        {
            // A CROSS JOIN carries no ON, so it builds the same node the comma form does: kind Cross with a
            // null condition. Everything downstream already understands that shape. The two APPLY kinds share
            // that shape too - what makes them lateral is the kind, which is all the executor needs to know to
            // re-run the right side per left row.
            table = join switch
            {
                ConditionalJoinContext c => new JoinTable(
                    table, BuildTablePrimary(c.tablePrimary()), JoinKindOf(c.joinType()), BuildExpression(c.expression())),
                CrossJoinContext x => new JoinTable(
                    table, BuildTablePrimary(x.tablePrimary()), JoinKind.Cross, null),
                CrossApplyContext a => new JoinTable(
                    table, BuildTablePrimary(a.tablePrimary()), JoinKind.CrossApply, null),
                OuterApplyContext a => new JoinTable(
                    table, BuildTablePrimary(a.tablePrimary()), JoinKind.OuterApply, null),
                _ => throw new SqlParseException($"Unsupported join: {join.GetText()}"),
            };
        }
        return table;
    }

    private static TableReference BuildTablePrimary(TablePrimaryContext ctx) => ctx switch
    {
        NamedTablePrimaryContext n => new NamedTable(Identifier(n.table), OptionalIdentifier(n.alias)),
        SubqueryPrimaryContext s => new SubqueryTable(BuildQueryExpression(s.queryExpression()), OptionalIdentifier(s.alias)),
        ParenJoinPrimaryContext p => BuildTableSource(p.tableSource()), // a parenthesized join group is just nested
        _ => throw new SqlParseException($"Unsupported table source: {ctx.GetText()}"),
    };

    private static JoinKind JoinKindOf(JoinTypeContext ctx) => ctx switch
    {
        LeftJoinContext => JoinKind.Left,
        RightJoinContext => JoinKind.Right,
        FullJoinContext => JoinKind.Full,
        _ => JoinKind.Inner,
    };

    private static OrderByItem BuildOrderByItem(OrderByItemContext ctx) =>
        new(BuildExpression(ctx.expression()),
            ctx.dir?.Type == DESC ? SortDirection.Descending : SortDirection.Ascending);

    internal static Expression BuildExpression(ExpressionContext ctx) => ctx switch
    {
        NotExprContext n => new UnaryExpression(
            n.op.Type == BNOT ? UnaryOperator.BitNot : UnaryOperator.Not, BuildExpression(n.expression())),
        // A unary plus leaves its operand as it is.
        NegateExprContext n => n.op.Type == PLUS
            ? BuildExpression(n.expression())
            : SignedNumber(n) is { } signed
                ? PowerChain(signed.Number, signed.Exponents)
                : new UnaryExpression(UnaryOperator.Negate, BuildExpression(n.expression())),
        PowExprContext p => BuildPower(p),
        MulDivExprContext m => Binary(m.op, m.left, m.right),
        IntDivExprContext d => Binary(d.op, d.left, d.right),
        ModExprContext m => Binary(m.op, m.left, m.right),
        AddSubExprContext a => Binary(a.op, a.left, a.right),
        ConcatExprContext c => Binary(c.op, c.left, c.right),
        ComparisonExprContext c => Binary(c.op, c.left, c.right),
        BetweenExprContext b => BuildBetween(b),
        InExprContext i => BuildIn(i),
        InSubqueryExprContext i => new InSubqueryExpression(
            BuildExpression(i.val), BuildQueryExpression(i.sub), i.not is not null),
        LikeExprContext l => l.not is null
            ? new BinaryExpression(BinaryOperator.Like, BuildExpression(l.left), BuildExpression(l.right))
            : new UnaryExpression(UnaryOperator.Not, new BinaryExpression(BinaryOperator.Like, BuildExpression(l.left), BuildExpression(l.right))),
        IsNullExprContext n => new UnaryExpression(n.not is null ? UnaryOperator.IsNull : UnaryOperator.IsNotNull, BuildExpression(n.operand)),
        AndExprContext a => Binary(a.op, a.left, a.right),
        OrExprContext o => Binary(o.op, o.left, o.right),
        XorExprContext x => Binary(x.op, x.left, x.right),
        EqvExprContext e => Binary(e.op, e.left, e.right),
        ImpExprContext i => Binary(i.op, i.left, i.right),
        PrimaryExprContext p => BuildPrimary(p.primary()),
        _ => throw new SqlParseException($"Unsupported expression: {ctx.GetText()}"),
    };

    private static Expression BuildPrimary(PrimaryContext ctx) => ctx switch
    {
        LiteralPrimaryContext l => BuildLiteral(l.literal()),
        ColumnPrimaryContext c => BuildColumn(c.columnRef()),
        ParamPrimaryContext p => new ParameterExpression(p.PARAM().GetText()),
        SystemVariablePrimaryContext s => new SystemVariableExpression(s.SYSVAR().GetText().TrimStart('@')),
        FunctionCallPrimaryContext f => BuildFunctionCall(f.functionCall()),
        ScalarSubqueryPrimaryContext s => new ScalarSubquery(BuildQueryExpression(s.queryExpression())),
        ExistsPrimaryContext e => new ExistsExpression(BuildQueryExpression(e.queryExpression())),
        CasePrimaryContext c => BuildCase(c.caseExpression()),
        ParenPrimaryContext p => BuildExpression(p.expression()),
        _ => throw new SqlParseException($"Unsupported primary: {ctx.GetText()}"),
    };

    /// <summary>A CASE expression. The simple form (<c>CASE operand WHEN value THEN …</c>) is folded into the
    /// searched form here by turning each arm into <c>operand = value</c>, so evaluation only ever sees one
    /// shape. The operand is re-emitted per arm, which is what the standard's own definition implies and
    /// matches how the Jet generator expands a CASE into IIFs.</summary>
    private static CaseExpression BuildCase(CaseExpressionContext ctx)
    {
        Expression? operand = ctx.operand is null ? null : BuildExpression(ctx.operand);

        var arms = ctx.caseWhen().Select(w =>
        {
            Expression condition = BuildExpression(w.condition);
            if (operand is not null)
                condition = new BinaryExpression(BinaryOperator.Equal, operand, condition);
            return new CaseWhen(condition, BuildExpression(w.result));
        }).ToList();

        return new CaseExpression(arms, ctx.elseResult is null ? null : BuildExpression(ctx.elseResult));
    }

    private static Expression BuildFunctionCall(FunctionCallContext ctx)
    {
        string name = FunctionName(ctx.name);
        List<Expression> args = ctx.star is not null
            ? [new StarExpression()]
            : ctx.expression().Select(BuildExpression).ToList();
        IReadOnlyList<SortDirection>? withinGroup = BuildWithinGroup(ctx, name, args);
        Expression? filter = ctx.filterClause() is { } f ? BuildExpression(f.condition) : null;

        // An OVER clause turns the same call into a window function, which is a different kind of node rather
        // than a FunctionCall carrying a spec — see WindowFunction for why the distinction has to be in the type.
        if (ctx.windowSpecification() is { } over)
        {
            return new WindowFunction(name, args, BuildWindowSpec(over),
                Distinct: ctx.distinct is not null,
                IgnoreNulls: ctx.nullTreatment() is { } nulls ? nulls.treatment.Type == IGNORE : null,
                FromLast: ctx.nthRowFrom() is { } from ? from.edge.Type == LAST : null,
                WithinGroup: withinGroup,
                Filter: filter);
        }
        return new FunctionCall(name, args, Distinct: ctx.distinct is not null, WithinGroup: withinGroup, Filter: filter);
    }

    /// <summary>
    /// An ordered-set aggregate's WITHIN GROUP: its ORDER BY keys, appended to <paramref name="args"/>, and their
    /// directions. The syntax belongs to those aggregates alone, and they cannot go without it. A percentile takes
    /// one argument, the fraction, no DISTINCT and one key; LISTAGG takes the value and optionally a separator, which
    /// the standard makes a string literal, and any number of keys.
    /// </summary>
    private static List<SortDirection>? BuildWithinGroup(FunctionCallContext ctx, string name, List<Expression> args)
    {
        bool stringAgg = name.Equals("STRING_AGG", StringComparison.OrdinalIgnoreCase);
        if (ctx.withinGroup() is not { } within)
        {
            // STRING_AGG's order is optional — without it the values list as the rows arrive — but its
            // separator is not, which is the other way round from LISTAGG.
            if (stringAgg)
            {
                ValidateStringAgg(ctx, args);
                return null;
            }
            return FunctionCall.IsOrderedSetAggregate(name)
                ? throw new SqlParseException($"{name} needs WITHIN GROUP (ORDER BY …).")
                : null;
        }
        if (!FunctionCall.AcceptsWithinGroup(name))
            throw new SqlParseException($"{name} takes no WITHIN GROUP.");
        if (ctx.star is not null)
            throw new SqlParseException($"{name} takes no *.");

        var keys = within.orderByClause().orderByItem().Select(BuildOrderByItem).ToList();
        if (stringAgg)
            ValidateStringAgg(ctx, args);
        else if (name.Equals("LISTAGG", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count is not (1 or 2) || args is [_, not LiteralExpression { Value: string }])
                throw new SqlParseException("LISTAGG takes a value and, optionally, a separator written as a string.");
        }
        else if (ctx.distinct is not null || args.Count != 1 || keys.Count != 1)
            throw new SqlParseException($"{name} takes one argument, the fraction, no DISTINCT, and orders by one key.");

        args.AddRange(keys.Select(k => k.Value));
        return keys.Select(k => k.Direction).ToList();
    }

    /// <summary>SQL Server's <c>STRING_AGG(expression, separator)</c>: both arguments, no <c>*</c>, and the
    /// separator written as a string, which is what makes it the one value the whole group shares.</summary>
    private static void ValidateStringAgg(FunctionCallContext ctx, List<Expression> args)
    {
        if (ctx.star is not null)
            throw new SqlParseException("STRING_AGG takes no *.");
        if (args.Count != 2 || args[1] is not LiteralExpression { Value: string })
            throw new SqlParseException(
                "STRING_AGG takes a value and a separator written as a string.");
    }

    private static WindowSpec BuildWindowSpec(WindowSpecificationContext ctx) =>
        new(ctx._partition.Select(BuildExpression).ToList(),
            ctx.orderByClause() is { } o ? o.orderByItem().Select(BuildOrderByItem).ToList() : [],
            ctx.windowFrame() is { } frame ? BuildWindowFrame(frame) : null);

    /// <summary>
    /// A frame clause, held to the standard's syntax rules: the start is not UNBOUNDED FOLLOWING, the end is not
    /// UNBOUNDED PRECEDING, and the start does not come after the end in window order — so <c>CURRENT ROW AND 1
    /// PRECEDING</c> is refused, and a lone <c>n FOLLOWING</c>, which ends at the current row, is too.
    /// </summary>
    private static WindowFrame BuildWindowFrame(WindowFrameContext ctx)
    {
        FrameBound start = BuildFrameBound(ctx.start);
        FrameBound end = ctx.end is null ? new FrameBound(FrameBoundKind.CurrentRow) : BuildFrameBound(ctx.end);
        if (start.Kind == FrameBoundKind.UnboundedFollowing)
            throw new SqlParseException("A window frame cannot start at UNBOUNDED FOLLOWING.");
        if (end.Kind == FrameBoundKind.UnboundedPreceding)
            throw new SqlParseException("A window frame cannot end at UNBOUNDED PRECEDING.");
        if (start.Kind > end.Kind)
            throw new SqlParseException("A window frame cannot start after it ends.");

        FrameUnit unit = ctx.unit.Type switch
        {
            ROWS => FrameUnit.Rows,
            RANGE => FrameUnit.Range,
            _ => FrameUnit.Groups,
        };
        FrameExclusion exclusion = ctx.exclusion switch
        {
            null => FrameExclusion.NoOthers,
            { } e when e.CURRENT() is not null => FrameExclusion.CurrentRow,
            { } e when e.GROUP() is not null => FrameExclusion.Group,
            { } e when e.TIES() is not null => FrameExclusion.Ties,
            _ => FrameExclusion.NoOthers, // NO OTHERS
        };
        return new WindowFrame(unit, start, end, exclusion);
    }

    private static FrameBound BuildFrameBound(FrameBoundContext ctx)
    {
        if (ctx.CURRENT() is not null)
            return new FrameBound(FrameBoundKind.CurrentRow);
        bool preceding = ctx.direction.Type == PRECEDING;
        return ctx.UNBOUNDED() is not null
            ? new FrameBound(preceding ? FrameBoundKind.UnboundedPreceding : FrameBoundKind.UnboundedFollowing)
            : new FrameBound(preceding ? FrameBoundKind.Preceding : FrameBoundKind.Following, BuildExpression(ctx.offset));
    }

    /// <summary>A function name: an identifier, or the LEFT/RIGHT keyword tokens as Left()/Right().</summary>
    private static string FunctionName(FunctionNameContext ctx) =>
        ctx.identifier() is { } id ? Identifier(id) : ctx.GetText();

    private static ColumnReference BuildColumn(ColumnRefContext ctx) =>
        new ColumnReference(OptionalIdentifier(ctx.qualifier), Identifier(ctx.name));

    /// <summary><c>x IN (a, b, …)</c> becomes a flat <see cref="InListExpression"/> evaluated iteratively — NOT a
    /// deep <c>(x = a) OR (x = b) OR …</c> tree, which recurses once per item and overflows the stack when EF Core
    /// inlines a "huge number of values" Contains (thousands of constants). The evaluator reproduces the same
    /// OR/=/NOT three-valued semantics in a loop.</summary>
    private static InListExpression BuildIn(InExprContext ctx)
    {
        Expression value = BuildExpression(ctx.val);
        var items = ctx._items.Select(BuildExpression).ToList();
        return new InListExpression(value, items, ctx.not is not null);
    }

    private static BetweenExpression BuildBetween(BetweenExprContext ctx) =>
        new BetweenExpression(BuildExpression(ctx.val), BuildExpression(ctx.lo), BuildExpression(ctx.hi), ctx.not is not null);

    /// <summary>Parses an Access <c>#…#</c> date literal (e.g. <c>#1/1/1997#</c>, month/day/year) to a
    /// <see cref="DateTime"/>. A time without a date is on 1899-12-30, day zero (verified vs ACE: #13:45:30# is
    /// 1899-12-30 13:45:30), not on today.</summary>
    private static DateTime ParseDate(string text)
    {
        DateTime value = DateTime.Parse(text.Trim('#'), CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault);
        return value.Date == DateTime.MinValue ? new DateTime(1899, 12, 30).Add(value.TimeOfDay) : value;
    }

    /// <summary>The exact value of a number written without an exponent; none for 1E5 or 1.5E2, or past a Decimal.</summary>
    private static decimal? WrittenDecimal(string text) =>
        text.IndexOfAny(['E', 'e']) < 0
        && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
            ? value : null;

    /// <summary>
    /// A minus written directly against a number that begins with a digit is part of that number, which is then a single
    /// operand binding tighter than <c>^</c> (verified vs ACE: <c>-2 ^ 2</c> is 4 and <c>2 ^ -2 ^ 2</c> is 0.0625, while
    /// <c>- 2 ^ 2</c>, <c>-(2) ^ 2</c> and <c>-.5 ^ 2</c> are -4, -4 and -0.25). The parser gives the minus the powers
    /// that follow, so this returns the signed number and the exponents to apply to it in turn, or null when the minus
    /// is an ordinary negation.
    /// </summary>
    private static (LiteralExpression Number, List<ExpressionContext> Exponents)? SignedNumber(NegateExprContext negation)
    {
        if (negation.op.Type != MINUS)
            return null;

        var exponents = new List<ExpressionContext>();
        ExpressionContext leftmost = negation.expression();
        while (leftmost is PowExprContext power)
        {
            exponents.Insert(0, power.right);
            leftmost = power.left;
        }
        if (leftmost is not PrimaryExprContext primary || primary.primary() is not LiteralPrimaryContext literalPrimary)
            return null;

        LiteralContext literal = literalPrimary.literal();
        string text = literal.GetText();
        if (literal is not (IntLiteralContext or NumberLiteralContext)
            || literal.Start.StartIndex != negation.op.StopIndex + 1
            || !char.IsDigit(text[0]))
            return null;

        return (BuildNumber("-" + text, literal is IntLiteralContext ? INTEGER_LITERAL : NUMBER_LITERAL), exponents);
    }

    private static Expression PowerChain(Expression @base, IEnumerable<ExpressionContext> exponents) =>
        exponents.Aggregate(@base, (result, exponent) =>
            new BinaryExpression(BinaryOperator.Power, result, BuildExpression(exponent)));

    private static Expression BuildPower(PowExprContext power) =>
        power.right is NegateExprContext negation && SignedNumber(negation) is { } signed
            ? PowerChain(new BinaryExpression(BinaryOperator.Power, BuildExpression(power.left), signed.Number), signed.Exponents)
            : new BinaryExpression(BinaryOperator.Power, BuildExpression(power.left), BuildExpression(power.right));

    /// <summary>A number literal, with the sign when a minus is written against it.</summary>
    private static LiteralExpression BuildNumber(string text, int tokenType) => tokenType == INTEGER_LITERAL
        ? new LiteralExpression(ParseInteger(text))
        : new LiteralExpression(double.Parse(text, CultureInfo.InvariantCulture), WrittenDecimal(text));

    private static LiteralExpression BuildLiteral(LiteralContext ctx) => ctx switch
    {
        IntLiteralContext i => BuildNumber(i.GetText(), INTEGER_LITERAL),
        NumberLiteralContext n => BuildNumber(n.GetText(), NUMBER_LITERAL),
        HexLiteralContext h => new LiteralExpression(ParseHexBytes(h.GetText())),
        StringLiteralContext s => new LiteralExpression(Unquote(s.GetText())),
        DateLiteralContext d => new LiteralExpression(ParseDate(d.GetText())),
        GuidLiteralContext g => new LiteralExpression(Guid.Parse(g.GetText())), // Access {…} braces; Guid.Parse accepts them
        TrueLiteralContext => new LiteralExpression(true),
        FalseLiteralContext => new LiteralExpression(false),
        NullLiteralContext => new LiteralExpression(null),
        _ => throw new SqlParseException($"Unsupported literal: {ctx.GetText()}"),
    };

    private static BinaryExpression Binary(Antlr4.Runtime.IToken op, ExpressionContext left, ExpressionContext right) =>
        new(MapOperator(op.Type), BuildExpression(left), BuildExpression(right));

    private static BinaryOperator MapOperator(int tokenType) => tokenType switch
    {
        EQ => BinaryOperator.Equal,
        NEQ => BinaryOperator.NotEqual,
        LT => BinaryOperator.LessThan,
        LTE => BinaryOperator.LessThanOrEqual,
        GT => BinaryOperator.GreaterThan,
        GTE => BinaryOperator.GreaterThanOrEqual,
        PLUS => BinaryOperator.Add,
        MINUS => BinaryOperator.Subtract,
        STAR => BinaryOperator.Multiply,
        SLASH => BinaryOperator.Divide,
        MOD => BinaryOperator.Modulo,
        BACKSLASH => BinaryOperator.IntDivide,
        AMP => BinaryOperator.Concat,
        AND => BinaryOperator.And,
        OR => BinaryOperator.Or,
        XOR => BinaryOperator.Xor,
        EQV => BinaryOperator.Eqv,
        IMP => BinaryOperator.Imp,
        BAND => BinaryOperator.BitAnd,
        BOR => BinaryOperator.BitOr,
        BXOR => BinaryOperator.BitXor,
        _ => throw new SqlParseException($"Unsupported operator token {tokenType}"),
    };

    private static object ParseInteger(string text) =>
        // Box each branch independently: a bare `? i : long.Parse(...)` would infer the
        // conditional's type as `long` and silently widen the int branch, so every literal
        // (even `1`) would arrive as a boxed long.
        int.TryParse(text, out int i) ? i : (object)long.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>A raw binary literal (<c>0x…</c>) → the decoded bytes. Access writes OLE / Long Binary values
    /// this way (e.g. a Categories.Picture bitmap). An odd digit count is a malformed literal and throws.</summary>
    private static byte[] ParseHexBytes(string text)
    {
        ReadOnlySpan<char> digits = text.AsSpan(2); // drop the "0x" prefix
        if (digits.Length % 2 != 0)
            throw new SqlParseException($"Binary literal '0x…' has an odd number of hex digits.");
        return Convert.FromHexString(digits);
    }

    private static string Identifier(IdentifierContext ctx)
    {
        string text = ctx.GetText();
        // Strip delimiters: [bracketed] or `backtick`.
        return text.Length >= 2 && ((text[0] == '[' && text[^1] == ']') || (text[0] == '`' && text[^1] == '`'))
            ? text[1..^1]
            : text;
    }

    private static string? OptionalIdentifier(IdentifierContext? ctx) => ctx is null ? null : Identifier(ctx);

    private static string Unquote(string text)
    {
        char quote = text[0]; // ' or "
        // Strip the surrounding quotes, then collapse each doubled quote to a single one (SQL escape).
        return text[1..^1].Replace(new string(quote, 2), quote.ToString());
    }
}
