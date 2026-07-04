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
        if (ctx.createTableStatement() is { } create) return BuildCreateTable(create);
        if (ctx.createIndexStatement() is { } createIndex) return BuildCreateIndex(createIndex);
        if (ctx.createViewStatement() is { } createView) return BuildCreateView(createView);
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

    /// <summary>Decomposes a view's "simple SELECT" into the columns/tables/joins/where Access stores as
    /// MSysQueries rows. Rejects anything Access itself rejects in a view (UNION, GROUP BY/aggregates,
    /// HAVING, ORDER BY) or that we can't decompose (a derived-table/subquery source).</summary>
    private static ViewDefinition BuildViewDefinition(QueryExpressionContext ctx)
    {
        if (ctx.setOperator().Length > 0)
            throw new NotSupportedException("A UNION query is not a valid (simple) view.");

        SelectStatementContext select = ctx.selectStatement(0);
        if (select.groupByClause() is not null || select.havingClause() is not null || select.orderByClause() is not null)
            throw new NotSupportedException("A view SELECT cannot use GROUP BY, HAVING or ORDER BY (only a simple SELECT).");

        // Output columns (verbatim text); SELECT * becomes a single "*", a qualified star stays "Table.*".
        var columns = select.selectList().STAR() is not null && select.selectList().selectItem().Length == 0
            ? (IReadOnlyList<string>)["*"]
            : select.selectList().selectItem().Select(ColumnText).ToList();

        var tables = new List<ViewSource>();
        var joins = new List<ViewJoin>();
        foreach (TableSourceContext ts in select.fromClause().tableSource())
        {
            (ViewSource left, string leftAlias) = BuildViewSource(ts.tablePrimary());
            tables.Add(left);

            foreach (JoinClauseContext jc in ts.joinClause())
            {
                (ViewSource right, string rightAlias) = BuildViewSource(jc.tablePrimary());
                tables.Add(right);
                joins.Add(new ViewJoin(ViewJoinKindOf(jc.joinType()), OriginalText(jc.expression()), leftAlias, rightAlias));
                leftAlias = rightAlias;
            }
        }

        string? where = select.whereClause() is { } w ? OriginalText(w.expression()) : null;
        return new ViewDefinition(Distinct: false, columns, tables, joins, where);
    }

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
        SelectStatementContext[] selects = ctx.selectStatement();
        SetOperatorContext[] operators = ctx.setOperator();
        SqlStatement result = BuildSelect(selects[0]);
        for (int i = 0; i < operators.Length; i++)
            result = new SetOperationStatement(result, SetOperatorOf(operators[i]), BuildSelect(selects[i + 1]));
        return result;
    }

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
        int? top = ctx.topClause() is { } t ? int.Parse(t.INTEGER_LITERAL().GetText(), CultureInfo.InvariantCulture) : null;

        return new SelectStatement(projection, star, from, where, groupBy, having, orderBy, top);
    }

    /// <summary>The verbatim text a view stores for a projection item — the expression (alias dropped),
    /// or <c>Table.*</c> for a qualified star.</summary>
    private static string ColumnText(SelectItemContext ctx) => ctx switch
    {
        ExpressionSelectItemContext e => OriginalText(e.expression()),
        _ => OriginalText(ctx), // qualified star: "Table.*"
    };

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
        LikeExprContext l => new BinaryExpression(BinaryOperator.Like, BuildExpression(l.left), BuildExpression(l.right)),
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
        return new FunctionCall(Identifier(ctx.name), args);
    }

    private static Expression BuildColumn(ColumnRefContext ctx) =>
        new ColumnReference(OptionalIdentifier(ctx.qualifier), Identifier(ctx.name));

    private static Expression BuildLiteral(LiteralContext ctx) => ctx switch
    {
        IntLiteralContext i => new LiteralExpression(ParseInteger(i.GetText())),
        NumberLiteralContext n => new LiteralExpression(double.Parse(n.GetText(), CultureInfo.InvariantCulture)),
        StringLiteralContext s => new LiteralExpression(Unquote(s.GetText())),
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

    private static string Identifier(IdentifierContext ctx)
    {
        string text = ctx.GetText();
        // Strip delimiters: [bracketed] or `backtick`.
        return text.Length >= 2 && ((text[0] == '[' && text[^1] == ']') || (text[0] == '`' && text[^1] == '`'))
            ? text[1..^1]
            : text;
    }

    private static string? OptionalIdentifier(IdentifierContext? ctx) => ctx is null ? null : Identifier(ctx);

    private static string Unquote(string text) => text[1..^1]; // strip the surrounding ' or "
}
