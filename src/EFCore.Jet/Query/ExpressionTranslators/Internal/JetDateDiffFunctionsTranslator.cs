// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using ExpressionExtensions = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    public class JetDateDiffFunctionsTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
    {
        private readonly Dictionary<MethodInfo, string> _methodInfoDateDiffMapping
            = new()
            {
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        [typeof(DbFunctions), typeof(DateTime), typeof(DateTime)])!,
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        [typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?)])!,
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        [typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset)])!,
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        [typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?)])!,
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        [typeof(DbFunctions), typeof(DateTime), typeof(DateTime)])!,
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        [typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?)])!,
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        [typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset)])!,
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        [typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?)])!,
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        [typeof(DbFunctions), typeof(DateTime), typeof(DateTime)])!,
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        [typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?)])!,
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        [typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset)])!,
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        [typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?)])!,
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        [typeof(DbFunctions), typeof(DateTime), typeof(DateTime)])!,
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        [typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?)])!,
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        [typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset)])!,
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        [typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?)])!,
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        [typeof(DbFunctions), typeof(TimeSpan), typeof(TimeSpan)])!,
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        [typeof(DbFunctions), typeof(TimeSpan?), typeof(TimeSpan?)])!,
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        [typeof(DbFunctions), typeof(DateTime), typeof(DateTime)])!,
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        [typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?)])!,
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        [typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset)])!,
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        [typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?)])!,
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        [typeof(DbFunctions), typeof(TimeSpan), typeof(TimeSpan)])!,
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        [typeof(DbFunctions), typeof(TimeSpan?), typeof(TimeSpan?)])!,
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        [typeof(DbFunctions), typeof(DateTime), typeof(DateTime)])!,
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        [typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?)])!,
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        [typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset)])!,
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        [typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?)])!,
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        [typeof(DbFunctions), typeof(TimeSpan), typeof(TimeSpan)])!,
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        [typeof(DbFunctions), typeof(TimeSpan?), typeof(TimeSpan?)])!,
                    "s"
                },
            };

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_methodInfoDateDiffMapping.TryGetValue(method, out var datePart))
            {
                var startDate = arguments[1];
                var endDate = arguments[2];
                var typeMapping = ExpressionExtensions.InferTypeMapping(startDate, endDate);

                startDate = sqlExpressionFactory.ApplyTypeMapping(startDate, typeMapping);
                endDate = sqlExpressionFactory.ApplyTypeMapping(endDate, typeMapping);

                return sqlExpressionFactory.Function(
                    "DATEDIFF",
                    [sqlExpressionFactory.Constant(datePart), startDate, endDate],
                    false,
                    [false, false, false], 
                    typeof(int));
            }

            return null;
        }
    }
}
