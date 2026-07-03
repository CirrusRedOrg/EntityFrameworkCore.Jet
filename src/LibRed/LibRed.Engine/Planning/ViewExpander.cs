using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;

namespace LibRed.Engine.Planning;

/// <summary>
/// Rewrites a query so that any reference to a view in a FROM clause becomes a derived table (a
/// subquery over the view's stored SELECT) — the same shape the planner already handles for explicit
/// subqueries. View SQL is reconstructed by the catalog (<c>JetCatalog.Views</c>) and parsed here.
/// Views nested inside other views are expanded recursively.
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

    private static SelectStatement RewriteSelect(SelectStatement select, IReadOnlyDictionary<string, string> views, ISqlParser parser) =>
        select with { From = RewriteSource(select.From, views, parser) };

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
