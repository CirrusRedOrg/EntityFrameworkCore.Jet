using LibRed.Catalog;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;

namespace LibRed.Engine.Planning;

/// <summary>
/// Rewrites a query so that any reference to a view becomes a derived table (a subquery over the view's
/// stored SELECT) — the same shape the planner already handles for explicit subqueries. A view named in
/// a FROM clause, or inside a scalar/EXISTS subquery in any clause (projection, WHERE, GROUP BY, HAVING,
/// ORDER BY), is expanded. View SQL is reconstructed by the catalog (<c>JetCatalog.FindQuery</c>) and parsed
/// here. Views nested inside other views are expanded recursively. A name that is a table is never a view —
/// tables and queries share one container, whose names are unique — so a name is only looked up as a query
/// when <c>FindTable</c> does not know it.
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
    // IDE0028's only fix here is `[]`, which would silently drop the comparer and let a view expand
    // itself recursively when the cycle guard's names differ only by case.
#pragma warning disable IDE0028
    public static SqlStatement Expand(SqlStatement statement, JetCatalog catalog, ISqlParser parser) =>
        Rewrite(statement, catalog, parser, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
#pragma warning restore IDE0028

    private static SqlStatement Rewrite(
        SqlStatement statement, JetCatalog catalog, ISqlParser parser, HashSet<string> active) => statement switch
        {
            SelectStatement s => RewriteSelect(s, catalog, parser, active),
            SetOperationStatement so => so with
            {
                Left = Rewrite(so.Left, catalog, parser, active),
                Right = Rewrite(so.Right, catalog, parser, active),
            },
            _ => statement,
        };

    private static SelectStatement RewriteSelect(
        SelectStatement select, JetCatalog catalog, ISqlParser parser, HashSet<string> active)
    {
        Expression Expr(Expression e) => RewriteExpression(e, catalog, parser, active);
        return select with
        {
            From = select.From is null ? null : RewriteSource(select.From, catalog, parser, active),
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
        Expression expr, JetCatalog catalog, ISqlParser parser, HashSet<string> active) => expr switch
        {
            // Rewrite, not RewriteSelect: a subquery may be a set operation, whose arms each need view expansion.
            ScalarSubquery s => new ScalarSubquery(Rewrite(s.Query, catalog, parser, active)),
            ExistsExpression x => new ExistsExpression(Rewrite(x.Query, catalog, parser, active)),
            InSubqueryExpression i => i with
            {
                Value = RewriteExpression(i.Value, catalog, parser, active),
                Query = Rewrite(i.Query, catalog, parser, active),
            },
            _ => expr.MapOperands(o => RewriteExpression(o, catalog, parser, active)),
        };

    private static TableReference RewriteSource(
        TableReference source, JetCatalog catalog, ISqlParser parser, HashSet<string> active) => source switch
        {
            NamedTable n when catalog.FindQuery(n.Name) is { IsAction: false, Sql: { } sql } => ExpandView(n, sql, catalog, parser, active),
            JoinTable j => j with
            {
                Left = RewriteSource(j.Left, catalog, parser, active),
                Right = RewriteSource(j.Right, catalog, parser, active),
            },
            SubqueryTable sq => sq with { Query = Rewrite(sq.Query, catalog, parser, active) },
            _ => source,
        };

    /// <summary>Expands one view reference into a derived table, refusing a definition that reaches itself.
    /// The name is removed again on the way out, so two sibling references to the same view are fine — only a
    /// reference reached from INSIDE that view's own expansion is a cycle.</summary>
    private static SubqueryTable ExpandView(
        NamedTable table, string sql, JetCatalog catalog, ISqlParser parser, HashSet<string> active)
    {
        if (!active.Add(table.Name))
            throw new InvalidOperationException(
                $"View '{table.Name}' is defined in terms of itself ({string.Join(" -> ", active)} -> {table.Name}).");
        try
        {
            return new SubqueryTable(
                Rewrite(parser.ParseStatement(sql), catalog, parser, active), // expand views nested in the view
                table.Alias ?? table.Name);
        }
        finally { active.Remove(table.Name); }
    }
}