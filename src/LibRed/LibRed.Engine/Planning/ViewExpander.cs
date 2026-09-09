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
/// <remarks>
/// The recursion carries the set of views currently being expanded, so a view that reaches itself is
/// reported instead of expanded forever. Nothing stops such a view being created — the binder never
/// validates a <c>CREATE VIEW</c> body's sources, so <c>CREATE VIEW V AS SELECT * FROM V</c> is accepted
/// and stored — and the failure without this guard is <see cref="StackOverflowException"/>, which .NET
/// cannot catch: it takes the host process down rather than failing the statement.
/// </remarks>
internal static class ViewExpander
{
    public static SqlStatement Expand(SqlStatement statement, IReadOnlyDictionary<string, string> views, ISqlParser parser) =>
        views.Count == 0 ? statement : Rewrite(statement, views, parser, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static SqlStatement Rewrite(
        SqlStatement statement, IReadOnlyDictionary<string, string> views, ISqlParser parser, HashSet<string> active) => statement switch
    {
        SelectStatement s => RewriteSelect(s, views, parser, active),
        SetOperationStatement so => so with
        {
            Left = Rewrite(so.Left, views, parser, active),
            Right = Rewrite(so.Right, views, parser, active),
        },
        _ => statement,
    };

    private static SelectStatement RewriteSelect(
        SelectStatement select, IReadOnlyDictionary<string, string> views, ISqlParser parser, HashSet<string> active)
    {
        Expression Expr(Expression e) => RewriteExpression(e, views, parser, active);
        return select with
        {
            From = select.From is null ? null : RewriteSource(select.From, views, parser, active),
            Projection = select.Projection.Select(i => i with { Value = Expr(i.Value) }).ToList(),
            Where = select.Where is { } w ? Expr(w) : null,
            GroupBy = select.GroupBy.Select(Expr).ToList(),
            Having = select.Having is { } h ? Expr(h) : null,
            OrderBy = select.OrderBy.Select(o => o with { Value = Expr(o.Value) }).ToList(),
        };
    }

    /// <summary>Rewrites views referenced inside expression subqueries (scalar / EXISTS), recursing through
    /// the operator/function tree; leaf expressions are returned unchanged.</summary>
    private static Expression RewriteExpression(
        Expression expr, IReadOnlyDictionary<string, string> views, ISqlParser parser, HashSet<string> active) => expr switch
    {
        // Rewrite, not RewriteSelect: a subquery may be a set operation, whose arms each need view expansion.
        ScalarSubquery s => new ScalarSubquery(Rewrite(s.Query, views, parser, active)),
        ExistsExpression x => new ExistsExpression(Rewrite(x.Query, views, parser, active)),
        InSubqueryExpression i => i with
        {
            Value = RewriteExpression(i.Value, views, parser, active),
            Query = Rewrite(i.Query, views, parser, active),
        },
        BinaryExpression b => b with
        {
            Left = RewriteExpression(b.Left, views, parser, active),
            Right = RewriteExpression(b.Right, views, parser, active),
        },
        UnaryExpression u => u with { Operand = RewriteExpression(u.Operand, views, parser, active) },
        FunctionCall f => f with { Arguments = f.Arguments.Select(a => RewriteExpression(a, views, parser, active)).ToList() },
        _ => expr,
    };

    private static TableReference RewriteSource(
        TableReference source, IReadOnlyDictionary<string, string> views, ISqlParser parser, HashSet<string> active) => source switch
    {
        NamedTable n when views.TryGetValue(n.Name, out string? sql) => ExpandView(n, sql, views, parser, active),
        JoinTable j => j with
        {
            Left = RewriteSource(j.Left, views, parser, active),
            Right = RewriteSource(j.Right, views, parser, active),
        },
        SubqueryTable sq => sq with { Query = Rewrite(sq.Query, views, parser, active) },
        _ => source,
    };

    /// <summary>Expands one view reference into a derived table, refusing a definition that reaches itself.
    /// The name is removed again on the way out, so two sibling references to the same view are fine — only a
    /// reference reached from INSIDE that view's own expansion is a cycle.</summary>
    private static TableReference ExpandView(
        NamedTable table, string sql, IReadOnlyDictionary<string, string> views, ISqlParser parser, HashSet<string> active)
    {
        if (!active.Add(table.Name))
            throw new InvalidOperationException(
                $"View '{table.Name}' is defined in terms of itself ({string.Join(" -> ", active)} -> {table.Name}).");
        try
        {
            return new SubqueryTable(
                Rewrite(parser.ParseStatement(sql), views, parser, active), // expand views nested in the view
                table.Alias ?? table.Name);
        }
        finally { active.Remove(table.Name); }
    }
}
