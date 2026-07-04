using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Evaluates an AST <see cref="Expression"/> against a single row, resolving column
/// references through an <see cref="EvalScope"/> (which chains to outer scopes for
/// correlation). Comparisons coerce numeric operands; SQL nulls propagate (a comparison
/// involving null yields null, treated as "not true" by filters).
/// </summary>
internal sealed class ExpressionEvaluator(
    EvalScope scope,
    IScalarSubqueryRunner subqueries,
    IReadOnlyDictionary<FunctionCall, object?>? aggregates = null,
    ParameterBag? parameters = null)
{
    public object? Evaluate(Expression expression) => expression switch
    {
        LiteralExpression l => l.Value,
        ColumnReference c => scope.TryResolve(c, out object? v) ? v
            : throw new InvalidOperationException($"Column '{EvalScope.Describe(c)}' was not found."),
        ScalarSubquery s => subqueries.ExecuteScalar(s.Query, scope),
        ExistsExpression e => subqueries.ExecuteExists(e.Query, scope),
        FunctionCall f => EvaluateFunction(f),
        UnaryExpression u => EvaluateUnary(u),
        BinaryExpression b => EvaluateBinary(b),
        ParameterExpression p => parameters is not null
            ? parameters.Resolve(p.Name)
            : throw new InvalidOperationException($"No parameters were supplied for '{p.Name}'."),
        _ => throw new NotSupportedException($"Cannot evaluate {expression.GetType().Name}."),
    };

    private object? EvaluateFunction(FunctionCall f)
    {
        // Aggregate calls are precomputed per group and resolved by reference.
        if (aggregates is not null && aggregates.TryGetValue(f, out object? aggregate))
            return aggregate;

        return f.Name.ToUpperInvariant() switch
        {
            "IIF" => IsTrue(f.Arguments[0]) ? Evaluate(f.Arguments[1]) : Evaluate(f.Arguments[2]),
            "DATEPART" => DatePart(Evaluate(f.Arguments[0]), Evaluate(f.Arguments[1])),
            "ROUND" => Round(f),
            "FIX" => UnaryNumeric(f, d => Math.Truncate(d)),     // toward zero
            "INT" => UnaryNumeric(f, d => Math.Floor(d)),        // toward -infinity
            "ABS" => UnaryNumeric(f, Math.Abs),
            "CCUR" => UnaryNumeric(f, d => Math.Round(d, 4)),   // coerce to Currency (decimal, 4 dp)
            // Jet VBA math functions (double precision). SQR = sqrt, ATN = atan, SGN = sign, LOG =
            // natural log. Acos/Asin/Atan2/Floor/Ceiling/Log10/Log-base are emitted by EF as
            // expressions built from these plus arithmetic, so they need no dedicated cases.
            "SIN" => UnaryDouble(f, Math.Sin),
            "COS" => UnaryDouble(f, Math.Cos),
            "TAN" => UnaryDouble(f, Math.Tan),
            "ATN" => UnaryDouble(f, Math.Atan),
            "EXP" => UnaryDouble(f, Math.Exp),
            "LOG" => UnaryDouble(f, Math.Log),
            "SQR" => UnaryDouble(f, Math.Sqrt),
            "SGN" => UnaryDouble(f, d => Math.Sign(d)),
            _ => throw new NotSupportedException($"Function {f.Name} is not supported."),
        };
    }

    /// <summary>Access ROUND(number[, digits]): banker's rounding, like VBA/Access.</summary>
    private object? Round(FunctionCall f)
    {
        object? value = Evaluate(f.Arguments[0]);
        if (value is null) return null;
        int digits = f.Arguments.Count > 1
            ? Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture)
            : 0;
        return Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), digits, MidpointRounding.ToEven);
    }

    /// <summary>Applies a numeric transform to a single argument, propagating NULL.</summary>
    private object? UnaryNumeric(FunctionCall f, Func<decimal, decimal> op)
    {
        object? value = Evaluate(f.Arguments[0]);
        return value is null ? null : op(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
    }

    /// <summary>Applies a double-precision transform to a single argument, propagating NULL. Used for
    /// the trig/exp/log/sqrt VBA functions, which are inherently floating-point.</summary>
    private object? UnaryDouble(FunctionCall f, Func<double, double> op)
    {
        object? value = Evaluate(f.Arguments[0]);
        return value is null ? null : op(Convert.ToDouble(value, CultureInfo.InvariantCulture));
    }

    /// <summary>Access DATEPART(interval, date): extracts a component of a date as an int.</summary>
    private static object? DatePart(object? interval, object? date)
    {
        if (date is null) return null;
        var d = Convert.ToDateTime(date, CultureInfo.InvariantCulture);
        return (interval?.ToString() ?? "").ToLowerInvariant() switch
        {
            "yyyy" => d.Year,
            "q" => (d.Month + 2) / 3,
            "m" => d.Month,
            "y" => d.DayOfYear,
            "d" => d.Day,
            "w" => (int)d.DayOfWeek + 1,
            "ww" => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstDay, DayOfWeek.Sunday),
            "h" => d.Hour,
            "n" => d.Minute,
            "s" => d.Second,
            _ => throw new NotSupportedException($"DATEPART interval '{interval}' is not supported."),
        };
    }

    public bool IsTrue(Expression expression) => Evaluate(expression) is true;

    private object? EvaluateUnary(UnaryExpression u)
    {
        object? v = Evaluate(u.Operand);
        return u.Operator switch
        {
            UnaryOperator.Not => v is bool b ? !b : null,
            UnaryOperator.Negate => v is null ? null : -Convert.ToDecimal(v, CultureInfo.InvariantCulture),
            UnaryOperator.IsNull => v is null,
            UnaryOperator.IsNotNull => v is not null,
            _ => throw new NotSupportedException($"Unary operator {u.Operator}."),
        };
    }

    private object? EvaluateBinary(BinaryExpression b)
    {
        if (b.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            bool? l = AsBool(Evaluate(b.Left));
            bool? r = AsBool(Evaluate(b.Right));
            return b.Operator == BinaryOperator.And ? (l & r) : (l | r);
        }

        object? left = Evaluate(b.Left);
        object? right = Evaluate(b.Right);

        if (b.Operator == BinaryOperator.Concat)
            return (left?.ToString() ?? "") + (right?.ToString() ?? "");

        if (left is null || right is null)
            return null;

        return b.Operator switch
        {
            BinaryOperator.Equal => Compare(left, right) == 0,
            BinaryOperator.NotEqual => Compare(left, right) != 0,
            BinaryOperator.LessThan => Compare(left, right) < 0,
            BinaryOperator.LessThanOrEqual => Compare(left, right) <= 0,
            BinaryOperator.GreaterThan => Compare(left, right) > 0,
            BinaryOperator.GreaterThanOrEqual => Compare(left, right) >= 0,
            BinaryOperator.Like => Like(left.ToString()!, right.ToString()!),
            // Access '+' concatenates when either operand is text (but, unlike '&', null already propagated above).
            BinaryOperator.Add => left is string || right is string ? left.ToString() + right.ToString() : Arithmetic(left, right, (a, c) => a + c),
            BinaryOperator.Subtract => Arithmetic(left, right, (a, c) => a - c),
            BinaryOperator.Multiply => Arithmetic(left, right, (a, c) => a * c),
            BinaryOperator.Divide => Arithmetic(left, right, (a, c) => a / c),
            BinaryOperator.Modulo => Convert.ToInt64(left, CultureInfo.InvariantCulture) % Convert.ToInt64(right, CultureInfo.InvariantCulture),
            BinaryOperator.IntDivide => Convert.ToInt64(left, CultureInfo.InvariantCulture) / Convert.ToInt64(right, CultureInfo.InvariantCulture),
            BinaryOperator.Power => Math.Pow(Convert.ToDouble(left, CultureInfo.InvariantCulture), Convert.ToDouble(right, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException($"Binary operator {b.Operator}."),
        };
    }

    /// <summary>SQL LIKE: '%'/'*' match any run, '_'/'?' match one char; case-insensitive.</summary>
    private static bool Like(string value, string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char ch in pattern)
            sb.Append(ch switch
            {
                '%' or '*' => ".*",
                '_' or '?' => ".",
                _ => Regex.Escape(ch.ToString()),
            });
        sb.Append('$');
        return Regex.IsMatch(value, sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static bool? AsBool(object? v) => v switch { bool b => b, null => null, _ => Convert.ToBoolean(v) };

    private static object Arithmetic(object left, object right, Func<decimal, decimal, decimal> op) =>
        op(ToNumber(left), ToNumber(right));

    /// <summary>Coerces a value to a decimal for numeric ops, using Jet's boolean convention
    /// (true = -1, false = 0) so a bool matches the numeric column it is stored in (see the encoder).</summary>
    private static decimal ToNumber(object v) =>
        v is bool b ? (b ? -1 : 0) : Convert.ToDecimal(v, CultureInfo.InvariantCulture);

    private static int Compare(object left, object right)
    {
        if (IsNumeric(left) && IsNumeric(right))
            return ToNumber(left).CompareTo(ToNumber(right));

        if (left is string || right is string)
            return string.CompareOrdinal(left.ToString(), right.ToString());

        if (left is IComparable c && left.GetType() == right.GetType())
            return c.CompareTo(right);

        return string.CompareOrdinal(left.ToString(), right.ToString());
    }

    /// <summary>Orders two values for SORT (nulls first), using the same coercion as comparisons.</summary>
    public static int CompareForSort(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        _ => Compare(a, b),
    };

    // Booleans count as numeric for comparison: EF maps CLR bool to a numeric (smallint) column, and
    // a boolean predicate (e.g. IS NOT NULL) must compare equal to that stored value. ToNumber uses
    // Jet's convention (false = 0, true = -1) so a bool matches the numeric value it is stored as.
    private static bool IsNumeric(object v) =>
        v is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
