using System.Globalization;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Evaluates an AST <see cref="Expression"/> against a single row, resolving column
/// references through an ordinal lookup. Comparisons coerce numeric operands; SQL nulls
/// propagate (a comparison involving null yields null, treated as "not true" by filters).
/// </summary>
internal sealed class ExpressionEvaluator(Func<ColumnReference, int> resolveColumn, object?[] row)
{
    public object? Evaluate(Expression expression) => expression switch
    {
        LiteralExpression l => l.Value,
        ColumnReference c => row[resolveColumn(c)],
        UnaryExpression u => EvaluateUnary(u),
        BinaryExpression b => EvaluateBinary(b),
        ParameterExpression => throw new NotSupportedException("Query parameters are not yet supported."),
        _ => throw new NotSupportedException($"Cannot evaluate {expression.GetType().Name}."),
    };

    /// <summary>True iff the expression evaluates to boolean true (null/other → false).</summary>
    public bool IsTrue(Expression expression) => Evaluate(expression) is true;

    private object? EvaluateUnary(UnaryExpression u)
    {
        object? v = Evaluate(u.Operand);
        return u.Operator switch
        {
            UnaryOperator.Not => v is bool b ? !b : null,
            UnaryOperator.Negate => Negate(v),
            UnaryOperator.IsNull => v is null,
            UnaryOperator.IsNotNull => v is not null,
            _ => throw new NotSupportedException($"Unary operator {u.Operator}."),
        };
    }

    private object? EvaluateBinary(BinaryExpression b)
    {
        // Logical operators first (they define their own null handling).
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
            return null; // arithmetic/comparison with null is null

        return b.Operator switch
        {
            BinaryOperator.Equal => Compare(left, right) == 0,
            BinaryOperator.NotEqual => Compare(left, right) != 0,
            BinaryOperator.LessThan => Compare(left, right) < 0,
            BinaryOperator.LessThanOrEqual => Compare(left, right) <= 0,
            BinaryOperator.GreaterThan => Compare(left, right) > 0,
            BinaryOperator.GreaterThanOrEqual => Compare(left, right) >= 0,
            BinaryOperator.Add => Arithmetic(left, right, (a, c) => a + c),
            BinaryOperator.Subtract => Arithmetic(left, right, (a, c) => a - c),
            BinaryOperator.Multiply => Arithmetic(left, right, (a, c) => a * c),
            BinaryOperator.Divide => Arithmetic(left, right, (a, c) => a / c),
            BinaryOperator.Modulo => Convert.ToInt64(left, CultureInfo.InvariantCulture) % Convert.ToInt64(right, CultureInfo.InvariantCulture),
            BinaryOperator.IntDivide => Convert.ToInt64(left, CultureInfo.InvariantCulture) / Convert.ToInt64(right, CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"Binary operator {b.Operator}."),
        };
    }

    /// <summary>Orders two values for SORT (nulls first), using the same numeric/string coercion as comparisons.</summary>
    public static int CompareForSort(object? a, object? b) =>
        (a, b) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            _ => Compare(a, b),
        };

    private static bool? AsBool(object? v) => v switch { bool b => b, null => null, _ => Convert.ToBoolean(v) };

    private static object Negate(object? v) => v is null ? throw new InvalidOperationException() : -Convert.ToDecimal(v, CultureInfo.InvariantCulture);

    private static object Arithmetic(object left, object right, Func<decimal, decimal, decimal> op) =>
        op(Convert.ToDecimal(left, CultureInfo.InvariantCulture), Convert.ToDecimal(right, CultureInfo.InvariantCulture));

    private static int Compare(object left, object right)
    {
        if (IsNumeric(left) && IsNumeric(right))
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));

        if (left is string || right is string)
            return string.CompareOrdinal(left.ToString(), right.ToString());

        if (left is IComparable c && left.GetType() == right.GetType())
            return c.CompareTo(right);

        return string.CompareOrdinal(left.ToString(), right.ToString());
    }

    private static bool IsNumeric(object v) =>
        v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
