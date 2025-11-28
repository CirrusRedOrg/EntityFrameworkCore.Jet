using EntityFrameworkCore.Jet.Storage.Internal;
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
public class JetZeroLimitConverter : ExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    private ParametersCacheDecorator _parametersDecorator;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetZeroLimitConverter(ISqlExpressionFactory sqlExpressionFactory)
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
                    selectExpression.GroupBy.Count > 0 ? selectExpression.Predicate : _sqlExpressionFactory.Constant(false, JetBoolTypeMapping.Default),
                    selectExpression.GroupBy,
                    selectExpression.GroupBy.Count > 0 ? _sqlExpressionFactory.Constant(false, JetBoolTypeMapping.Default) : null,
                    selectExpression.Projection,
                    [],
                    limit: null,
                    offset: null);
                return base.VisitExtension(result);
            }

            bool IsZero(SqlExpression? sqlExpression)
            {
                return sqlExpression switch
                {
                    SqlConstantExpression { Value: int intValue } => intValue == 0,
                    SqlParameterExpression parameter => _parametersDecorator.GetAndDisableCaching()[parameter.Name] is 0,
                    SqlBinaryExpression { Left: SqlConstantExpression left, Right: SqlConstantExpression right } => left.Value is int leftValue && right.Value is int rightValue && leftValue + rightValue == 0,
                    SqlBinaryExpression { Left: SqlParameterExpression left, Right: SqlConstantExpression right } => _parametersDecorator.GetAndDisableCaching()[left.Name] is 0 && right.Value is int and 0,
                    SqlBinaryExpression { Left: SqlConstantExpression left, Right: SqlParameterExpression right } => _parametersDecorator.GetAndDisableCaching()[right.Name] is 0 && left.Value is int and 0,
                    SqlBinaryExpression { Left: SqlParameterExpression left, Right: SqlParameterExpression right } => _parametersDecorator.GetAndDisableCaching()[left.Name] is 0 && _parametersDecorator.GetAndDisableCaching()[right.Name] is 0,
                    _ => false,
                };
            }
        }

        return base.VisitExtension(extensionExpression);
    }
}
