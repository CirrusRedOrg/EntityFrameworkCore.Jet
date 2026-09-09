namespace LibRed.Engine.Execution;

/// <summary>
/// Hash membership over the values of an uncorrelated <c>IN (subquery)</c> body, built once when the body is
/// hoisted so that testing an outer row is a lookup rather than a walk of the hoisted list.
/// </summary>
/// <remarks>
/// <para>The list is hoisted already — an uncorrelated body runs once — but the membership test over it ran per
/// outer row, so a query like <c>WHERE K IN (SELECT K FROM Small)</c> cost outer × inner comparisons: 100k rows
/// against 100 values is ~10M <see cref="ExpressionEvaluator.Compare"/> calls, which measured 13× the same
/// query written as a join (the join hashes). This closes that gap without changing the plan.</para>
/// <para><b>Why only some values qualify.</b> A hash consistent with the evaluator's equality exists only
/// within one type kind, which is the same constraint the hash join lives under: <c>5 = '5'</c> and
/// <c>5 = 5.0</c>, but <c>'5' ≠ '5.0'</c>, so no single hash can agree with <c>=</c> across kinds. Numeric and
/// text are taken because <see cref="ExpressionEvaluator.KeyHash"/> is defined to agree with
/// <see cref="ExpressionEvaluator.KeyEqual"/> for exactly those (numeric via double, text via Access's
/// case-insensitive, trailing-space-trimmed collation). Everything else — mixed kinds in the body, or a probe
/// of a different kind from the body — declines, and the caller scans the list as it always did. Declining
/// costs nothing but the old behaviour; a wrong hash would silently drop matching rows.</para>
/// <para>Dates deliberately do not qualify. The evaluator compares two <c>DateTime</c>s by their OLE Automation
/// serial (to agree with the index key encoding — see <see cref="ExpressionEvaluator.Compare"/>), while
/// <c>KeyHash</c> would fall through to <c>DateTime.GetHashCode</c> over ticks. Two values equal by serial but
/// differing in ticks would hash apart, so the set would miss them.</para>
/// </remarks>
internal sealed class HoistedInSet
{
    /// <summary>The kinds a value may take for membership purposes. Only kinds whose hash is defined to agree
    /// with the evaluator's equality appear here; anything else makes the set decline.</summary>
    private enum Kind
    {
        Numeric,
        Text,
    }

    private static readonly IEqualityComparer<object> Comparer = new EvaluatorEquality();

    private readonly HashSet<object> _values;
    private readonly Kind _kind;

    private HoistedInSet(HashSet<object> values, Kind kind, bool hasNull)
    {
        _values = values;
        _kind = kind;
        HasNull = hasNull;
    }

    /// <summary>Whether the body produced a null, which <c>IN</c> reports as UNKNOWN rather than no-match.</summary>
    private bool HasNull { get; }

    /// <summary>Builds a set over <paramref name="values"/>, or null when they cannot be hashed consistently
    /// (a kind outside <see cref="Kind"/>, or more than one kind among them). An empty or all-null body also
    /// returns null: there is nothing to accelerate, and the caller's scan of it is already trivial.</summary>
    public static HoistedInSet? TryBuild(IReadOnlyList<object?> values)
    {
        Kind? kind = null;
        bool hasNull = false;
        var set = new HashSet<object>(Comparer);

        foreach (object? value in values)
        {
            if (value is null)
            {
                hasNull = true;
                continue;
            }

            if (KindOf(value) is not { } valueKind || (kind is { } only && only != valueKind))
                return null;

            kind = valueKind;
            set.Add(value);
        }

        return kind is { } single ? new HoistedInSet(set, single, hasNull) : null;
    }

    /// <summary>Membership of <paramref name="value"/>, or null when it is of a different kind from the body's
    /// values — where the hash says nothing and only the evaluator's own comparison can answer.</summary>
    public (bool Found, bool HasNull)? Lookup(object value) =>
        KindOf(value) == _kind ? (_values.Contains(value), HasNull) : null;

    private static Kind? KindOf(object value) => value switch
    {
        string => Kind.Text,
        _ when ExpressionEvaluator.IsNumeric(value) => Kind.Numeric,
        _ => null,
    };

    /// <summary>Equality and hashing delegated to the evaluator, so the set agrees with <c>=</c> exactly.</summary>
    private sealed class EvaluatorEquality : IEqualityComparer<object>
    {
        public new bool Equals(object? a, object? b) =>
            a is not null && b is not null && ExpressionEvaluator.KeyEqual(a, b);

        public int GetHashCode(object value) => ExpressionEvaluator.KeyHash(value);
    }
}
