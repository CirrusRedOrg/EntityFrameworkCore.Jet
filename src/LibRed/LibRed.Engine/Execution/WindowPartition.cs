using LibRed.Sql.Ast;
using System.Globalization;

namespace LibRed.Engine.Execution;

/// <summary>
/// A window's frame clause with what it needs from each row: the bounds' offsets, as each row evaluates them, and —
/// for a RANGE offset — the one ORDER BY key and its direction.
/// </summary>
internal sealed record WindowFrameInput(
    WindowFrame Frame, IReadOnlyList<object?> StartOffsets, IReadOnlyList<object?> EndOffsets,
    IReadOnlyList<object?>? RangeKeys = null, bool Descending = false)
{
    /// <summary>The standard's default frame, which needs nothing from the rows.</summary>
    public static readonly WindowFrameInput Default = new(WindowFrame.Default, [], []);
}

/// <summary>How the call is written, beyond its name and arguments.</summary>
/// <param name="Star">The argument is <c>*</c>, as in COUNT(*), which has no values.</param>
/// <param name="Currency">The first argument is a Currency, which the statistical aggregates square exactly.</param>
/// <param name="Distinct">An aggregate over the distinct values in each frame.</param>
/// <param name="IgnoreNulls">IGNORE NULLS: rows whose first argument is Null are not counted.</param>
/// <param name="FromLast">FROM LAST: NTH_VALUE counts from the frame's last row.</param>
/// <param name="WithinGroup">An ordered-set aggregate's ordering: the directions of its last arguments, one each.</param>
internal sealed record WindowCall(
    bool Star = false, bool Currency = false, bool Distinct = false, bool IgnoreNulls = false, bool FromLast = false,
    IReadOnlyList<SortDirection>? WithinGroup = null)
{
    public static readonly WindowCall Plain = new();
}

/// <summary>
/// The rows of one frame, as positions in the partition's window order: <c>[Start, End)</c> less the excluded
/// <c>[ExcludeStart, ExcludeEnd)</c>, which leaves <see cref="Kept"/> in when it is set (EXCLUDE TIES).
/// </summary>
internal readonly record struct FrameRows(int Start, int End, int ExcludeStart = 0, int ExcludeEnd = 0, int Kept = -1)
{
    private int ExcludedFrom => Math.Clamp(ExcludeStart, Start, End);

    private int ExcludedTo => Math.Clamp(ExcludeEnd, ExcludedFrom, End);

    private bool KeepsOne => Kept >= ExcludedFrom && Kept < ExcludedTo;

    /// <summary>Whether a row is left out inside <c>[Start, End)</c>.</summary>
    public bool HasExclusion => ExcludedTo > ExcludedFrom;

    public int Count => End - Start - (ExcludedTo - ExcludedFrom) + (KeepsOne ? 1 : 0);

    /// <summary>The position of the frame's <paramref name="n"/>th row, from 0; -1 when it has no such row.</summary>
    public int Nth(int n)
    {
        if (n < 0 || n >= Count)
            return -1;
        int before = ExcludedFrom - Start;
        if (n < before)
            return Start + n;
        n -= before;
        if (KeepsOne)
        {
            if (n == 0)
                return Kept;
            n--;
        }
        return ExcludedTo + n;
    }

    /// <summary>The frame's rows, in window order.</summary>
    public IEnumerable<int> Positions()
    {
        for (int n = 0, count = Count; n < count; n++)
            yield return Nth(n);
    }

    /// <summary>The frame as at most three runs of adjacent positions, <c>[From, To)</c>, in window order: the rows
    /// before the exclusion, the one it keeps, and the rows after.</summary>
    public IEnumerable<(int From, int To)> Runs()
    {
        if (ExcludedFrom > Start)
            yield return (Start, ExcludedFrom);
        if (KeepsOne)
            yield return (Kept, Kept + 1);
        if (End > ExcludedTo)
            yield return (ExcludedTo, End);
    }
}

/// <summary>
/// One partition of a window's input, its rows already in window order, together with the peer-group
/// information the ranking functions need and the frame each row sees. A <b>peer group</b> is a run of adjacent rows
/// whose ORDER BY keys compare equal; with no ORDER BY the whole partition is one peer group, which is what makes
/// RANK constant over an unordered window.
/// </summary>
/// <remarks>Positions are indexes into the partition's own window order, not into the input.</remarks>
/// <param name="peerStart">Per row, the position the row's peer group starts at.</param>
/// <param name="peerOrdinal">Per row, the zero-based ordinal of the row's peer group within the partition.</param>
/// <param name="arguments">Per row, the already-evaluated arguments of the window call.</param>
/// <param name="call">How the call is written; null for a plain one.</param>
/// <param name="frame">The frame clause; null for the default frame.</param>
/// <param name="included">Which rows an aggregate's FILTER lets in; null when it has none.</param>
internal sealed class WindowPartition(
    IReadOnlyList<int> peerStart, IReadOnlyList<int> peerOrdinal, IReadOnlyList<object?[]> arguments,
    WindowCall? call = null, WindowFrameInput? frame = null, IReadOnlyList<bool>? included = null)
{
    /// <summary>Whether an aggregate takes in the row at <paramref name="position"/>: its FILTER holds there.</summary>
    public bool Includes(int position) => included is null || included[position];

    private readonly WindowFrameInput _frame = frame ?? WindowFrameInput.Default;

    // Where each peer group starts, by ordinal; built on first use by GROUPS and the peer ends.
    private int[]? _groupStarts;

    // The run of rows whose RANGE key is not Null — Null keys sort together at one end of the partition.
    private (int Start, int End)? _keyed;

    // The positions whose first argument is not Null, ascending; built on first use by IGNORE NULLS.
    private int[]? _valued;

    public WindowCall Call { get; } = call ?? WindowCall.Plain;

    /// <summary>Rows in this partition.</summary>
    public int Count => peerStart.Count;

    /// <summary>Position of the first row of the peer group holding <paramref name="position"/>. RANK is this
    /// plus one, which is why ranks skip after a tie.</summary>
    public int PeerStart(int position) => peerStart[position];

    /// <summary>Zero-based ordinal of the peer group holding <paramref name="position"/>, counted from the
    /// start of the partition. DENSE_RANK is this plus one, which is why it does not skip.</summary>
    public int PeerOrdinal(int position) => peerOrdinal[position];

    /// <summary>The position after the last peer of the row at <paramref name="position"/>.</summary>
    public int PeerEnd(int position) => GroupEnd(peerOrdinal[position]);

    /// <summary>The value of the call's <paramref name="argument"/>th argument on the row at
    /// <paramref name="position"/> — for the functions that take one (NTILE, LAG, FIRST_VALUE, …).</summary>
    public object? Argument(int position, int argument) => arguments[position][argument];

    /// <summary>How many arguments the call was given.</summary>
    public int ArgumentCount => arguments.Count == 0 ? 0 : arguments[0].Length;

    /// <summary>
    /// The position of the <paramref name="n"/>th row of <paramref name="frame"/>, from 0, counted from its first row
    /// or, <paramref name="fromLast"/>, its last; -1 when it has no such row. Under IGNORE NULLS only the rows whose
    /// first argument is not Null are counted.
    /// </summary>
    public int Pick(FrameRows frame, long n, bool fromLast)
    {
        var runs = frame.Runs();
        foreach ((int from, int to) in fromLast ? runs.Reverse() : runs)
        {
            int count = Call.IgnoreNulls ? ValuedBefore(to) - ValuedBefore(from) : to - from;
            if (n < count)
            {
                int k = (int)(fromLast ? count - 1 - n : n);
                return Call.IgnoreNulls ? Valued[ValuedBefore(from) + k] : from + k;
            }
            n -= count;
        }
        return -1;
    }

    /// <summary>
    /// The position <paramref name="offset"/> rows before (or, <paramref name="forward"/>, after) the one at
    /// <paramref name="position"/> in the partition; -1 past its edge. Under IGNORE NULLS only the rows whose first
    /// argument is not Null are counted, and offset 0 is still the row itself.
    /// </summary>
    public long Step(int position, long offset, bool forward)
    {
        if (!Call.IgnoreNulls || offset == 0)
        {
            long target = forward ? position + offset : position - offset;
            return target >= 0 && target < Count ? target : -1;
        }
        long index = forward ? ValuedBefore(position + 1) + offset - 1 : ValuedBefore(position) - offset;
        return index >= 0 && index < Valued.Length ? Valued[(int)index] : -1;
    }

    private int[] Valued => _valued ??= Enumerable.Range(0, Count).Where(i => arguments[i][0] is not null).ToArray();

    /// <summary>How many valued positions lie before <paramref name="position"/>.</summary>
    private int ValuedBefore(int position)
    {
        int index = Array.BinarySearch(Valued, position);
        return index >= 0 ? index : ~index;
    }

    /// <summary>The rows of the frame the row at <paramref name="position"/> sees.</summary>
    public FrameRows Frame(int position)
    {
        WindowFrame spec = _frame.Frame;
        int start = Bound(spec.Start, position, _frame.StartOffsets, isStart: true);
        int end = Math.Max(start, Bound(spec.End, position, _frame.EndOffsets, isStart: false));
        return spec.Exclusion switch
        {
            FrameExclusion.CurrentRow => new(start, end, position, position + 1),
            FrameExclusion.Group => new(start, end, PeerStart(position), PeerEnd(position)),
            FrameExclusion.Ties => new(start, end, PeerStart(position), PeerEnd(position), position),
            _ => new(start, end),
        };
    }

    private int[] GroupStarts => _groupStarts ??= Enumerable.Range(0, Count).Where(i => peerStart[i] == i).ToArray();

    private int GroupEnd(int ordinal) => ordinal + 1 < GroupStarts.Length ? GroupStarts[ordinal + 1] : Count;

    /// <summary>
    /// Where a bound puts the frame's first row (<paramref name="isStart"/>) or the position after its last: in
    /// ROWS the offset counts rows; in GROUPS, peer groups, the bound taking in all of its group; in RANGE, the ORDER
    /// BY key's distance from the row's. CURRENT ROW is the row itself in ROWS, and its peer group otherwise.
    /// </summary>
    private int Bound(FrameBound bound, int position, IReadOnlyList<object?> offsets, bool isStart)
    {
        switch (bound.Kind)
        {
            case FrameBoundKind.UnboundedPreceding:
                return 0;
            case FrameBoundKind.UnboundedFollowing:
                return Count;
            case FrameBoundKind.CurrentRow:
                return _frame.Frame.Unit == FrameUnit.Rows
                    ? (isStart ? position : position + 1)
                    : (isStart ? PeerStart(position) : PeerEnd(position));
        }

        bool preceding = bound.Kind == FrameBoundKind.Preceding;
        object? offset = offsets[position];
        if (_frame.Frame.Unit == FrameUnit.Range)
            return RangeBound(position, RangeOffset(offset), preceding, isStart);

        long n = WholeOffset(offset);
        if (_frame.Frame.Unit == FrameUnit.Rows)
        {
            long row = preceding ? position - n : position + n;
            return (int)Math.Clamp(isStart ? row : row + 1, 0, Count);
        }

        long group = preceding ? PeerOrdinal(position) - n : PeerOrdinal(position) + n;
        if (group < 0)
            return 0;
        if (group >= GroupStarts.Length)
            return Count;
        return isStart ? GroupStarts[group] : GroupEnd((int)group);
    }

    /// <summary>
    /// A RANGE offset bound: the first row (<paramref name="isStart"/>), or the row after the last, whose key lies
    /// within <paramref name="offset"/> of the current row's on the bound's side. A row with a Null key has only its
    /// peers — the other Nulls — for an offset bound, and a Null key is never within an offset of a value.
    /// </summary>
    private int RangeBound(int position, object offset, bool preceding, bool isStart)
    {
        IReadOnlyList<object?> keys = _frame.RangeKeys!;
        if (keys[position] is not { } key)
            return isStart ? PeerStart(position) : PeerEnd(position);

        (int low, int high) = _keyed ??= Keyed(keys);
        object here = RangeNumber(key);

        // Over the keyed run, a row's signed distance from this one grows along the window order, so the bound is
        // where it first reaches the offset (a start) or first passes it (an end).
        int lo = low, hi = high;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = CompareDistance(RangeNumber(keys[mid]!), here, offset, preceding);
            if (isStart ? c >= 0 : c > 0)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }

    /// <summary>The signed distance of <paramref name="other"/> from <paramref name="here"/> in window order, compared
    /// with the bound's offset: negative for PRECEDING, positive for FOLLOWING. Exact where no side is a
    /// floating-point number.</summary>
    private int CompareDistance(object other, object here, object offset, bool preceding)
    {
        int direction = _frame.Descending ? -1 : 1;
        if (other is not (double or float) && here is not (double or float) && offset is not (double or float))
        {
            try
            {
                decimal distance = direction * (Convert.ToDecimal(other, CultureInfo.InvariantCulture)
                    - Convert.ToDecimal(here, CultureInfo.InvariantCulture));
                decimal limit = Convert.ToDecimal(offset, CultureInfo.InvariantCulture);
                return distance.CompareTo(preceding ? -limit : limit);
            }
            catch (OverflowException)
            {
                // Past a Decimal: the Double comparison below.
            }
        }
        double d = direction * (Convert.ToDouble(other, CultureInfo.InvariantCulture)
            - Convert.ToDouble(here, CultureInfo.InvariantCulture));
        double l = Convert.ToDouble(offset, CultureInfo.InvariantCulture);
        return d.CompareTo(preceding ? -l : l);
    }

    private (int Start, int End) Keyed(IReadOnlyList<object?> keys)
    {
        int start = 0, end = Count;
        while (start < end && keys[start] is null)
            start++;
        while (end > start && keys[end - 1] is null)
            end--;
        return (start, end);
    }

    /// <summary>A RANGE key as a number: a date as its serial, as dates sort; text and other kinds are a type
    /// mismatch.</summary>
    private static object RangeNumber(object key) => key is string or char or Guid or byte[]? throw new InvalidCastException("Type mismatch: a RANGE offset needs a number or date to order by.")
        : ExpressionEvaluator.ConversionNumber(key);

    /// <summary>A RANGE offset: a number of the key's units — days, for a date — Null and negative refused.</summary>
    private static object RangeOffset(object? offset)
    {
        if (offset is null)
            throw new ArgumentException("Invalid procedure call: a frame offset cannot be Null.");
        object n = ExpressionEvaluator.ConversionNumber(offset);
        return Convert.ToDouble(n, CultureInfo.InvariantCulture) >= 0
            ? n
            : throw new ArgumentException($"Invalid procedure call: a frame offset cannot be {n}.");
    }

    /// <summary>A ROWS or GROUPS offset: a whole number, read as CLng reads it, Null and negative refused.</summary>
    private static long WholeOffset(object? offset)
    {
        if (offset is null)
            throw new ArgumentException("Invalid procedure call: a frame offset cannot be Null.");
        long n = Convert.ToInt64(ExpressionEvaluator.ConversionNumber(offset), CultureInfo.InvariantCulture);
        return n >= 0 ? n : throw new ArgumentException($"Invalid procedure call: a frame offset cannot be {n}.");
    }
}