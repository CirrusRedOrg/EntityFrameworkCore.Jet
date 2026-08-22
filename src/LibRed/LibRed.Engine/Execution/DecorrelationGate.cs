using System.Diagnostics;

namespace LibRed.Engine.Execution;

/// <summary>
/// Decides <b>when</b> a correlated subquery stops being evaluated per outer row and switches to its decorrelated
/// form. Whether it <b>may</b> is a separate question, settled by <see cref="SubqueryCorrelation" /> and the
/// rewrites themselves; this is only about cost.
/// </summary>
/// <remarks>
/// <para>
/// It exists because decorrelating is not free. The rewrite runs the body once <i>without</i> the correlation, so a
/// body that per-row would have been a single index seek pays a full pass instead. Measured on a 3-row outer
/// against a 20,000-row indexed inner: 0.2 ms per-row against 32.8 ms decorrelated for <c>EXISTS</c>, 0.1 against
/// 34.5 for <c>IN</c>, and 0.2 against 117.7 for a <c>COUNT</c> — 160x to 590x the wrong way, in the same engine
/// where decorrelating turns 92 s into 0.4 s the other way.
/// </para>
/// <para>
/// Choosing up front would need the outer row count, and nobody has it at the first probe: rows stream, and the
/// subquery is asked for its answer long before the outer scan has finished. So don't choose — measure. Evaluate
/// per row, accumulate the time actually spent doing it, and switch once that reaches a budget. The loss is then
/// bounded whichever way the query leans: a per-row-friendly query never reaches the budget and wastes nothing,
/// and a decorrelation-friendly one wastes at most the budget before switching.
/// </para>
/// <para>
/// The budget only has to separate "a handful of index seeks" from "a query that is actually slow" — microseconds
/// from seconds — so its exact value is not delicate; three orders of magnitude sit between the two.
/// </para>
/// <para>
/// What this deliberately cannot see is the case in between: a medium outer with cheap seeks over a very large
/// inner, where per-row would have finished just above the budget and the build costs far more than the probes
/// that remained. Bounding that needs the inner's cardinality — the TDEF carries a row count, which is where a
/// later refinement would start. Recorded rather than guessed at.
/// </para>
/// <para>
/// Switching mid-scan is sound for two reasons: both forms answer identically, which is what the semantics tests
/// pin, and DML materialises its entire row set before mutating anything, so no probe and no build ever observes a
/// partly-modified table.
/// </para>
/// </remarks>
internal sealed class DecorrelationGate
{
    /// <summary>25 ms of per-row work before the decorrelated form takes over.</summary>
    private static readonly long DefaultBudget = Stopwatch.Frequency / 40;

    private readonly long _budget;
    private readonly Func<long> _timestamp;
    private long _spent;

    internal DecorrelationGate(long? budget = null, Func<long>? timestamp = null)
    {
        _budget = budget ?? DefaultBudget;
        if (_budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }

    /// <summary>Whether enough per-row time has been spent to justify one pass over the whole body.</summary>
    internal bool Ready => _spent >= _budget;

    /// <summary>Charges one per-row evaluation, given the timestamp taken just before it started.</summary>
    internal void Charge(long startTimestamp)
    {
        long elapsed = _timestamp() - startTimestamp;
        if (elapsed > 0) _spent += elapsed;
    }
}
