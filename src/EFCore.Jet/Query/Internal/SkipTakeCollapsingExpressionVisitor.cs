using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public class SkipTakeCollapsingExpressionVisitor : ExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    private ParametersCacheDecorator _parametersDecorator;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public SkipTakeCollapsingExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _parametersDecorator = null!;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
    {
        _parametersDecorator = parametersDecorator;

        return Visit(queryExpression);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitExtension(Expression extensionExpression)
    {
        if (extensionExpression is SelectExpression selectExpression)
        {
            if (IsZero(selectExpression.Limit))
            {
                var result = selectExpression.Update(
                    selectExpression.Tables,
                    selectExpression.GroupBy.Count > 0 ? selectExpression.Predicate : _sqlExpressionFactory.Constant(false),
                    selectExpression.GroupBy,
                    selectExpression.GroupBy.Count > 0 ? _sqlExpressionFactory.Constant(false) : null,
                    selectExpression.Projection,
                    [],
                    limit: null,
                    offset: null);
                return base.VisitExtension(result);
            }

            bool IsZero(SqlExpression? sqlExpression)
            {
                switch (sqlExpression)
                {
                    case SqlConstantExpression { Value: int intValue }:
                        return intValue == 0;
                    case SqlParameterExpression parameter:
                        return _parametersDecorator.GetAndDisableCaching()[parameter.Name] is 0;
                    case SqlBinaryExpression { Left: SqlConstantExpression left, Right: SqlConstantExpression right }:
                        return left.Value is int leftValue && right.Value is int rightValue && leftValue + rightValue == 0;
                    case SqlBinaryExpression { Left: SqlParameterExpression left, Right: SqlConstantExpression right }:
                        return _parametersDecorator.GetAndDisableCaching()[left.Name] is 0 && right.Value is int and 0;
                    case SqlBinaryExpression { Left: SqlConstantExpression left, Right: SqlParameterExpression right }:
                        return _parametersDecorator.GetAndDisableCaching()[right.Name] is 0 && left.Value is int and 0;
                    case SqlBinaryExpression { Left: SqlParameterExpression left, Right: SqlParameterExpression right }:
                        return _parametersDecorator.GetAndDisableCaching()[left.Name] is 0 && _parametersDecorator.GetAndDisableCaching()[right.Name] is 0;
                    default:
                        return false;
                }
            }
        }

        return base.VisitExtension(extensionExpression);
    }
}
