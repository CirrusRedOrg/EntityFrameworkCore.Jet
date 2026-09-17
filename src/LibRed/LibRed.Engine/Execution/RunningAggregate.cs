using System.Globalization;
using EntityFrameworkCore.Jet.Data;

namespace LibRed.Engine.Execution;

/// <summary>
/// An aggregate fed its argument's values one at a time, whose result can be read after any of them — the arithmetic
/// behind both GROUP BY's aggregates and the windowed ones, so the two give the same values. Null values are skipped,
/// except where COUNT(*) counts rows.
/// </summary>
/// <remarks>
/// <para>COUNT is an Access Long Integer (32-bit) — EF reads it with GetInt32, so it is an int, not a long.</para>
/// <para>The numeric aggregates read each value as the conversion functions read it (verified vs ACE): text as a
/// number (text that is not one is a type mismatch), a date as its serial, True as -1; a GUID or binary value is a
/// type mismatch. SUM keeps the type of the first value, as LINQ's <c>Sum</c> overloads do — the EF provider emits a
/// bare SUM and reads it by the LINQ operand type: Boolean, Byte, Integer and Long sum to Int32, Int64 to Int64, Single
/// to Single, Double (text and dates included) to Double, Decimal and Currency to Decimal. AVG is Double unless the
/// first value is a Decimal or Currency, and a Decimal average keeps its full precision, as LINQ's does; ACE rounds a
/// Currency average to four places and cuts a Decimal one to ten.</para>
/// <para>MIN and MAX keep the value and its type, in the sort order. Empty text wins against any other text but then
/// counts as no value, so the next value replaces it, and empty text left at the end is Null (verified vs ACE: Min of
/// '5', 'x', '', '7' is '7', and Min of '5', '' is Null).</para>
/// <para>The statistical aggregates are verified vs ACE to the last bit. VAR and STDEV are the sample forms, Null for
/// a single value; VARP and STDEVP the population forms; the STDEVs are the square roots. ACE works them out as
/// (n·Σx² − (Σx)²) / (n·(n−1)), or / n² for the population, in doubles; a Single is squared in single precision, and
/// a Currency's square and squared sum are Currency products, rounded to four places.</para>
/// </remarks>
internal sealed class RunningAggregate
{
    private readonly string _name;
    private readonly bool _countRows;
    private readonly bool _currency;

    private int _count;
    private object? _extreme;
    private object? _first;
    private decimal _decimalSum;
    private double _doubleSum;
    private long _longSum;
    private int _intSum;
    private double _squares;
    private decimal _exactSum;
    private decimal _exactSquares;
    private bool _allDecimal = true;

    /// <param name="name">The aggregate, in upper case.</param>
    /// <param name="countRows">Whether this is COUNT(*), which counts rows rather than values.</param>
    /// <param name="currency">Whether the argument is a Currency, which the statistical aggregates square
    /// exactly.</param>
    public RunningAggregate(string name, bool countRows, bool currency)
    {
        _name = Supports(name) ? name : throw new NotSupportedException($"Aggregate {name} is not supported.");
        _countRows = countRows;
        _currency = currency;
    }

    /// <summary>Whether <paramref name="name"/> (upper case) is an aggregate this computes.</summary>
    public static bool Supports(string name) =>
        name is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or "VAR" or "VARP" or "STDEV" or "STDEVP" or "STDDEV" or "STDDEVP";

    private static bool IsStatistic(string name) => name is not ("COUNT" or "SUM" or "AVG" or "MIN" or "MAX");

    public void Add(object? value)
    {
        if (_countRows)
        {
            _count++;
            return;
        }
        if (value is null)
            return;
        _count++;

        if (_name is "COUNT")
            return;
        if (_name is "MIN" or "MAX")
        {
            if (_extreme is null or string { Length: 0 }
                || (_name == "MAX"
                    ? ExpressionEvaluator.CompareForSort(value, _extreme) > 0
                    : ExpressionEvaluator.CompareForSort(value, _extreme) < 0))
                _extreme = value;
            return;
        }

        object number = ExpressionEvaluator.ConversionNumber(value);
        _first ??= number;
        CultureInfo invariant = CultureInfo.InvariantCulture;
        if (IsStatistic(_name))
        {
            if (number is not decimal)
                _allDecimal = false;
            else if (_currency)
            {
                decimal m = (decimal)number;
                _exactSum += m;
                _exactSquares += decimal.Round(m * m, 4);
            }

            if (number is float f)
            {
                _doubleSum += f;
                _squares += f * f;
            }
            else
            {
                double d = Convert.ToDouble(number, invariant);
                _doubleSum += d;
                _squares += d * d;
            }
            return;
        }

        switch (_first)
        {
            case decimal:
                _decimalSum += JetDecimalConverter.ToDecimal(number, invariant);
                break;
            case long or ulong when _name == "SUM":
                _longSum = checked(_longSum + Convert.ToInt64(number, invariant));
                break;
            case double or float:
            case not null when _name == "AVG":
                _doubleSum += Convert.ToDouble(number, invariant);
                break;
            default:
                _intSum = checked(_intSum + Convert.ToInt32(number, invariant));
                break;
        }
    }

    /// <summary>The aggregate of the values added so far.</summary>
    public object? Result => _name switch
    {
        "COUNT" => _count,
        "MIN" or "MAX" => _extreme is string { Length: 0 } ? null : _extreme,
        _ when _count == 0 => null,
        "SUM" => _first switch
        {
            decimal => _decimalSum,
            double => _doubleSum,
            float => (float)_doubleSum,
            long or ulong => _longSum,
            _ => _intSum,
        },
        "AVG" => _first is decimal ? _decimalSum / _count : _doubleSum / _count,
        _ => Statistic(),
    };

    private object? Statistic()
    {
        bool sample = !_name.EndsWith('P');
        if (sample && _count < 2)
            return null;

        (double squares, double squaredSum) = _currency && _allDecimal
            ? ((double)_exactSquares, (double)decimal.Round(_exactSum * _exactSum, 4))
            : (_squares, _doubleSum * _doubleSum);
        double n = _count;
        double variance = (n * squares - squaredSum) / (sample ? n * (n - 1) : n * n);
        return _name.Contains("DEV", StringComparison.Ordinal) ? Math.Sqrt(variance) : variance;
    }
}
