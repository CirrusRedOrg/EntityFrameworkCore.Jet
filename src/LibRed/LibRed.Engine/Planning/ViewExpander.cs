using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;

namespace LibRed.Engine.Planning;

/// <summary>
/// Rewrites a query so that any reference to a view becomes a derived table (a subquery over the view's
/// stored SELECT) — the same shape the planner already handles for explicit subqueries. A view named in
/// a FROM clause, or inside a scalar/EXISTS subquery in any clause (projection, WHERE, GROUP BY, HAVING,
/// ORDER BY), is expanded. View SQL is reconstructed by the catalog (<c>JetCatalog.Views</c>) and parsed
/// here. Views nested inside other views are expanded recursively.
/// </summary>
internal static class ViewExpander
{
    public static SqlStatement Expand(SqlStatement statement, IReadOnlyDictionary<string, string> views, ISqlParser parser) =>
        views.Count == 0 ? statement : Rewrite(statement, views, parser);

    private static SqlStatement Rewrite(SqlStatement statement, IReadOnlyDictionary<string, string> views, ISqlParser parser) => statement switch
    {
        SelectStatement s => RewriteSelect(s, views, parser),
        SetOperationStatement so => so with
        {
            Left = Rewrite(so.Left, views, parser),
            Right = Rewrite(so.Right, views, parser),
        },
        _ => statement,
    };

    private static SelectStatement RewriteSelect(SelectStatement select, IReadOnlyDictionary<string, string> views, ISqlParser parser)
    {
        Expression Expr(Expression e) => RewriteExpression(e, views, parser);
        return select with
        {
            From = RewriteSource(select.From, views, parser),
            Projection = select.Projection.Select(i => i with { Value = Expr(i.Value) }).ToList(),
            Where = select.Where is { } w ? Expr(w) : null,
            GroupBy = select.GroupBy.Select(Expr).ToList(),
            Having = select.Having is { } h ? Expr(h) : null,
            OrderBy = select.OrderBy.Select(o => o with { Value = Expr(o.Value) }).ToList(),
        };
    }

    /// <summary>Rewrites views referenced inside expression subqueries (scalar / EXISTS), recursing through
    /// the operator/function tree; leaf expressions are returned unchanged.</summary>
    private static Expression RewriteExpression(Expression expr, IReadOnlyDictionary<string, string> views, ISqlParser parser) => expr switch
    {
        ScalarSubquery s => new ScalarSubquery(RewriteSelect(s.Query, views, parser)),
        ExistsExpression x => new ExistsExpression(RewriteSelect(x.Query, views, parser)),
        InSubqueryExpression i => i with
        {
            Value = RewriteExpression(i.Value, views, parser),
            Query = RewriteSelect(i.Query, views, parser),
        },
        BinaryExpression b => b with
        {
            Left = RewriteExpression(b.Left, views, parser),
            Right = RewriteExpression(b.Right, views, parser),
        },
        UnaryExpression u => u with { Operand = RewriteExpression(u.Operand, views, parser) },
        FunctionCall f => f with { Arguments = f.Arguments.Select(a => RewriteExpression(a, views, parser)).ToList() },
        _ => expr,
    };

    private static TableReference RewriteSource(TableReference source, IReadOnlyDictionary<string, string> views, ISqlParser parser) => source switch
    {
        NamedTable n when views.TryGetValue(n.Name, out string? sql) =>
            new SubqueryTable(
                Rewrite(parser.ParseStatement(sql), views, parser), // expand views nested in the view
                n.Alias ?? n.Name),
        JoinTable j => j with
        {
            Left = RewriteSource(j.Left, views, parser),
            Right = RewriteSource(j.Right, views, parser),
        },
        SubqueryTable sq => sq with { Query = Rewrite(sq.Query, views, parser) },
        _ => source,
    };
}
