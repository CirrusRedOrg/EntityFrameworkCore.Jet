using LibRed.Catalog;
using LibRed.Engine.Execution;
using LibRed.Sql.Ast;
using LibRed.Sql.Binding;
using LibRed.Sql.Parsing;

namespace LibRed.Engine.Planning;

/// <summary>
/// Rewrites a query so that any reference to a view becomes a derived table (a subquery over the view's
/// stored SELECT) — the same shape the planner already handles for explicit subqueries. A view named in
/// a FROM clause, or inside a scalar/EXISTS subquery in any clause (projection, WHERE, GROUP BY, HAVING,
/// ORDER BY), is expanded, and so is one an INSERT, UPDATE or DELETE reads — not the table it writes. View SQL is reconstructed by the catalog (<c>JetCatalog.FindQuery</c>) and parsed
/// here. Views nested inside other views are expanded recursively. A name that is a table is never a view —
/// tables and queries share one container, whose names are unique — so a name is only looked up as a query
/// when <c>FindTable</c> does not know it.
/// </summary>
/// <remarks>
/// <para>The recursion carries the set of views currently being expanded, so a view that reaches itself is
/// reported instead of expanded forever. Nothing stops such a view being created — the binder never
/// validates a <c>CREATE VIEW</c> body's sources, so <c>CREATE VIEW V AS SELECT * FROM V</c> is accepted
/// and stored — and the failure without this guard is <see cref="StackOverflowException"/>, which .NET
/// cannot catch: it takes the host process down rather than failing the statement.</para>
/// <para>It also carries the database names resolve in. A table whose FROM has an IN externaldatabase is looked up
/// in that file — as a table, or as a query of that file's, which is expanded there — and everything inside such a
/// query belongs to that file too, its subqueries included. Anywhere else is the statement's own database, a
/// subquery under an external FROM as well (verified vs ACE). The binder cannot see another file, so a missing
/// external table is refused here.</para>
/// </remarks>
internal static class ViewExpander
{
    /// <summary>Where names resolve: the database a statement runs on, the catalog of the file names are being
    /// looked up in, and that file's IN clause (null for the database itself).</summary>
    private sealed record Scope(
        JetDatabase Home, JetCatalog Catalog, ExternalDatabase? Database, ISqlParser Parser, HashSet<string> Active);

    // IDE0028's only fix here is `[]`, which would silently drop the comparer and let a view expand
    // itself recursively when the cycle guard's names differ only by case.
#pragma warning disable IDE0028
    public static SqlStatement Expand(SqlStatement statement, JetDatabase database, ISqlParser parser) =>
        Rewrite(statement, new Scope(database, database.Catalog, null, parser, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
#pragma warning restore IDE0028

    private static SqlStatement Rewrite(SqlStatement statement, Scope scope) => statement switch
    {
        SelectStatement s => RewriteSelect(s, scope),
        SetOperationStatement so => so with
        {
            Left = Rewrite(so.Left, scope),
            Right = Rewrite(so.Right, scope),
        },
        TransformStatement t => t with
        {
            Value = RewriteExpression(t.Value, scope),
            Select = RewriteSelect(t.Select, scope),
            Pivot = RewriteExpression(t.Pivot, scope),
        },
        // What an INSERT, UPDATE or DELETE reads goes through views as a SELECT's does: an INSERT's source and values,
        // and an UPDATE's or DELETE's subqueries. The table it writes is left as written — writing through a query is
        // not done.
        InsertStatement i => i with
        {
            Source = i.Source is { } source ? Rewrite(source, scope) : null,
            Rows = i.Rows.Select(row => (IReadOnlyList<Expression>)row.Select(e => RewriteExpression(e, scope)).ToList()).ToList(),
        },
        UpdateStatement u => u with
        {
            Assignments = u.Assignments.Select(a => a with { Value = RewriteExpression(a.Value, scope) }).ToList(),
            Where = u.Where is { } updateWhere ? RewriteExpression(updateWhere, scope) : null,
        },
        DeleteStatement d => d with { Where = d.Where is { } deleteWhere ? RewriteExpression(deleteWhere, scope) : null },
        _ => statement,
    };

    private static SelectStatement RewriteSelect(SelectStatement select, Scope scope)
    {
        Expression Expr(Expression e) => RewriteExpression(e, scope);
        return select with
        {
            From = select.From is null ? null : RewriteSource(select.From, scope),
            Projection = select.Projection.Select(i => i with { Value = Expr(i.Value) }).ToList(),
            Where = select.Where is { } w ? Expr(w) : null,
            GroupBy = select.GroupBy.Select(Expr).ToList(),
            Having = select.Having is { } h ? Expr(h) : null,
            OrderBy = select.OrderBy.Select(o => o with { Value = Expr(o.Value) }).ToList(),
        };
    }

    /// <summary>Rewrites views referenced inside expression subqueries (scalar / EXISTS), recursing through
    /// the operator/function tree; leaf expressions are returned unchanged.</summary>
    private static Expression RewriteExpression(Expression expr, Scope scope) => expr switch
    {
        // Rewrite, not RewriteSelect: a subquery may be a set operation, whose arms each need view expansion.
        ScalarSubquery s => new ScalarSubquery(Rewrite(s.Query, scope)),
        ExistsExpression x => new ExistsExpression(Rewrite(x.Query, scope)),
        InSubqueryExpression i => i with
        {
            Value = RewriteExpression(i.Value, scope),
            Query = Rewrite(i.Query, scope),
        },
        _ => expr.MapOperands(o => RewriteExpression(o, scope)),
    };

    private static TableReference RewriteSource(TableReference source, Scope scope) => source switch
    {
        NamedTable n => RewriteNamed(n, scope),
        JoinTable j => j with
        {
            Left = RewriteSource(j.Left, scope),
            Right = RewriteSource(j.Right, scope),
        },
        SubqueryTable sq => sq with { Query = Rewrite(sq.Query, scope) },
        _ => source,
    };

    /// <summary>A named table or query, in the file its own IN clause names or else the one the scope is in, which it
    /// is stamped with so the planner reads it from there.</summary>
    private static TableReference RewriteNamed(NamedTable table, Scope scope)
    {
        if (table.Database is { } named && named != scope.Database)
            scope = scope with { Catalog = ExternalDatabases.Open(scope.Home, named).Catalog, Database = named };
        table = table with { Database = scope.Database };

        if (scope.Catalog.FindQuery(table.Name) is { IsAction: false, Sql: { } sql })
            return ExpandView(table, sql, scope);
        if (scope.Database is not null && scope.Catalog.FindTable(table.Name) is null)
            throw new SqlBindException(SqlBindException.InputNotFound(table.Name));
        return table;
    }

    /// <summary>Expands one view reference into a derived table, refusing a definition that reaches itself.
    /// The name is removed again on the way out, so two sibling references to the same view are fine — only a
    /// reference reached from INSIDE that view's own expansion is a cycle.</summary>
    private static SubqueryTable ExpandView(NamedTable table, string sql, Scope scope)
    {
        string key = scope.Database is { } database ? $"{database.File}|{table.Name}" : table.Name;
        if (!scope.Active.Add(key))
            throw new InvalidOperationException(
                $"View '{table.Name}' is defined in terms of itself ({string.Join(" -> ", scope.Active)} -> {table.Name}).");
        try
        {
            return new SubqueryTable(
                Rewrite(scope.Parser.ParseStatement(sql), scope), // expand views nested in the view
                table.Alias ?? table.Name);
        }
        finally { scope.Active.Remove(key); }
    }
}
