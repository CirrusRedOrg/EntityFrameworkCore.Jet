// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        private readonly Dictionary<MethodInfo, string> _methodInfoDatePartMapping = new()
        {
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), [typeof(int)])!, "yyyy"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), [typeof(int)])!, "m"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), [typeof(double)])!, "d"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), [typeof(double)])!, "h"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), [typeof(double)])!, "n"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), [typeof(double)])!, "s"},
            {typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMilliseconds), [typeof(double)])!, "millisecond"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddYears), [typeof(int)])!, "yyyy"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMonths), [typeof(int)])!, "m"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddDays), [typeof(double)])!, "d"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddHours), [typeof(double)])!, "h"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMinutes), [typeof(double)])!, "n"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddSeconds), [typeof(double)])!, "s"},
            {typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.AddMilliseconds), [typeof(double)])!, "millisecond"}
        };

        private static readonly Dictionary<MethodInfo, string> _methodInfoDateDiffMapping = new()
        {
            { typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.ToUnixTimeSeconds), Type.EmptyTypes)!, "s" },
            { typeof(DateTimeOffset).GetRuntimeMethod(nameof(DateTimeOffset.ToUnixTimeMilliseconds), Type.EmptyTypes)!, "millisecond" }
        };

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_methodInfoDatePartMapping.TryGetValue(method, out var datePart) && instance != null)
            {
                var amountToAdd = arguments[0];

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
                    [
                        new SqlConstantExpression(datePart, null),
                        amountToAdd,
                        instance
                    ],
                    true,
                    argumentsPropagateNullability: [false, false, true],
                    instance.Type,
                    instance.TypeMapping);
            }

            if (_methodInfoDateDiffMapping.TryGetValue(method, out var timePart))
            {
                return _sqlExpressionFactory.Function(
                    "DATEDIFF",
                    [
                        new SqlConstantExpression(timePart, null),
                        _sqlExpressionFactory.Constant(DateTimeOffset.UnixEpoch, instance!.TypeMapping),
                        instance
                    ],
                    nullable: true,
                    argumentsPropagateNullability: [false, true, true],
                    typeof(long));
            }

            return null;
        }

        private List<ColumnExpression> ExtractColumnExpressions(SqlBinaryExpression binaryexp)
        {
            List<ColumnExpression> result = [];
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
            List<ColumnExpression> result = [];
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