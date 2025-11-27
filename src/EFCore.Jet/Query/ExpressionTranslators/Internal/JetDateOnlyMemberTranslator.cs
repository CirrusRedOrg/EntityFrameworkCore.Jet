// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

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
public class JetDateOnlyMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMemberTranslator
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member.DeclaringType != typeof(DateOnly) || instance is null)
        {
            return null;
        }

        return member.Name switch
        {
            nameof(DateOnly.Year) => DatePart("yyyy"),
            nameof(DateOnly.Month) => DatePart("m"),
            nameof(DateOnly.DayOfYear) => DatePart("y"),
            nameof(DateOnly.Day) => DatePart("d"),

            nameof(DateOnly.DayNumber) => sqlExpressionFactory.Add(sqlExpressionFactory.Function(
                "DATEDIFF",
                [
                    sqlExpressionFactory.Constant("d"),
                    sqlExpressionFactory.Constant(new DateOnly(100, 1, 1)),
                    instance
                ],
                nullable: true,
                argumentsPropagateNullability: [false, true, true],
                returnType),sqlExpressionFactory.Constant(new DateOnly(100,1,1).DayNumber)),

            _ => null
        };

        SqlExpression DatePart(string datePart)
            => sqlExpressionFactory.Function(
                "DATEPART",
                [sqlExpressionFactory.Constant(datePart), instance],
                nullable: true,
                argumentsPropagateNullability: [false, true],
                returnType);
    }
}
