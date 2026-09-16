namespace LibRed.Sql.Ast;

/// <summary>
/// The expressions an expression is built from, so that a walker names only the nodes it treats specially and a new
/// kind of node is taught to every walker here, once.
/// </summary>
public static class ExpressionTree
{
    /// <summary>
    /// The expressions directly inside <paramref name="expression"/> and evaluated in its scope: operands, function
    /// arguments, IN-list items, BETWEEN bounds, and a CASE's conditions and results. Null for anything else — a leaf,
    /// a subquery, an IN subquery or a window function — which each walker treats in its own way.
    /// </summary>
    public static IEnumerable<Expression>? Operands(this Expression expression) => expression switch
    {
        BinaryExpression b => new[] { b.Left, b.Right },
        UnaryExpression u => new[] { u.Operand },
        FunctionCall f => f.Arguments,
        InListExpression il => il.Items.Prepend(il.Value),
        BetweenExpression be => new[] { be.Value, be.Low, be.High },
        CaseExpression c => c.WhenClauses
            .SelectMany(w => new[] { w.Condition, w.Result })
            .Concat(c.ElseResult is { } otherwise ? new[] { otherwise } : []),
        _ => null,
    };

    /// <summary><paramref name="expression"/> with <paramref name="map"/> applied to each of its
    /// <see cref="Operands"/>, in order; the expression itself when it has none.</summary>
    public static Expression MapOperands(this Expression expression, Func<Expression, Expression> map) => expression switch
    {
        BinaryExpression b => b with { Left = map(b.Left), Right = map(b.Right) },
        UnaryExpression u => u with { Operand = map(u.Operand) },
        FunctionCall f => f with { Arguments = f.Arguments.Select(map).ToList() },
        InListExpression il => il with { Value = map(il.Value), Items = il.Items.Select(map).ToList() },
        BetweenExpression be => be with { Value = map(be.Value), Low = map(be.Low), High = map(be.High) },
        CaseExpression c => c with
        {
            WhenClauses = c.WhenClauses.Select(w => w with { Condition = map(w.Condition), Result = map(w.Result) }).ToList(),
            ElseResult = c.ElseResult is null ? null : map(c.ElseResult),
        },
        _ => expression,
    };
}
