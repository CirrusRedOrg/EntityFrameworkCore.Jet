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
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), new[] {typeof(int)})!, "yyyy"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), new[] {typeof(int)})!, "m"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), new[] {typeof(double)})!, "d"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), new[] {typeof(double)})!, "h"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), new[] {typeof(double)})!, "n"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), new[] {typeof(double)})!, "s"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMilliseconds), new[] {typeof(double)})!, "millisecond"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddYears), new[] {typeof(int)})!, "yyyy"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMonths), new[] {typeof(int)})!, "m"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddDays), new[] {typeof(double)})!, "d"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddHours), new[] {typeof(double)})!, "h"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMinutes), new[] {typeof(double)})!, "n"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddSeconds), new[] {typeof(double)})!, "s"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMilliseconds), new[] {typeof(double)})!, "millisecond"}
        };

        public JetDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_methodInfoDatePartMapping.TryGetValue(method, out var datePart) && instance != null)
            {
                var amountToAdd = arguments.First();

                if (!datePart.Equals("yyyy")
                    && !datePart.Equals("m")
                    && amountToAdd is SqlConstantExpression constantExpression
                    && constantExpression.Value is double doubleValue
                    && (doubleValue >= int.MaxValue
                        || doubleValue <= int.MinValue))
                {
                    return null;
                }
                //The DateAdd function can not take a null for the argument with the amount to add
                //We rewrite the expression a bit to simplify things
                //We first take dig town into the operand of the convert expression to colaesce that to 0
                //Then do the convert on that
                //This can simplify the expression as the reverse would have more IIF when there would be the 0
                if (amountToAdd is SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert)
                {
                    var cols = ExtractColumnExpressions(convert);
                    if (cols.Any(c => c.IsNullable))
                    {
                        amountToAdd = _sqlExpressionFactory.Coalesce(convert.Operand,
                            _sqlExpressionFactory.Constant(0, amountToAdd.TypeMapping));
                        amountToAdd = _sqlExpressionFactory.Convert(amountToAdd, convert.Type, convert.TypeMapping);
                    }
                }

                return _sqlExpressionFactory.Function(
                    "DATEADD",
                    new[]
                    {
                        new SqlConstantExpression(Expression.Constant(datePart), null),
                        amountToAdd,
                        instance
                    },
                    true,
                    argumentsPropagateNullability: new[] { false, false, true },
                    instance.Type,
                    instance.TypeMapping);
            }

            return null;
        }

        private List<ColumnExpression> ExtractColumnExpressions(SqlBinaryExpression binaryexp)
        {
            List<ColumnExpression> result = new List<ColumnExpression>();
            if (binaryexp.Left is SqlBinaryExpression left)
            {
                result.AddRange(ExtractColumnExpressions(left));
            }
            else if (binaryexp.Left is ColumnExpression colLeft)
            {
                result.Add(colLeft);
            }

            if (binaryexp.Right is SqlBinaryExpression right)
            {
                result.AddRange(ExtractColumnExpressions(right));
            }
            else if (binaryexp.Right is ColumnExpression colRight)
            {
                result.Add(colRight);
            }

            return result;
        }
        private List<ColumnExpression> ExtractColumnExpressions(SqlUnaryExpression unaryexp)
        {
            List<ColumnExpression> result = new List<ColumnExpression>();
            if (unaryexp.Operand is SqlBinaryExpression left)
            {
                result.AddRange(ExtractColumnExpressions(left));
            }
            else if (unaryexp.Operand is ColumnExpression colLeft)
            {
                result.Add(colLeft);
            }

            return result;
        }
    }
}