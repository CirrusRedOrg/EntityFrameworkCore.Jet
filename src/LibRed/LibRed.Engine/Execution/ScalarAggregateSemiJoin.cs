using LibRed.Catalog;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Turns a correlated scalar aggregate — <c>… &gt; (SELECT COUNT(*) FROM I WHERE I.K = o.K)</c> — into one grouped
/// pass: run the body once grouped BY the correlation columns, giving one aggregate value per key, then look each
/// outer row's key up in that map instead of re-running the aggregate for it.
/// </summary>
/// <remarks>
/// <para>
/// The grouping is what makes it exact. The correlated body aggregates over the rows with <c>K = </c>this row's
/// key; grouping by <c>K</c> aggregates over precisely the same rows, one key at a time in a single pass.
/// </para>
/// <para>
/// The trap is the missing key. Absence from the map is NOT null: a bare aggregate over zero rows still returns a
/// row, so an outer row with no partner gets <c>COUNT(*) = 0</c> — while <c>SUM</c>/<c>MIN</c>/<c>MAX</c> do give
/// null there. Rather than encode a table of per-aggregate empty values (which could drift from what the evaluator
/// actually computes), the miss value is obtained by asking the executor to compute this very aggregate call over
/// an empty group. It is the same code path the per-row form would have taken, so the two cannot disagree.
/// </para>
/// <para>
/// Only a lone aggregate call is accepted. A non-aggregate scalar body (<c>SELECT TOP 1 x … ORDER BY …</c>) has
/// first-row semantics, whose answer depends on an ordering this rewrite discards, and an expression built around
/// an aggregate would need its own empty-input evaluation; both decline. So does a <c>GROUP BY</c> body, which
/// yields several rows for the outer row to take the first of.
/// </para>
/// </remarks>
internal sealed class ScalarAggregateSemiJoin
{
    private readonly SelectStatement _keyQuery;
    private readonly IReadOnlyList<Expression> _outerKeys;

    /// <summary>The aggregate call itself, kept to compute the value a key absent from the map stands for.</summary>
    private readonly FunctionCall _aggregate;

    private Dictionary<object?[], object?>? _values;
    private object? _empty;

    private ScalarAggregateSemiJoin(SelectStatement keyQuery, IReadOnlyList<Expression> outerKeys, FunctionCall aggregate)
        => (_keyQuery, _outerKeys, _aggregate) = (keyQuery, outerKeys, aggregate);

    internal static ScalarAggregateSemiJoin? TryBuild(
        SelectStatement subquery,
        IReadOnlyList<OutputColumn> outerColumns,
        HashSet<string> outerAliases,
        JetCatalog catalog)
    {
        // A lone aggregate call and nothing else — see the remarks. TOP is redundant over a single row but would
        // have to be reasoned about, and DISTINCT/ORDER BY over one row are no-ops, so the strictest form is taken.
        if (subquery is not
            {
                Top: null, GroupBy.Count: 0, Having: null, IsSelectStar: false,
                Projection: [{ Value: FunctionCall { Name: { } name } call }],
            }
            || !Planning.QueryPlanner.IsAggregate(name)
            || SubqueryCorrelation.TrySplit(subquery, outerColumns, outerAliases, catalog) is not { } split)
        {
            return null;
        }

        var keyQuery = subquery with
        {
            // Correlation columns first so the aggregate lands at a known index; they must be grouping columns to
            // be projectable alongside an aggregate at all.
            Projection = [.. split.InnerKeys.Select(k => new SelectItem(k, null)), subquery.Projection[0]],
            GroupBy = [.. split.InnerKeys],
            Where = split.Residual,
            OrderBy = [],
            Distinct = false,
            DistinctRow = false,
        };

        return new ScalarAggregateSemiJoin(keyQuery, split.OuterKeys, call);
    }

    /// <summary>
    ///     The value the correlated body would have returned for this outer row. The grouped pass runs on first
    ///     use — once per statement, not per row.
    /// </summary>
    internal object? Evaluate(QueryExecutor executor, ExpressionEvaluator outerEval)
    {
        if (_values is null)
        {
            _values = executor.BuildGroupedAggregate(_keyQuery, _outerKeys.Count);
            _empty = executor.EmptyGroupAggregate(_aggregate);
        }

        var probe = new object?[_outerKeys.Count];
        for (var i = 0; i < _outerKeys.Count; i++)
        {
            // A null correlation value equals no inner row, so the body aggregates over nothing.
            if ((probe[i] = outerEval.Evaluate(_outerKeys[i])) is null)
            {
                return _empty;
            }
        }

        return _values.TryGetValue(probe, out object? value) ? value : _empty;
    }
}
