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
    public SqlStatement Build(StatementContext ctx) => BuildSelect(ctx.selectStatement());

    private static SelectStatement BuildSelect(SelectStatementContext ctx)
    {
        SelectListContext list = ctx.selectList();
        bool star = list.STAR() != null;
        var projection = star
            ? (IReadOnlyList<SelectItem>)[]
            : list.selectItem().Select(BuildSelectItem).ToList();

        var from = new NamedTable(Identifier(ctx.tableSource().table), OptionalIdentifier(ctx.tableSource().alias));
        Expression? where = ctx.whereClause() is { } w ? BuildExpression(w.expression()) : null;
        int? top = ctx.topClause() is { } t ? int.Parse(t.INTEGER_LITERAL().GetText(), CultureInfo.InvariantCulture) : null;

        return new SelectStatement(projection, star, from, where, [], null, [], top);
    }

    private static SelectItem BuildSelectItem(SelectItemContext ctx) =>
        new(BuildExpression(ctx.expression()), OptionalIdentifier(ctx.alias));

    private static Expression BuildExpression(ExpressionContext ctx) => ctx switch
    {
        NotExprContext n => new UnaryExpression(UnaryOperator.Not, BuildExpression(n.expression())),
        MulDivExprContext m => Binary(m.op, m.left, m.right),
        AddConcatExprContext a => Binary(a.op, a.left, a.right),
        ComparisonExprContext c => Binary(c.op, c.left, c.right),
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
        ParenPrimaryContext p => BuildExpression(p.expression()),
        _ => throw new SqlParseException($"Unsupported primary: {ctx.GetText()}"),
    };

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
        AMP => BinaryOperator.Concat,
        _ => throw new SqlParseException($"Unsupported operator token {tokenType}"),
    };

    private static object ParseInteger(string text) =>
        int.TryParse(text, out int i) ? i : long.Parse(text, CultureInfo.InvariantCulture);

    private static string Identifier(IdentifierContext ctx)
    {
        string text = ctx.GetText();
        return text.Length >= 2 && text[0] == '[' && text[^1] == ']' ? text[1..^1] : text;
    }

    private static string? OptionalIdentifier(IdentifierContext? ctx) => ctx is null ? null : Identifier(ctx);

    private static string Unquote(string text) => text[1..^1]; // strip the surrounding ' or "
}
