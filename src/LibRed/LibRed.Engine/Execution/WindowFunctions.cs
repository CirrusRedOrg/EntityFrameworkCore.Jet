namespace LibRed.Engine.Execution;

/// <summary>
/// One partition of a window's input, its rows already in window order, together with the peer-group
/// information the ranking functions need. A <b>peer group</b> is a run of adjacent rows whose ORDER BY keys
/// compare equal; with no ORDER BY the whole partition is one peer group, which is what makes RANK constant
/// over an unordered window.
/// </summary>
/// <remarks>Positions are indexes into the partition's own window order, not into the input.</remarks>
internal sealed class WindowPartition(
    IReadOnlyList<int> peerStart, IReadOnlyList<int> peerOrdinal, IReadOnlyList<object?[]> arguments)
{
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
}

/// <summary>Fills <paramref name="output"/> — one slot per row of the partition, in window order.</summary>
internal delegate void WindowEvaluator(WindowPartition partition, object?[] output);

/// <param name="ResultType">The declared CLR type of the result, given the declared type of the first argument
/// (null when there is none) — the window counterpart of QueryExecutor.DeclaredFunctionType.</param>
internal sealed record WindowFunctionDef(
    int MinArguments, int MaxArguments, Func<Type?, Type?> ResultType, WindowEvaluator Evaluate);

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
    };

    /// <summary>Whether <paramref name="name"/> names a window function this engine can compute. A call with an
    /// OVER clause that this returns false for is a parse-level window function the engine has no evaluator
    /// for — <see cref="Lookup"/> then reports it by name.</summary>
    public static bool IsWindowFunction(string name) => Registry.ContainsKey(name);

    public static WindowFunctionDef Lookup(string name) =>
        Registry.TryGetValue(name, out WindowFunctionDef? def)
            ? def
            : throw new NotSupportedException($"Window function '{name}' is not supported.");
}
