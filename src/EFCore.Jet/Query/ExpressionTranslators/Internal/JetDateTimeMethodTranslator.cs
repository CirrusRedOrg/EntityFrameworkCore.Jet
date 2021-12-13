// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeMethodTranslator : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        private readonly Dictionary<MethodInfo, string> _methodInfoDatePartMapping = new Dictionary<MethodInfo, string>
        {
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), new[] {typeof(int)}), "yyyy"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), new[] {typeof(int)}), "m"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), new[] {typeof(double)}), "d"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), new[] {typeof(double)}), "h"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), new[] {typeof(double)}), "n"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), new[] {typeof(double)}), "s"},
            // {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMilliseconds), new[] {typeof(double)}), "millisecond"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddYears), new[] {typeof(int)}), "yyyy"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMonths), new[] {typeof(int)}), "m"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddDays), new[] {typeof(double)}), "d"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddHours), new[] {typeof(double)}), "h"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMinutes), new[] {typeof(double)}), "n"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddSeconds), new[] {typeof(double)}), "s"},
            // {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMilliseconds), new[] {typeof(double)}), "millisecond"}
        };

        public JetDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory) sqlExpressionFactory;

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_methodInfoDatePartMapping.TryGetValue(method, out var datePart))
            {
                var amountToAdd = arguments.First();

                if (!datePart.Equals("yyyy")
                    && !datePart.Equals("m")
                    && amountToAdd is SqlConstantExpression constantExpression
                    && ((double) constantExpression.Value >= int.MaxValue
                        || (double) constantExpression.Value <= int.MinValue))
                {
                    return null;
                }

                return _sqlExpressionFactory.Function(
                    "DATEADD",
                    new[]
                    {
                        new SqlConstantExpression(Expression.Constant(datePart), null),
                        amountToAdd,
                        instance
                    },
                    false,
                    new[] {false},
                    method.ReturnType);
            }

            return null;
        }
    }
}