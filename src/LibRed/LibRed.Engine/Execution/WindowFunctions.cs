namespace LibRed.Engine.Execution;

/// <summary>
/// One partition of a window's input, its rows already in window order, together with the peer-group
/// information the ranking functions need. A <b>peer group</b> is a run of adjacent rows whose ORDER BY keys
/// compare equal; with no ORDER BY the whole partition is one peer group, which is what makes RANK constant
/// over an unordered window.
/// </summary>
/// <remarks>Positions are indexes into the partition's own window order, not into the input.</remarks>
/// <param name="starArgument">Whether the call's argument is <c>*</c>, as in COUNT(*), which has no values.</param>
/// <param name="currencyArgument">Whether the call's first argument is a Currency, which the statistical aggregates
/// square exactly.</param>
internal sealed class WindowPartition(
    IReadOnlyList<int> peerStart, IReadOnlyList<int> peerOrdinal, IReadOnlyList<object?[]> arguments,
    bool starArgument = false, bool currencyArgument = false)
{
    public bool StarArgument => starArgument;

    public bool CurrencyArgument => currencyArgument;

    /// <summary>Rows in this partition.</summary>
    public int Count => peerStart.Count;

    /// <summary>Position of the first row of the peer group holding <paramref name="position"/>. RANK is this
    /// plus one, which is why ranks skip after a tie.</summary>
    public int PeerStart(int position) => peerStart[position];

    /// <summary>Zero-based ordinal of the peer group holding <paramref name="position"/>, counted from the
    /// start of the partition. DENSE_RANK is this plus one, which is why it does not skip.</summary>
    public int PeerOrdinal(int position) => peerOrdinal[position];

    /// <summary>The value of the call's <paramref name="argument"/>th argument on the row at
    /// <paramref name="position"/> — for the functions that take one (NTILE, LAG, FIRST_VALUE, …).</summary>
    public object? Argument(int position, int argument) => arguments[position][argument];

    /// <summary>How many arguments the call was given.</summary>
    public int ArgumentCount => arguments.Count == 0 ? 0 : arguments[0].Length;
}

/// <summary>Fills <paramref name="output"/> — one slot per row of the partition, in window order.</summary>
internal delegate void WindowEvaluator(WindowPartition partition, object?[] output);

/// <summary>What a window function's declared type can depend on: its arguments' declared types.</summary>
internal interface IWindowTyping
{
    /// <summary>The declared type of an argument; null when it is absent or its type is unknown.</summary>
    Type? ArgumentType(int argument);

    /// <summary>The one type the given arguments' values share, as CASE's alternatives do; an absent argument or a
    /// bare Null takes no part. Null when any other has an unknown type or the types cannot be reconciled.</summary>
    Type? SharedType(params int[] arguments);
}

/// <param name="ResultType">The declared CLR type of the result — the window counterpart of
/// QueryExecutor.DeclaredFunctionType. The values a function returns are converted to it.</param>
internal sealed record WindowFunctionDef(
    int MinArguments, int MaxArguments, Func<IWindowTyping, Type?> ResultType, WindowEvaluator Evaluate);

/// <summary>
/// The window functions the engine implements. This table IS the extension point: because the grammar hangs
/// <c>OVER</c> off any function call, adding one is an entry here and nothing else — no grammar, no ANTLR
/// regeneration, no AST, no planner and no executor change.
/// </summary>
internal static class WindowFunctions
{
    private static readonly Dictionary<string, WindowFunctionDef> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        // Position within the partition, 1-based. The only window function EF Core emits.
        ["ROW_NUMBER"] = new(0, 0, static _ => typeof(int),
            static (p, o) => { for (int i = 0; i < o.Length; i++) o[i] = i + 1; }),

        // Peers share a rank, and the next group resumes at its own position — so ranks skip after a tie.
        ["RANK"] = new(0, 0, static _ => typeof(int),
            static (p, o) => { for (int i = 0; i < o.Length; i++) o[i] = p.PeerStart(i) + 1; }),

        // The same, counting peer GROUPS rather than rows, so nothing is skipped.
        ["DENSE_RANK"] = new(0, 0, static _ => typeof(int),
            static (p, o) => { for (int i = 0; i < o.Length; i++) o[i] = p.PeerOrdinal(i) + 1; }),

        // (RANK − 1) / (rows − 1): the share of the partition ranked before the row, 0 for a partition of one.
        ["PERCENT_RANK"] = new(0, 0, static _ => typeof(double),
            static (p, o) =>
            {
                for (int i = 0; i < o.Length; i++)
                    o[i] = o.Length == 1 ? 0.0 : (double)p.PeerStart(i) / (o.Length - 1);
            }),

        // The share of the partition up to and including the row's last peer.
        ["CUME_DIST"] = new(0, 0, static _ => typeof(double),
            static (p, o) =>
            {
                for (int i = 0; i < o.Length;)
                {
                    int end = PeerEnd(p, i, o.Length);
                    for (int k = i; k < end; k++)
                        o[k] = (double)end / o.Length;
                    i = end;
                }
            }),

        ["NTILE"] = new(1, 1, static _ => typeof(int), Ntile),
        ["LAG"] = new(1, 3, static t => t.SharedType(0, 2), static (p, o) => Offset(p, o, forward: false)),
        ["LEAD"] = new(1, 3, static t => t.SharedType(0, 2), static (p, o) => Offset(p, o, forward: true)),

        ["COUNT"] = Aggregate("COUNT"),
        ["SUM"] = Aggregate("SUM"),
        ["AVG"] = Aggregate("AVG"),
        ["MIN"] = Aggregate("MIN"),
        ["MAX"] = Aggregate("MAX"),
        ["VAR"] = Aggregate("VAR"),
        ["VARP"] = Aggregate("VARP"),
        ["STDEV"] = Aggregate("STDEV"),
        ["STDEVP"] = Aggregate("STDEVP"),
        ["STDDEV"] = Aggregate("STDDEV"),
        ["STDDEVP"] = Aggregate("STDDEVP"),
    };

    /// <summary>
    /// An aggregate over the window's default frame, as the standard defines it: with an ORDER BY, the partition's
    /// rows up to the current row and its peers (a running total, ties sharing its value); without one, every row of
    /// the partition, which is then one peer group. The values are the grouped aggregate's
    /// (<see cref="RunningAggregate"/>), and so is the declared type.
    /// </summary>
    private static WindowFunctionDef Aggregate(string name) => new(1, 1,
        typing => QueryExecutor.AggregateResultType(name, typing.ArgumentType(0)),
        (p, o) =>
        {
            if (p.StarArgument && name != "COUNT")
                throw new InvalidOperationException($"{name}(*) is not an aggregate; only COUNT takes *.");
            var aggregate = new RunningAggregate(name, countRows: p.StarArgument, currency: p.CurrencyArgument);
            for (int start = 0; start < o.Length;)
            {
                int end = PeerEnd(p, start, o.Length);
                for (int i = start; i < end; i++)
                    aggregate.Add(p.StarArgument ? null : p.Argument(i, 0));
                object? value = aggregate.Result;
                for (int i = start; i < end; i++)
                    o[i] = value;
                start = end;
            }
        });

    /// <summary>The position after the last peer of the row at <paramref name="position"/>.</summary>
    private static int PeerEnd(WindowPartition p, int position, int count)
    {
        int start = p.PeerStart(position), end = position + 1;
        while (end < count && p.PeerStart(end) == start)
            end++;
        return end;
    }

    /// <summary>
    /// <c>NTILE(n)</c>: the partition cut in window order into <c>n</c> numbered buckets as even as can be, the
    /// first ones taking the rows left over (10 rows in 4 buckets are 3, 3, 2, 2). Ties do not matter. The standard
    /// makes <c>n</c> a constant, so the partition's first row gives it; read as CLng reads it. A Null <c>n</c>
    /// gives Null, and one below 1 is an invalid procedure call.
    /// </summary>
    private static void Ntile(WindowPartition p, object?[] o)
    {
        if (o.Length == 0 || WholeArgument(p.Argument(0, 0), "NTILE's bucket count", least: 1) is not { } buckets)
            return;
        long size = o.Length / buckets, larger = o.Length % buckets;
        long bucket = 1, filled = 0;
        for (int i = 0; i < o.Length; i++)
        {
            if (filled == size + (bucket <= larger ? 1 : 0))
            {
                bucket++;
                filled = 0;
            }
            o[i] = (int)bucket;
            filled++;
        }
    }

    /// <summary>
    /// <c>LAG(x [, offset [, default]])</c> and <c>LEAD</c>: <c>x</c> on the row <c>offset</c> rows before or after in
    /// window order (1 when omitted; 0 is the row itself), or <c>default</c> (Null when omitted) where the partition
    /// has no such row. Rows are counted one by one, ties or not. Each row's own offset and default apply; a Null
    /// offset gives Null, and a negative one is an invalid procedure call.
    /// </summary>
    private static void Offset(WindowPartition p, object?[] o, bool forward)
    {
        for (int i = 0; i < o.Length; i++)
        {
            long offset = 1;
            if (p.ArgumentCount > 1)
            {
                if (WholeArgument(p.Argument(i, 1), "The offset", least: 0) is not { } given)
                {
                    o[i] = null;
                    continue;
                }
                offset = given;
            }
            long target = forward ? i + offset : i - offset;
            o[i] = target >= 0 && target < o.Length
                ? p.Argument((int)target, 0)
                : p.ArgumentCount > 2 ? p.Argument(i, 2) : null;
        }
    }

    /// <summary>A whole-number argument, read as CLng reads it; null for Null, and an invalid procedure call below
    /// <paramref name="least"/>.</summary>
    private static long? WholeArgument(object? value, string what, long least)
    {
        if (value is null)
            return null;
        long n = Convert.ToInt64(ExpressionEvaluator.ConversionNumber(value), System.Globalization.CultureInfo.InvariantCulture);
        return n >= least ? n : throw new ArgumentException($"Invalid procedure call: {what} cannot be {n}.");
    }

    /// <summary>Whether <paramref name="name"/> names a window function this engine can compute. A call with an
    /// OVER clause that this returns false for is a parse-level window function the engine has no evaluator
    /// for — <see cref="Lookup"/> then reports it by name.</summary>
    public static bool IsWindowFunction(string name) => Registry.ContainsKey(name);

    public static WindowFunctionDef Lookup(string name) =>
        Registry.TryGetValue(name, out WindowFunctionDef? def)
            ? def
            : throw new NotSupportedException($"Window function '{name}' is not supported.");
}
