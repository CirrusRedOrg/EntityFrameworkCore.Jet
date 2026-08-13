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
        var optimizedQueryExpression = new JetZeroLimitConverter(Dependencies.SqlExpressionFactory)
            .Process(queryExpression, parametersDecorator);

        optimizedQueryExpression = new JetDateTimeRangeConverter(Dependencies.SqlExpressionFactory)
            .Process(optimizedQueryExpression, parametersDecorator);

        var afterBaseProcessing = base.Process(optimizedQueryExpression, parametersDecorator);

        var afterSearchConditionConversion = afterBaseProcessing;/*new SearchConditionConverter(Dependencies.SqlExpressionFactory)
            .Visit(afterBaseProcessing);

        */

        // Guard row-independent projections inside LEFT JOIN subqueries. This must run AFTER base.Process: the
        // guard is CASE WHEN <anchor> IS NULL THEN NULL ELSE <literal> END, and the anchor is non-nullable within
        // the subquery, so SqlNullabilityProcessor proves the test false and folds the CASE straight back to its
        // ELSE branch. Applied here it survives to SQL generation.
        afterSearchConditionConversion = new JetOuterJoinProjectionGuardExpressionVisitor(
            Dependencies.SqlExpressionFactory).Visit(afterSearchConditionConversion);

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
