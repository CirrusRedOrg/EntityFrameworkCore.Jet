// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.Utilities;

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
public class JetParameterBasedSqlProcessor(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
    RelationalParameterBasedSqlProcessorParameters parameters) : RelationalParameterBasedSqlProcessor(dependencies, parameters)
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
    {
        var optimizedQueryExpression = new SkipTakeCollapsingExpressionVisitor(Dependencies.SqlExpressionFactory)
            .Process(queryExpression, parametersDecorator);

        var afterBaseProcessing = base.Process(optimizedQueryExpression, parametersDecorator);

        var afterSearchConditionConversion = afterBaseProcessing;/*new SearchConditionConverter(Dependencies.SqlExpressionFactory)
            .Visit(afterBaseProcessing);

        */

        // Run the compatibility checks as late in the query pipeline (before the actual SQL translation happens) as reasonable.
        afterSearchConditionConversion = new JetCompatibilityExpressionVisitor().Visit(afterSearchConditionConversion);

        return afterSearchConditionConversion;
    }

    /// <inheritdoc />
    protected override Expression ProcessSqlNullability(Expression selectExpression, ParametersCacheDecorator Decorator)
    {
        return new JetSqlNullabilityProcessor(Dependencies, Parameters).Process(
            selectExpression, Decorator);
    }
}
