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
public class JetDateOnlyMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
{
    private readonly Dictionary<MethodInfo, string> _methodInfoDatePartMapping = new()
    {
        { typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddYears), [typeof(int)])!, "yyyy" },
        { typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddMonths), [typeof(int)])!, "m" },
        { typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddDays), [typeof(int)])!, "d" }
    };

    private static readonly MethodInfo ToDateTimeMethodInfo
        = typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.ToDateTime), [typeof(TimeOnly)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance != null)
        {
            if (method == ToDateTimeMethodInfo)
            {
                var timeOnly = arguments[0];

                // We need to refrain from doing the translation when either the DateOnly or the TimeOnly
                // are a complex SQL expression (anything other than a column/constant/parameter), to avoid evaluating them multiple
                // potentially expensive arbitrary expressions multiple times.
                if (instance is not ColumnExpression and not SqlParameterExpression and not SqlConstantExpression
                    || timeOnly is not ColumnExpression and not SqlParameterExpression and not SqlConstantExpression)
                {
                    return null;
                }

                return _sqlExpressionFactory.Function(
                    "DATETIME2FROMPARTS",
                    [
                        MapDatePartExpression("yyyy", instance),
                        MapDatePartExpression("m", instance),
                        MapDatePartExpression("d", instance),
                        MapDatePartExpression("h", timeOnly),
                        MapDatePartExpression("n", timeOnly),
                        MapDatePartExpression("s", timeOnly),
                        MapDatePartExpression("fraction", timeOnly),
                        _sqlExpressionFactory.Constant(7, typeof(int)),
                    ],
                    nullable: true,
                    argumentsPropagateNullability: [true, true, true, true, true, true, true, false],
                    typeof(DateTime));
            }

            if (_methodInfoDatePartMapping.TryGetValue(method, out var datePart))
            {
                instance = _sqlExpressionFactory.ApplyDefaultTypeMapping(instance);

                return _sqlExpressionFactory.Function(
                    "DATEADD",
                    [_sqlExpressionFactory.Constant(datePart), _sqlExpressionFactory.Convert(arguments[0], typeof(int)), instance],
                    nullable: true,
                    argumentsPropagateNullability: [false, false, true],
                    instance.Type,
                    instance.TypeMapping);
            }
        }
            

        if (method.DeclaringType == typeof(DateOnly)
            && method.Name == nameof(DateOnly.FromDateTime)
            && arguments.Count == 1)
        {
            return _sqlExpressionFactory.Function(
                "DATEVALUE",
                [arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [false],
                method.ReturnType);
        }

        return null;
    }

    private SqlExpression MapDatePartExpression(string datepart, SqlExpression argument)
    {
        if (argument is SqlConstantExpression constantArgument)
        {
            var constant = datepart switch
            {
                "yyyy" => ((DateOnly)constantArgument.Value!).Year,
                "m" => ((DateOnly)constantArgument.Value!).Month,
                "d" => ((DateOnly)constantArgument.Value!).Day,
                "h" => ((TimeOnly)constantArgument.Value!).Hour,
                "n" => ((TimeOnly)constantArgument.Value!).Minute,
                "s" => ((TimeOnly)constantArgument.Value!).Second,
                "fraction" => ((TimeOnly)constantArgument.Value!).Ticks % 10_000_000,

                _ => throw new UnreachableException()
            };

            return _sqlExpressionFactory.Constant(constant, typeof(int));
        }

        if (datepart == "fraction")
        {
            return _sqlExpressionFactory.Divide(
                _sqlExpressionFactory.Function(
                    "DATEPART",
                    [_sqlExpressionFactory.Fragment("nanosecond"), argument],
                    nullable: true,
                    argumentsPropagateNullability: [true, true],
                    typeof(int)
                ),
                _sqlExpressionFactory.Constant(100, typeof(int))
            );
        }

        return _sqlExpressionFactory.Function(
            "DATEPART",
            [_sqlExpressionFactory.Constant(datepart), argument],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            typeof(int));
    }
}
