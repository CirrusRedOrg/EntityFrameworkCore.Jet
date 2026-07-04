using System.Globalization;
using LibRed.Sql.Ast;
using static LibRed.Sql.Grammar.AccessSqlParser;

namespace LibRed.Sql.Parsing;

/// <summary>
/// Lowers an ANTLR parse tree into the engine's <see cref="SqlNode"/> AST, so nothing
/// downstream depends on the generated grammar types.
/// </summary>
internal sealed class AstBuilder
{
    public SqlStatement Build(StatementContext ctx)
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

    private SqlStatement BuildBody(StatementContext ctx)
    {
        if (ctx.createTableStatement() is { } create) return BuildCreateTable(create);
        if (ctx.createIndexStatement() is { } createIndex) return BuildCreateIndex(createIndex);
        if (ctx.createViewStatement() is { } createView) return BuildCreateView(createView);
        if (ctx.createProcedureStatement() is { } createProc) return BuildCreateProcedure(createProc);
        if (ctx.alterTableStatement() is { } alter) return BuildAlterTable(alter);
        if (ctx.insertStatement() is { } insert) return BuildInsert(insert);
        return BuildQueryExpression(ctx.queryExpression());
    }

    private static SqlStatement BuildCreateTable(CreateTableStatementContext ctx)
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

        // Column-level UNIQUE and REFERENCES (the single-field forms) apply to the column they follow.
        foreach (ColumnDefinitionContext cd in ctx.columnDefinition())
        {
            string columnName = Identifier(cd.name);
            foreach (ColumnConstraintContext cc in cd.columnConstraint())
            {
                switch (cc)
                {
                    case UniqueColumnConstraintContext u:
                        uniques.Add(new UniqueConstraint(u.cname is null ? null : Identifier(u.cname), [columnName]));
                        break;
                    case ColumnReferencesConstraintContext r:
                        foreignKeys.Add(BuildColumnReferences(r, columnName));
                        break;
                }
            }
        }

        foreach (TableConstraintContext tc in ctx.tableConstraint())
        {
            switch (tc)
            {
                case PrimaryKeyTableConstraintContext pk:
                    primaryKey.AddRange(pk._columns.Select(Identifier));
                    break;
                case UniqueTableConstraintContext uq:
                    uniques.Add(new UniqueConstraint(uq.name is null ? null : Identifier(uq.name), uq._columns.Select(Identifier).ToList()));
                    break;
                case ForeignKeyTableConstraintContext fk:
                    foreignKeys.Add(BuildForeignKey(fk));
                    break;
                case CheckTableConstraintContext ck:
                    checks.Add(new CheckConstraint(
                        ck.name is null ? null : Identifier(ck.name), OriginalText(ck.checkBody())));
                    break;
            }
        }

        return new CreateTableStatement(Identifier(ctx.table), columns, primaryKey, foreignKeys, uniques, checks);
    }

    /// <summary>The verbatim source text of a parse context (preserving spacing), via the input stream —
    /// unlike <c>GetText()</c>, which concatenates token text with no whitespace. Used to store a CHECK
    /// expression exactly as written (matching Access).</summary>
    private static string OriginalText(Antlr4.Runtime.ParserRuleContext ctx) =>
        ctx.Start is null || ctx.Stop is null
            ? ctx.GetText()
            : ctx.Start.InputStream.GetText(Antlr4.Runtime.Misc.Interval.Of(ctx.Start.StartIndex, ctx.Stop.StopIndex));

    private static SqlStatement BuildAlterTable(AlterTableStatementContext ctx)
    {
        AlterTableAction action = ctx.alterTableAction() switch
        {
            AddColumnActionContext a => new AddColumnAction(BuildColumnDefinition(a.columnDefinition())),
            AddConstraintActionContext a => BuildAddConstraint(a.tableConstraint()),
            AlterColumnActionContext a => new AlterColumnAction(
                Identifier(a.field), TypeName(a.dataType()), Size(a.dataType()), Scale(a.dataType())),
            DropColumnActionContext a => new DropColumnAction(Identifier(a.field)),
            DropConstraintActionContext a => new DropConstraintAction(Identifier(a.cname)),
            _ => throw new SqlParseException("Unsupported ALTER TABLE action."),
        };
        return new AlterTableStatement(Identifier(ctx.table), action);
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

    private static int? Size(DataTypeContext type) => type.size is { } s ? int.Parse(s.Text, CultureInfo.InvariantCulture) : null;
    private static int? Scale(DataTypeContext type) => type.scale is { } s ? int.Parse(s.Text, CultureInfo.InvariantCulture) : null;

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
        int? size = type.size is { } s ? int.Parse(s.Text, CultureInfo.InvariantCulture) : null;
        int? scale = type.scale is { } sc ? int.Parse(sc.Text, CultureInfo.InvariantCulture) : null;

        // Two-word type names (e.g. CHARACTER VARYING) join with a single space.
        string typeName = type.extra is null
            ? Identifier(type.typeName)
            : $"{Identifier(type.typeName)} {Identifier(type.extra)}";

        if (ctx.columnConstraint().OfType<CompressionConstraintContext>().Any())
            throw new NotSupportedException($"WITH COMPRESSION on column '{Identifier(ctx.name)}' is not supported.");

        bool notNull = ctx.columnConstraint().OfType<NotNullConstraintContext>().Any();
        bool primaryKey = ctx.columnConstraint().OfType<PrimaryKeyConstraintContext>().Any();
        // Capture the DEFAULT expression's source text (stored verbatim as the DefaultValue property,
        // matching Access — e.g. "42", "'hi'"). Re-parsed and evaluated when a column is omitted on insert.
        string? defaultSql = ctx.columnConstraint().OfType<DefaultConstraintContext>()
            .FirstOrDefault()?.expression().GetText();

        return new ColumnDefinition(Identifier(ctx.name), typeName, size, scale, notNull, primaryKey, defaultSql);
    }

    private static SqlStatement BuildCreateIndex(CreateIndexStatementContext ctx)
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

    private static SqlStatement BuildCreateView(CreateViewStatementContext ctx)
    {
        var columns = ctx._columns.Select(Identifier).ToList();
        ViewDefinition definition = BuildViewDefinition(ctx.query);
        return new CreateViewStatement(Identifier(ctx.name), columns, definition, OriginalText(ctx.query));
    }

    private static SqlStatement BuildCreateProcedure(CreateProcedureStatementContext ctx)
    {
        var parameters = (ctx.procParamList()?.procParam() ?? [])
            .Select(p => new ProcedureParameter(ParamName(p), TypeName(p.dataType())))
            .ToList();

        // A procedure body is a SELECT (stored as a parameterized query, like a view) or an action query
        // (CREATE TABLE / INSERT — stored byte-faithfully in their own MSysQueries shape).
        ProcedureBodyContext body = ctx.body;
        string name = Identifier(ctx.name);

        if (body.createTableStatement() is { } ddl)
        {
            RejectParametersOnAction(parameters);
            return new CreateActionProcedureStatement(
                name, ProcedureActionKind.DataDefinition, OriginalText(ddl), null, null);
        }
        if (body.insertStatement() is { } insert)
        {
            RejectParametersOnAction(parameters);
            return BuildAppendProcedure(name, insert);
        }

        ViewDefinition definition = BuildViewDefinition(body.queryExpression());
        return new CreateProcedureStatement(name, parameters, definition, OriginalText(body.queryExpression()));
    }

    private static void RejectParametersOnAction(IReadOnlyList<ProcedureParameter> parameters)
    {
        if (parameters.Count > 0)
            throw new NotSupportedException("Parameters on an action-query procedure are not stored yet.");
    }

    private static SqlStatement BuildAppendProcedure(string name, InsertStatementContext insert)
    {
        var columns = insert._columns;
        var values = insert.expression();
        if (columns.Count == 0)
            throw new NotSupportedException("An INSERT procedure body must list its target columns.");
        if (columns.Count != values.Length)
            throw new SqlParseException(
                $"INSERT lists {columns.Count} columns but {values.Length} values.");

        var appendColumns = columns
            .Select((col, i) => new AppendColumn(Identifier(col), OriginalText(values[i])))
            .ToList();
        return new CreateActionProcedureStatement(
            name, ProcedureActionKind.Append, null, Identifier(insert.table), appendColumns);
    }

    /// <summary>A declared parameter's name, with any leading <c>@</c> stripped — Access stores the bare
    /// name (e.g. <c>@Beginning_Date</c> is stored as <c>Beginning_Date</c>).</summary>
    private static string ParamName(ProcParamContext p) => p.pname.PARAM() is { } at
        ? at.GetText().TrimStart('@')
        : Identifier(p.pname.identifier());

    /// <summary>The declared type name of a data type (two-word names joined by a space).</summary>
    private static string TypeName(DataTypeContext type) => type.extra is null
        ? Identifier(type.typeName)
        : $"{Identifier(type.typeName)} {Identifier(type.extra)}";

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

    private static TableReference LowerFrom(TableReference t, HashSet<string> names) => t switch
    {
        JoinTable j => j with
        {
            Left = LowerFrom(j.Left, names),
            Right = LowerFrom(j.Right, names),
            On = j.On is null ? null : LowerExpr(j.On, names),
        },
        SubqueryTable sub => sub with { Query = LowerParameters(sub.Query, names) },
        _ => t,
    };

    private static Expression LowerExpr(Expression e, HashSet<string> names) => e switch
    {
        ColumnReference { Table: null, Column: var c } when names.Contains(c) => new ParameterExpression(c),
        BinaryExpression b => b with { Left = LowerExpr(b.Left, names), Right = LowerExpr(b.Right, names) },
        UnaryExpression u => u with { Operand = LowerExpr(u.Operand, names) },
        FunctionCall f => f with { Arguments = f.Arguments.Select(a => LowerExpr(a, names)).ToList() },
        ScalarSubquery s => new ScalarSubquery(LowerSelect(s.Query, names)),
        ExistsExpression x => new ExistsExpression(LowerSelect(x.Query, names)),
        InSubqueryExpression i => i with { Value = LowerExpr(i.Value, names), Query = LowerSelect(i.Query, names) },
        _ => e,
    };

    /// <summary>Decomposes a view's "simple SELECT" into the columns/tables/joins/where Access stores as
    /// MSysQueries rows. Rejects anything Access itself rejects in a view (UNION, GROUP BY/aggregates,
    /// HAVING, ORDER BY) or that we can't decompose (a derived-table/subquery source).</summary>
    private static ViewDefinition BuildViewDefinition(QueryExpressionContext ctx)
    {
        if (ctx.setOperator().Length > 0)
            throw new NotSupportedException("A UNION query is not a valid (simple) view.");
        if (ctx.queryTerm(0) is not SelectTermContext term)
            throw new NotSupportedException("A parenthesised query is not a valid (simple) view.");

        SelectStatementContext select = term.selectStatement();
        if (select.havingClause() is not null)
            throw new NotSupportedException("A view with HAVING is not stored yet.");
        var groupBy = select.groupByClause() is { } g
            ? g.expression().Select(OriginalText).ToList() : (IReadOnlyList<string>)[];
        var orderBy = select.orderByClause() is { } ob
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
        // join groups (Access stores them flat — one Attribute=5 per table, one Attribute=7 per join).
        var tables = new List<ViewSource>();
        var joins = new List<ViewJoin>();
        foreach (TableSourceContext ts in select.fromClause().tableSource())
            CollectSources(ts, tables, joins);

        string? where = select.whereClause() is { } w ? OriginalText(w.expression()) : null;
        return new ViewDefinition(select.distinct != null, columns, tables, joins, where, groupBy, orderBy, top);
    }

    private static void CollectSources(TableSourceContext ts, List<ViewSource> tables, List<ViewJoin> joins)
    {
        CollectPrimary(ts.tablePrimary(), tables, joins);
        foreach (JoinClauseContext jc in ts.joinClause())
        {
            CollectPrimary(jc.tablePrimary(), tables, joins);
            // Access records the join by the two tables named in its condition (Name1/Name2), not the
            // structural left/right (which for a nested group is a whole subtree).
            (string left, string right) = JoinSides(jc.expression());
            joins.Add(new ViewJoin(ViewJoinKindOf(jc.joinType()), OriginalText(jc.expression()), left, right));
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
            switch (e)
            {
                case ColumnReference { Table: { } q } when !qualifiers.Contains(q): qualifiers.Add(q); break;
                case BinaryExpression b: Walk(b.Left); Walk(b.Right); break;
                case UnaryExpression u: Walk(u.Operand); break;
                case FunctionCall f: foreach (Expression a in f.Arguments) Walk(a); break;
            }
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
        _ => ViewJoinKind.Inner,
    };

    private static SqlStatement BuildInsert(InsertStatementContext ctx)
    {
        var columns = ctx._columns.Select(Identifier).ToList();
        var values = ctx.expression().Select(BuildExpression).ToList();
        return new InsertStatement(Identifier(ctx.table), columns, [values]);
    }

    private static SqlStatement BuildQueryExpression(QueryExpressionContext ctx)
    {
        QueryTermContext[] terms = ctx.queryTerm();
        SetOperatorContext[] operators = ctx.setOperator();
        SqlStatement result = BuildQueryTerm(terms[0]);
        for (int i = 0; i < operators.Length; i++)
            result = new SetOperationStatement(result, SetOperatorOf(operators[i]), BuildQueryTerm(terms[i + 1]));
        return result;
    }

    /// <summary>A set-operation operand: a SELECT, or a parenthesised (possibly nested) query expression.</summary>
    private static SqlStatement BuildQueryTerm(QueryTermContext ctx) => ctx switch
    {
        SelectTermContext s => BuildSelect(s.selectStatement()),
        ParenTermContext p => BuildQueryExpression(p.queryExpression()),
        _ => throw new SqlParseException($"Unsupported query term: {ctx.GetText()}"),
    };

    private static SetOperator SetOperatorOf(SetOperatorContext ctx)
    {
        if (ctx.INTERSECT() != null) return SetOperator.Intersect;
        if (ctx.EXCEPT() != null) return SetOperator.Except;
        return ctx.ALL() != null ? SetOperator.UnionAll : SetOperator.Union;
    }

    private static SelectStatement BuildSelect(SelectStatementContext ctx)
    {
        SelectListContext list = ctx.selectList();
        bool star = list.STAR() != null;
        var projection = star
            ? (IReadOnlyList<SelectItem>)[]
            : list.selectItem().Select(BuildSelectItem).ToList();

        TableReference from = BuildFrom(ctx.fromClause());
        Expression? where = ctx.whereClause() is { } w ? BuildExpression(w.expression()) : null;
        var groupBy = ctx.groupByClause() is { } g
            ? g.expression().Select(BuildExpression).ToList()
            : (IReadOnlyList<Expression>)[];
        Expression? having = ctx.havingClause() is { } h ? BuildExpression(h.expression()) : null;
        var orderBy = ctx.orderByClause() is { } o
            ? o.orderByItem().Select(BuildOrderByItem).ToList()
            : (IReadOnlyList<OrderByItem>)[];
        Expression? top = ctx.topClause() is { } t ? BuildTop(t) : null;

        return new SelectStatement(projection, star, from, where, groupBy, having, orderBy, top, ctx.distinct != null);
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

    private static TableReference BuildFrom(FromClauseContext ctx)
    {
        TableReference table = BuildTableSource(ctx.tableSource(0));
        // Comma between sources is an implicit cross join (no ON).
        foreach (TableSourceContext src in ctx.tableSource().Skip(1))
            table = new JoinTable(table, BuildTableSource(src), JoinKind.Cross, null);
        return table;
    }

    private static TableReference BuildTableSource(TableSourceContext ctx)
    {
        TableReference table = BuildTablePrimary(ctx.tablePrimary());
        foreach (JoinClauseContext join in ctx.joinClause())
        {
            TableReference right = BuildTablePrimary(join.tablePrimary());
            table = new JoinTable(table, right, JoinKindOf(join.joinType()), BuildExpression(join.expression()));
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
        _ => JoinKind.Inner,
    };

    private static OrderByItem BuildOrderByItem(OrderByItemContext ctx) =>
        new(BuildExpression(ctx.expression()),
            ctx.dir?.Type == DESC ? SortDirection.Descending : SortDirection.Ascending);

    internal static Expression BuildExpression(ExpressionContext ctx) => ctx switch
    {
        NotExprContext n => new UnaryExpression(UnaryOperator.Not, BuildExpression(n.expression())),
        NegateExprContext n => new UnaryExpression(UnaryOperator.Negate, BuildExpression(n.expression())),
        PowExprContext p => new BinaryExpression(BinaryOperator.Power, BuildExpression(p.left), BuildExpression(p.right)),
        MulDivExprContext m => Binary(m.op, m.left, m.right),
        AddConcatExprContext a => Binary(a.op, a.left, a.right),
        ComparisonExprContext c => Binary(c.op, c.left, c.right),
        BetweenExprContext b => BuildBetween(b),
        InExprContext i => BuildIn(i),
        InSubqueryExprContext i => new InSubqueryExpression(
            BuildExpression(i.val), BuildSelect(i.sub), i.not is not null),
        LikeExprContext l => l.not is null
            ? new BinaryExpression(BinaryOperator.Like, BuildExpression(l.left), BuildExpression(l.right))
            : new UnaryExpression(UnaryOperator.Not, new BinaryExpression(BinaryOperator.Like, BuildExpression(l.left), BuildExpression(l.right))),
        IsNullExprContext n => new UnaryExpression(n.not is null ? UnaryOperator.IsNull : UnaryOperator.IsNotNull, BuildExpression(n.operand)),
        AndExprContext a => new BinaryExpression(BinaryOperator.And, BuildExpression(a.left), BuildExpression(a.right)),
        OrExprContext o => new BinaryExpression(BinaryOperator.Or, BuildExpression(o.left), BuildExpression(o.right)),
        PrimaryExprContext p => BuildPrimary(p.primary()),
        _ => throw new SqlParseException($"Unsupported expression: {ctx.GetText()}"),
    };

    private static Expression BuildPrimary(PrimaryContext ctx) => ctx switch
    {
        LiteralPrimaryContext l => BuildLiteral(l.literal()),
        ColumnPrimaryContext c => BuildColumn(c.columnRef()),
        ParamPrimaryContext p => new ParameterExpression(p.PARAM().GetText()),
        FunctionCallPrimaryContext f => BuildFunctionCall(f.functionCall()),
        ScalarSubqueryPrimaryContext s => new ScalarSubquery(BuildSelect(s.selectStatement())),
        ExistsPrimaryContext e => new ExistsExpression(BuildSelect(e.selectStatement())),
        ParenPrimaryContext p => BuildExpression(p.expression()),
        _ => throw new SqlParseException($"Unsupported primary: {ctx.GetText()}"),
    };

    private static Expression BuildFunctionCall(FunctionCallContext ctx)
    {
        IReadOnlyList<Expression> args = ctx.star is not null
            ? [new StarExpression()]
            : ctx.expression().Select(BuildExpression).ToList();
        return new FunctionCall(FunctionName(ctx.name), args);
    }

    /// <summary>A function name: an identifier, or the LEFT/RIGHT keyword tokens as Left()/Right().</summary>
    private static string FunctionName(FunctionNameContext ctx) =>
        ctx.identifier() is { } id ? Identifier(id) : ctx.GetText();

    private static Expression BuildColumn(ColumnRefContext ctx) =>
        new ColumnReference(OptionalIdentifier(ctx.qualifier), Identifier(ctx.name));

    /// <summary>Lowers <c>x [NOT] BETWEEN lo AND hi</c> to <c>(x &gt;= lo AND x &lt;= hi)</c> (negated for NOT),
    /// so no dedicated node is needed and the evaluator handles it via the comparison operators.</summary>
    /// <summary><c>x IN (a, b, …)</c> lowers to <c>(x = a) OR (x = b) OR …</c> (and NOT IN wraps it in NOT),
    /// so null/three-valued semantics fall out of the existing OR/=/NOT evaluation. The items are ordinary
    /// expressions — literals or parameters in practice.</summary>
    private static Expression BuildIn(InExprContext ctx)
    {
        Expression value = BuildExpression(ctx.val);
        Expression membership = ctx._items
            .Select(item => (Expression)new BinaryExpression(BinaryOperator.Equal, value, BuildExpression(item)))
            .Aggregate((left, right) => new BinaryExpression(BinaryOperator.Or, left, right));
        return ctx.not is null ? membership : new UnaryExpression(UnaryOperator.Not, membership);
    }

    private static Expression BuildBetween(BetweenExprContext ctx)
    {
        Expression value = BuildExpression(ctx.val), lo = BuildExpression(ctx.lo), hi = BuildExpression(ctx.hi);
        Expression range = new BinaryExpression(BinaryOperator.And,
            new BinaryExpression(BinaryOperator.GreaterThanOrEqual, value, lo),
            new BinaryExpression(BinaryOperator.LessThanOrEqual, value, hi));
        return ctx.not is null ? range : new UnaryExpression(UnaryOperator.Not, range);
    }

    /// <summary>Parses an Access <c>#…#</c> date literal (e.g. <c>#1/1/1997#</c>, month/day/year) to a
    /// <see cref="DateTime"/>.</summary>
    private static DateTime ParseDate(string text) =>
        DateTime.Parse(text.Trim('#'), CultureInfo.InvariantCulture);

    private static Expression BuildLiteral(LiteralContext ctx) => ctx switch
    {
        IntLiteralContext i => new LiteralExpression(ParseInteger(i.GetText())),
        NumberLiteralContext n => new LiteralExpression(double.Parse(n.GetText(), CultureInfo.InvariantCulture)),
        HexLiteralContext h => new LiteralExpression(ParseHexBytes(h.GetText())),
        StringLiteralContext s => new LiteralExpression(Unquote(s.GetText())),
        DateLiteralContext d => new LiteralExpression(ParseDate(d.GetText())),
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
