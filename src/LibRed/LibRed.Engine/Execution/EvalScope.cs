using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// The column bindings visible while evaluating an expression: the current row's schema and
/// values, plus a link to the enclosing query's scope so correlated subqueries can resolve
/// outer columns.
/// </summary>
internal sealed class EvalScope(IReadOnlyList<OutputColumn> schema, object?[] row, EvalScope? outer)
{
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
}

/// <summary>Executes a subquery, correlating it to <paramref name="outerScope"/>.</summary>
internal interface IScalarSubqueryRunner
{
    object? ExecuteScalar(SelectStatement query, EvalScope outerScope);

    /// <summary>True when the (possibly correlated) subquery returns at least one row.</summary>
    bool ExecuteExists(SelectStatement query, EvalScope outerScope);
}
