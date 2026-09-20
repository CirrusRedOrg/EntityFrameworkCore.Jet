namespace LibRed.Engine.Execution;

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

/// <summary>What a window function may be written with, beyond its arguments and OVER (PARTITION BY … ORDER BY …).</summary>
[Flags]
internal enum WindowOptions
{
    None = 0,

    /// <summary>A frame clause: the function reads the frame. The standard gives the ranking and offset functions
    /// none — they see the whole partition.</summary>
    Frame = 1,

    /// <summary>RESPECT NULLS or IGNORE NULLS.</summary>
    NullTreatment = 2,

    /// <summary>FROM FIRST or FROM LAST.</summary>
    FromLast = 4,

    /// <summary>DISTINCT, as the aggregates take it.</summary>
    Distinct = 8,

    /// <summary>FILTER (WHERE …), as every aggregate takes it.</summary>
    Filter = 16,
}

/// <param name="ResultType">The declared CLR type of the result — the window counterpart of
/// QueryExecutor.DeclaredFunctionType. The values a function returns are converted to it.</param>
internal sealed record WindowFunctionDef(
    int MinArguments, int MaxArguments, Func<IWindowTyping, Type?> ResultType, WindowEvaluator Evaluate,
    WindowOptions Options = WindowOptions.None);

/// <summary>
/// The window functions the engine implements. This table IS the extension point: because the grammar hangs
/// <c>OVER</c> off any function call, adding one is an entry here and nothing else — no grammar, no ANTLR
/// regeneration, no AST, no planner and no executor change.
/// </summary>
internal static class WindowFunctions
{
    // The value on the frame's first or last row, Null for an empty frame. Over the default frame the first is the
    // partition's, and the last is the current row's last peer — not the partition's last. Declared before Registry,
    // which a static initializer reads in textual order.
    private static readonly WindowFunctionDef FirstValue = new(1, 1, static t => t.ArgumentType(0),
        static (p, o) => FrameValue(p, o, fromLast: false), WindowOptions.Frame | WindowOptions.NullTreatment);

    private static readonly WindowFunctionDef LastValue = new(1, 1, static t => t.ArgumentType(0),
        static (p, o) => FrameValue(p, o, fromLast: true), WindowOptions.Frame | WindowOptions.NullTreatment);

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
            static (p, o) => { for (int i = 0; i < o.Length; i++) o[i] = (double)p.PeerEnd(i) / o.Length; }),

        ["NTILE"] = new(1, 1, static _ => typeof(int), Ntile),
        ["LAG"] = new(1, 3, static t => t.SharedType(0, 2), static (p, o) => Offset(p, o, forward: false),
            WindowOptions.NullTreatment),
        ["LEAD"] = new(1, 3, static t => t.SharedType(0, 2), static (p, o) => Offset(p, o, forward: true),
            WindowOptions.NullTreatment),

        ["FIRST_VALUE"] = FirstValue,
        ["LAST_VALUE"] = LastValue,
        ["NTH_VALUE"] = new(2, 2, static t => t.ArgumentType(0), NthValue,
            WindowOptions.Frame | WindowOptions.NullTreatment | WindowOptions.FromLast),

        // The ordered-set aggregates over each frame; the parser has put the WITHIN GROUP key after the fraction.
        ["PERCENTILE_CONT"] = PercentileOf("PERCENTILE_CONT"),
        ["PERCENTILE_DISC"] = PercentileOf("PERCENTILE_DISC"),
        ["LISTAGG"] = new(2, int.MaxValue, static _ => typeof(string), ListAggOf,
            WindowOptions.Frame | WindowOptions.Distinct | WindowOptions.Filter),
        // SQL Server's spelling of the same aggregate, whose WITHIN GROUP is optional — so over a window it
        // can arrive with just the value and the separator.
        ["STRING_AGG"] = new(2, int.MaxValue, static _ => typeof(string), ListAggOf,
            WindowOptions.Frame | WindowOptions.Distinct | WindowOptions.Filter),

        // Access's own First and Last, over the frame rather than the group: the same rows as FIRST_VALUE and
        // LAST_VALUE, as the grouped forms take the group's first and last row.
        ["FIRST"] = FirstValue,
        ["LAST"] = LastValue,

        // Every aggregate RunningAggregate computes is a window function too — see Lookup.
    };

    /// <summary>
    /// An aggregate over each row's frame — by default, with an ORDER BY, the partition's rows up to the current row
    /// and its peers (a running total, ties sharing its value); without one, every row of the partition. The values
    /// are the grouped aggregate's (<see cref="RunningAggregate"/>), and so is the declared type; an empty frame gives
    /// what an empty group does. With DISTINCT each frame's value counts once, however many of its rows carry it; a
    /// binary set function (CORR, REGR_SLOPE, …) reads a pair from each row and takes no DISTINCT.
    /// </summary>
    private static WindowFunctionDef Aggregate(string name) => new(
        RunningAggregate.IsPair(name) ? 2 : 1,
        RunningAggregate.IsPair(name) ? 2 : 1,
        typing => QueryExecutor.AggregateResultType(name, typing.ArgumentType(0)),
        (p, o) =>
        {
            WindowCall call = p.Call;
            if (call.Star && name != "COUNT")
                throw new InvalidOperationException($"{name}(*) is not an aggregate; only COUNT takes *.");
            var frames = new FrameRows[o.Length];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = p.Frame(i);
            if (frames.Length == 0)
                return;

            // A frame that only ever grows at one end — every running total, the default frame included — is added
            // to row by row rather than summed afresh: from the front when the frames share their first row, from
            // the back when they share their last.
            bool contiguous = frames.All(f => !f.HasExclusion);
            if (contiguous && frames.All(f => f.Start == frames[0].Start) && Growing(frames, f => f.End))
            {
                var aggregate = new FrameAggregate(name, p);
                for (int i = 0, added = frames[0].Start; i < frames.Length; i++)
                {
                    for (; added < frames[i].End; added++)
                        aggregate.Add(added);
                    o[i] = aggregate.Result;
                }
            }
            else if (contiguous && frames.All(f => f.End == frames[0].End) && Growing(frames, f => f.Start))
            {
                var aggregate = new FrameAggregate(name, p);
                for (int i = frames.Length - 1, added = frames[0].End; i >= 0; i--)
                {
                    for (; added > frames[i].Start; added--)
                        aggregate.Add(added - 1);
                    o[i] = aggregate.Result;
                }
            }
            else
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    if (i > 0 && frames[i] == frames[i - 1])
                    {
                        o[i] = o[i - 1];
                        continue;
                    }
                    var aggregate = new FrameAggregate(name, p);
                    foreach (int position in frames[i].Positions())
                        aggregate.Add(position);
                    o[i] = aggregate.Result;
                }
            }
        },
        WindowOptions.Frame | WindowOptions.Filter | (RunningAggregate.IsPair(name) ? 0 : WindowOptions.Distinct));

    /// <summary>A <see cref="RunningAggregate"/> fed from a partition's rows; under DISTINCT each value goes in once,
    /// equal as GROUP BY takes values to be equal.</summary>
    private sealed class FrameAggregate(string name, WindowPartition p)
    {
        private readonly RunningAggregate _aggregate = new(name, countRows: p.Call.Star, currency: p.Call.Currency);
        private readonly HashSet<QueryExecutor.GroupKey>? _seen = p.Call.Distinct ? [] : null;

        public object? Result => _aggregate.Result;

        public void Add(int position)
        {
            if (!p.Includes(position))
                return;
            if (p.ArgumentCount == 2)
            {
                _aggregate.AddPair(p.Argument(position, 0), p.Argument(position, 1));
                return;
            }
            object? value = p.Call.Star ? null : p.Argument(position, 0);
            if (_seen is not null && (value is null || !_seen.Add(new QueryExecutor.GroupKey([value]))))
                return;
            _aggregate.Add(value);
        }
    }

    /// <summary>
    /// <c>PERCENTILE_CONT(fraction) WITHIN GROUP (ORDER BY key)</c> and <c>PERCENTILE_DISC</c> over each row's frame —
    /// by default the partition up to the row's last peer, or all of it; see <see cref="Percentile"/>. Each row's own
    /// fraction applies.
    /// </summary>
    private static WindowFunctionDef PercentileOf(string name) => new(2, 2,
        typing => Percentile.ResultType(name, typing.ArgumentType(1)),
        (p, o) =>
        {
            FrameRows previous = default;
            for (int i = 0; i < o.Length; i++)
            {
                // Peers under the default frame, and every row of an unordered window, share their frame.
                FrameRows frame = p.Frame(i);
                o[i] = i > 0 && frame == previous && Equals(p.Argument(i, 0), p.Argument(i - 1, 0))
                    ? o[i - 1]
                    : Percentile.Of(name, frame.Positions().Where(p.Includes).Select(k => p.Argument(k, 1)),
                        p.Argument(i, 0), p.Call.WithinGroup![0]);
                previous = frame;
            }
        },
        WindowOptions.Frame | WindowOptions.Filter);

    /// <summary>
    /// <c>LISTAGG(x [, separator]) WITHIN GROUP (ORDER BY …)</c> over each row's frame; see <see cref="ListAgg"/>. The
    /// arguments are the value, the separator when written, and the WITHIN GROUP keys.
    /// </summary>
    private static void ListAggOf(WindowPartition p, object?[] o)
    {
        // STRING_AGG may have no WITHIN GROUP at all, and then lists in window order with no keys of its own.
        IReadOnlyList<Sql.Ast.SortDirection> directions = p.Call.WithinGroup ?? [];
        int keys = directions.Count;
        string separator = p.ArgumentCount - keys == 2 && o.Length > 0 ? (string)p.Argument(0, 1)! : "";
        FrameRows previous = default;
        for (int i = 0; i < o.Length; i++)
        {
            FrameRows frame = p.Frame(i);
            o[i] = i > 0 && frame == previous
                ? o[i - 1]
                : ListAgg.Of(
                    frame.Positions().Where(p.Includes).Select(k =>
                        (p.Argument(k, 0), Enumerable.Range(p.ArgumentCount - keys, keys).Select(a => p.Argument(k, a)).ToArray())),
                    separator, directions, p.Call.Distinct);
            previous = frame;
        }
    }

    /// <summary>Whether a frame edge never moves back along the window order.</summary>
    private static bool Growing(FrameRows[] frames, Func<FrameRows, int> edge)
    {
        for (int i = 1; i < frames.Length; i++)
            if (edge(frames[i]) < edge(frames[i - 1]))
                return false;
        return true;
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
    /// has no such row. Rows are counted one by one, ties or not — under IGNORE NULLS, only those where <c>x</c> is not
    /// Null. Each row's own offset and default apply; a Null offset gives Null, and a negative one is an invalid
    /// procedure call.
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
            long target = p.Step(i, offset, forward);
            o[i] = target >= 0
                ? p.Argument((int)target, 0)
                : p.ArgumentCount > 2 ? p.Argument(i, 2) : null;
        }
    }

    /// <summary>
    /// The first argument's value on the frame's first row or, <paramref name="fromLast"/>, its last; Null for an
    /// empty frame. By default Nulls are respected — a Null on that row is the result; under IGNORE NULLS it is the
    /// first or last row whose value is not Null.
    /// </summary>
    private static void FrameValue(WindowPartition p, object?[] o, bool fromLast)
    {
        for (int i = 0; i < o.Length; i++)
            o[i] = p.Pick(p.Frame(i), 0, fromLast) is var position and >= 0 ? p.Argument(position, 0) : null;
    }

    /// <summary>
    /// <c>NTH_VALUE(x, n) [FROM FIRST | FROM LAST]</c>: <c>x</c> on the frame's <c>n</c>th row, counted from its first
    /// row or its last, or Null while the frame has fewer rows — so over the default frame the rows before the
    /// <c>n</c>th peer group are Null. IGNORE NULLS counts only the rows where <c>x</c> is not Null. Each row's own
    /// <c>n</c> applies, as LAG's offset does; a Null <c>n</c> gives Null, and one below 1 is an invalid procedure
    /// call.
    /// </summary>
    private static void NthValue(WindowPartition p, object?[] o)
    {
        for (int i = 0; i < o.Length; i++)
            o[i] = WholeArgument(p.Argument(i, 1), "NTH_VALUE's row number", least: 1) is { } n
                   && p.Pick(p.Frame(i), n - 1, p.Call.FromLast) is var position and >= 0
                ? p.Argument(position, 0)
                : null;
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
    public static bool IsWindowFunction(string name) =>
        Registry.ContainsKey(name) || RunningAggregate.Supports(name.ToUpperInvariant());

    public static WindowFunctionDef Lookup(string name) =>
        Registry.TryGetValue(name, out WindowFunctionDef? def) ? def
        : RunningAggregate.Supports(name.ToUpperInvariant()) ? Aggregate(name.ToUpperInvariant())
        : throw new NotSupportedException($"Window function '{name}' is not supported.");
}
