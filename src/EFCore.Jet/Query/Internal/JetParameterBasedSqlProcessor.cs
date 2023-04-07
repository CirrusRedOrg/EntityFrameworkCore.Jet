// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class JetParameterBasedSqlProcessor : RelationalParameterBasedSqlProcessor
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetParameterBasedSqlProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        bool useRelationalNulls)
        : base(dependencies, useRelationalNulls)
    {
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override SelectExpression Optimize(
        SelectExpression selectExpression,
        IReadOnlyDictionary<string, object> parametersValues,
        out bool canCache)
    {
        var optimizedSelectExpression = base.Optimize(selectExpression, parametersValues, out canCache);

        /*optimizedSelectExpression = new SkipTakeCollapsingExpressionVisitor(Dependencies.SqlExpressionFactory)
            .Process(optimizedSelectExpression, parametersValues, out var canCache2);*/

        //canCache &= canCache2;

        return (SelectExpression)new SearchConditionConvertingExpressionVisitor(Dependencies.SqlExpressionFactory).Visit(optimizedSelectExpression);
    }

    /// <inheritdoc />
    protected override SelectExpression ProcessSqlNullability(
        SelectExpression selectExpression,
        IReadOnlyDictionary<string, object> parametersValues,
        out bool canCache)
    {
        Check.NotNull(selectExpression, nameof(selectExpression));
        Check.NotNull(parametersValues, nameof(parametersValues));

        return new JetSqlNullabilityProcessor(Dependencies, UseRelationalNulls).Process(
            selectExpression, parametersValues, out canCache);
    }
}
