using System.Globalization;
using EntityFrameworkCore.Jet.Data;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// The standard's inverse distribution functions, <c>PERCENTILE_CONT</c> and <c>PERCENTILE_DISC</c>: the value at a
/// fraction of the way through the ordered non-Null values of a group or a window frame. Access has neither; this is a
/// LibRed extension.
/// </summary>
internal static class Percentile
{
    /// <summary>The declared type: PERCENTILE_DISC returns one of the values, so has the ORDER BY key's type;
    /// PERCENTILE_CONT interpolates, giving a Double, or a date for dates.</summary>
    public static Type? ResultType(string name, Type? key) =>
        name.Equals("PERCENTILE_DISC", StringComparison.OrdinalIgnoreCase) ? key
        : key == typeof(DateTime) ? typeof(DateTime)
        : typeof(double);

    /// <summary>
    /// <paramref name="name"/> (upper case) over <paramref name="values"/> — the group's or frame's ORDER BY key values,
    /// Nulls included, in any order — at <paramref name="fraction"/>. Null when there are no values or the fraction is
    /// Null; a fraction outside 0 to 1 is an invalid procedure call.
    /// </summary>
    /// <remarks>
    /// PERCENTILE_DISC is the first value, in the given order, whose cumulative share of the values reaches the
    /// fraction. PERCENTILE_CONT is at position <c>fraction × (n − 1)</c>, interpolating linearly between the values
    /// either side of it; text cannot be interpolated. The fraction is read as the Decimal it was written as, so
    /// that <c>0.7</c> of 10 values is the 7th rather than the 8th, as a Double's <c>7.000000000000001</c> would make it.
    /// </remarks>
    public static object? Of(string name, IEnumerable<object?> values, object? fraction, SortDirection direction)
    {
        if (fraction is null)
            return null;
        decimal p = JetDecimalConverter.ToDecimal(ExpressionEvaluator.ConversionNumber(fraction), CultureInfo.InvariantCulture);
        if (p is < 0 or > 1)
            throw new ArgumentException($"Invalid procedure call: a percentile fraction must be from 0 to 1, not {p}.");

        // A stable sort, so values that compare equal but differ — 'a' and 'A' — keep their order.
        var comparer = Comparer<object?>.Create(ExpressionEvaluator.CompareForSort);
        var present = values.Where(v => v is not null);
        List<object?> sorted = direction == SortDirection.Descending
            ? [.. present.OrderByDescending(v => v, comparer)]
            : [.. present.OrderBy(v => v, comparer)];
        int n = sorted.Count;
        if (n == 0)
            return null;

        if (name == "PERCENTILE_DISC")
            return sorted[Math.Max((int)Math.Ceiling(p * n), 1) - 1];

        decimal position = p * (n - 1);
        int below = (int)decimal.Floor(position);
        int above = (int)decimal.Ceiling(position);
        double share = (double)(position - below);
        object low = sorted[below]!, high = sorted[above]!;
        if (low is DateTime from && high is DateTime to)
        {
            // Along the timeline, to the whole second — dates carry no less.
            long ticks = from.Ticks + (long)Math.Round((to.Ticks - from.Ticks) * share);
            const long second = TimeSpan.TicksPerSecond;
            return new DateTime((ticks + second / 2) / second * second);
        }
        double a = Number(low), b = Number(high);
        return below == above ? a : a + (b - a) * share;
    }

    private static double Number(object value) => value is string or char or Guid or byte[]
        ? throw new InvalidCastException("Type mismatch: PERCENTILE_CONT interpolates numbers and dates.")
        : Convert.ToDouble(ExpressionEvaluator.ConversionNumber(value), CultureInfo.InvariantCulture);
}
