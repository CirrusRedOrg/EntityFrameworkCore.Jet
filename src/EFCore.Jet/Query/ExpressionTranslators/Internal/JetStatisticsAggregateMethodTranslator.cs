﻿using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;

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
public class JetStatisticsAggregateMethodTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource) : IAggregateMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory;
    private readonly RelationalTypeMapping _doubleTypeMapping = typeMappingSource.FindMapping(typeof(double))!;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // Docs: https://docs.microsoft.com/sql/t-sql/functions/aggregate-functions-transact-sql

        if (method.DeclaringType != typeof(JetDbFunctionsExtensions)
            || source.Selector is not SqlExpression sqlExpression)
        {
            return null;
        }

        var functionName = method.Name switch
        {
            nameof(JetDbFunctionsExtensions.StandardDeviationSample) => "StDev",
            nameof(JetDbFunctionsExtensions.StandardDeviationPopulation) => "StDevP",
            nameof(JetDbFunctionsExtensions.VarianceSample) => "Var",
            nameof(JetDbFunctionsExtensions.VariancePopulation) => "VarP",
            _ => null
        };

        if (functionName is null)
        {
            return null;
        }

        return _sqlExpressionFactory.Function(functionName, [sqlExpression], nullable: true, argumentsPropagateNullability: [false], typeof(double), _doubleTypeMapping);
    }
}
