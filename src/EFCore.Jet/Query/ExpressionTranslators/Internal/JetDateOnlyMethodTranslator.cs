// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class JetDateOnlyMethodTranslator : IMethodCallTranslator
{
    private readonly Dictionary<MethodInfo, string> _methodInfoDatePartMapping = new()
    {
        { typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddYears), new[] { typeof(int) })!, "yyyy" },
        { typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddMonths), new[] { typeof(int) })!, "m" },
        { typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddDays), new[] { typeof(int) })!, "d" }
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetDateOnlyMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

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
        if (_methodInfoDatePartMapping.TryGetValue(method, out var datePart)
            && instance != null)
        {
            instance = _sqlExpressionFactory.ApplyDefaultTypeMapping(instance);

            return _sqlExpressionFactory.Function(
                "DATEADD",
                new[] { _sqlExpressionFactory.Constant(datePart), _sqlExpressionFactory.Convert(arguments[0], typeof(int)), instance },
                nullable: true,
                argumentsPropagateNullability: new[] { false, true, true },
                instance.Type,
                instance.TypeMapping);
        }

        if (method.DeclaringType == typeof(DateOnly)
            && method.Name == nameof(DateOnly.FromDateTime)
            && arguments.Count == 1)
        {
            return _sqlExpressionFactory.Convert(arguments[0], typeof(DateOnly));
        }

        return null;
    }
}
