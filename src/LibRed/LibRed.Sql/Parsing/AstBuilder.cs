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
    public SqlStatement Build(StatementContext ctx) => BuildQueryExpression(ctx.queryExpression());

    private static SqlStatement BuildQueryExpression(QueryExpressionContext ctx)
    {
        SelectStatementContext[] selects = ctx.selectStatement();
        SqlStatement result = BuildSelect(selects[0]);
        // ANTLR exposes one UNION token per set operator, each optionally followed by ALL.
        var unions = ctx.UNION();
        var alls = ctx.ALL();
        for (int i = 0; i < unions.Length; i++)
        {
            // ALL belongs to this operator if its token sits between this UNION and the next select.
            bool all = alls.Any(a => a.Symbol.TokenIndex > unions[i].Symbol.TokenIndex
                && a.Symbol.TokenIndex < selects[i + 1].Start.TokenIndex);
            result = new SetOperationStatement(result, all ? SetOperator.UnionAll : SetOperator.Union, BuildSelect(selects[i + 1]));
        }
        return result;
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
        var orderBy = ctx.orderByClause() is { } o
            ? o.orderByItem().Select(BuildOrderByItem).ToList()
            : (IReadOnlyList<OrderByItem>)[];
        int? top = ctx.topClause() is { } t ? int.Parse(t.INTEGER_LITERAL().GetText(), CultureInfo.InvariantCulture) : null;

        return new SelectStatement(projection, star, from, where, groupBy, null, orderBy, top);
    }

    private static SelectItem BuildSelectItem(SelectItemContext ctx) =>
        new(BuildExpression(ctx.expression()), OptionalIdentifier(ctx.alias));

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
        SubqueryPrimaryContext s => new SubqueryTable(BuildSelect(s.selectStatement()), OptionalIdentifier(s.alias)),
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

    private static Expression BuildExpression(ExpressionContext ctx) => ctx switch
    {
        NotExprContext n => new UnaryExpression(UnaryOperator.Not, BuildExpression(n.expression())),
        NegateExprContext n => new UnaryExpression(UnaryOperator.Negate, BuildExpression(n.expression())),
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
