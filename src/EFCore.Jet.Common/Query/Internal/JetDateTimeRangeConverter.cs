using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     Brings DateTime operands that fall outside Jet's representable date range into it.
/// </summary>
/// <remarks>
///     <para>
///         <c>default(DateTime)</c> (0001-01-01, Ticks 0) is below Jet's date floor of 0100-01-01, so it has no
///         on-disk representation. The DateTime type mapping substitutes the OLE epoch (1899-12-30, OA 0) for it,
///         which is the right sentinel for storage and equality — a stored default round-trips through it — but
///         wrong for ordering: it places "the lowest possible value" in the *middle* of the range, so every
///         genuine pre-1899 date compares as if it were below the minimum. <c>WHERE d &gt; default</c> then
///         silently drops rows from years 100-1899.
///     </para>
///     <para>
///         Ordering comparisons don't need the round-trip sentinel — they need an operand that really is the
///         bottom of the range — so substitute 0100-01-01 (the same floor
///         <c>JetSqlTranslatingExpressionVisitor</c> uses for a COALESCE default). Equality is deliberately left
///         alone: <c>d == default</c> must keep matching the epoch a default was stored as.
///     </para>
///     <para>
///         This has to run in <c>JetParameterBasedSqlProcessor</c> rather than in the compile-time
///         <c>JetDateTimeExpressionVisitor</c>, because the value normally arrives as a parameter and query
///         compilation is cached across invocations, so no value is available there. Nor can it be deferred to
///         a value-independent clamp in the emitted SQL: by then the DateTime type mapping has already replaced
///         0001-01-01 with the epoch, so the clamp would test the substituted value and never fire.
///     </para>
/// </remarks>
public class JetDateTimeRangeConverter(ISqlExpressionFactory sqlExpressionFactory) : ExpressionVisitor
{
    /// <summary>The lowest date Jet/ACE can represent; dates run 0100-01-01 to 9999-12-31.</summary>
    private static readonly DateTime JetMinDate = new(100, 1, 1);

    private ParametersCacheDecorator _parametersDecorator = null!;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release.
    /// </summary>
    public virtual Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
    {
        _parametersDecorator = parametersDecorator;

        return Visit(queryExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression extensionExpression)
    {
        if (extensionExpression is SqlBinaryExpression
            {
                OperatorType: ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual
                or ExpressionType.LessThan
                or ExpressionType.LessThanOrEqual
            } binary)
        {
            var left = Replace(binary.Left);
            var right = Replace(binary.Right);

            if (!ReferenceEquals(left, binary.Left) || !ReferenceEquals(right, binary.Right))
            {
                return base.VisitExtension(
                    new SqlBinaryExpression(binary.OperatorType, left, right, binary.Type, binary.TypeMapping));
            }
        }

        return base.VisitExtension(extensionExpression);
    }

    /// <summary>Swaps an operand that is the CLR minimum DateTime for Jet's minimum.</summary>
    private SqlExpression Replace(SqlExpression operand)
    {
        // Only look up a parameter's value when the operand could actually be one of ours: resolving it
        // disables query caching for this shape, so don't pay that on unrelated comparisons.
        var value = operand switch
        {
            SqlConstantExpression { Value: DateTime { Ticks: 0 } } => (object?)default(DateTime),
            SqlParameterExpression { Type: var t } p when t == typeof(DateTime) || t == typeof(DateTimeOffset)
                => _parametersDecorator.GetAndDisableCaching()[p.Name],
            _ => null,
        };

        return value switch
        {
            DateTime { Ticks: 0 } => sqlExpressionFactory.Constant(JetMinDate, operand.TypeMapping),
            DateTimeOffset { Ticks: 0 } => sqlExpressionFactory.Constant(JetMinDate, operand.TypeMapping),
            _ => operand,
        };
    }
}
