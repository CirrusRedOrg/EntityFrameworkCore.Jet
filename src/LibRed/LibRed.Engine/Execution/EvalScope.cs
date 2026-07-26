using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// The column bindings visible while evaluating an expression: the current row's schema and
/// values, plus a link to the enclosing query's scope so correlated subqueries can resolve
/// outer columns.
/// </summary>
internal sealed class EvalScope(
    IReadOnlyList<OutputColumn> schema, object?[] row, EvalScope? outer,
    IReadOnlyDictionary<FunctionCall, object?>? aggregates = null)
{
    // The current row's values. Mutable so a hot loop (e.g. a join's per-row key/residual evaluation) can
    // rebind the same scope + evaluator to successive rows instead of allocating a fresh pair each iteration;
    // the schema and outer link are fixed for the scope's lifetime.
    private object?[] row = row;

    /// <summary>Points this scope at a new row of the same schema (see <see cref="Rebind"/>'s callers for why
    /// reuse is worthwhile). Returns the scope for fluent use.</summary>
    public EvalScope Rebind(object?[] newRow)
    {
        row = newRow;
        return this;
    }

    /// <summary>Resolves a precomputed aggregate (by reference), walking out to enclosing scopes so an outer
    /// aggregate referenced inside a correlated subquery (e.g. <c>… WHERE x = MAX(o.Col) …</c>) is found.</summary>
    public bool TryResolveAggregate(FunctionCall call, out object? value)
    {
        if (aggregates is not null && aggregates.TryGetValue(call, out value)) return true;
        if (outer is not null) return outer.TryResolveAggregate(call, out value);
        value = null;
        return false;
    }

    public bool TryResolve(ColumnReference reference, out object? value)
    {
        int found = -1;
        for (int i = 0; i < schema.Count; i++)
        {
            bool nameMatch = string.Equals(schema[i].Name, reference.Column, StringComparison.OrdinalIgnoreCase);
            bool qualifierMatch = reference.Table is null
                || string.Equals(schema[i].Qualifier, reference.Table, StringComparison.OrdinalIgnoreCase);
            if (!nameMatch || !qualifierMatch) continue;

            if (found >= 0)
                throw new InvalidOperationException($"Column reference '{Describe(reference)}' is ambiguous.");
            found = i;
        }

        if (found >= 0)
        {
            value = row[found];
            return true;
        }

        if (outer is not null)
            return outer.TryResolve(reference, out value);

        value = null;
        return false;
    }

    internal static string Describe(ColumnReference r) => r.Table is null ? r.Column : $"{r.Table}.{r.Column}";

    /// <summary>Every table alias visible in this scope and its enclosing scopes — so index selection on a
    /// correlated subquery knows which column references belong to the outer query (and are seekable constants).</summary>
    public IEnumerable<string> VisibleAliases()
    {
        foreach (OutputColumn c in schema)
            if (c.Qualifier is { } q)
                yield return q;
        if (outer is not null)
            foreach (string a in outer.VisibleAliases())
                yield return a;
    }

    /// <summary>Every column visible in this scope and its enclosing scopes — so decorrelating a correlated
    /// EXISTS can read the outer side's declared type, which decides whether a hash key is sound (see
    /// <see cref="ExistsSemiJoin"/>).</summary>
    public IReadOnlyList<OutputColumn> AllColumns()
    {
        if (outer is null) return schema;
        var all = new List<OutputColumn>(schema);
        all.AddRange(outer.AllColumns());
        return all;
    }
}

/// <summary>Executes a subquery, correlating it to <paramref name="outerScope"/>.</summary>
internal interface IScalarSubqueryRunner
{
    object? ExecuteScalar(SelectStatement query, EvalScope outerScope);

    /// <summary>True when the (possibly correlated) subquery returns at least one row.</summary>
    bool ExecuteExists(SelectStatement query, EvalScope outerScope);

    /// <summary>The values of the first column of the (possibly correlated) subquery — for <c>IN (subquery)</c>.</summary>
    IEnumerable<object?> ExecuteColumn(SelectStatement query, EvalScope outerScope);

    /// <summary>
    ///     Membership of <paramref name="value" /> in a correlated subquery's column, answered from a hash rather
    ///     than by re-running the body for this row, together with whether that column held a null (which
    ///     <c>IN</c> reports as UNKNOWN, not as no-match). Null when the subquery has no such form and the caller
    ///     should fall back to <see cref="ExecuteColumn" />.
    /// </summary>
    (bool Found, bool HasNull)? ExecuteInSubquery(SelectStatement query, Expression value, object? evaluated, EvalScope outerScope);
}
