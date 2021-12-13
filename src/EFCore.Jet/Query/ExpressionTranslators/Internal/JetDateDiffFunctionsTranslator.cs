// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    public class JetDateDiffFunctionsTranslator : IMethodCallTranslator
    {
        private readonly Dictionary<MethodInfo, string> _methodInfoDateDiffMapping
            = new Dictionary<MethodInfo, string>
            {
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        new[] { typeof(DbFunctions), typeof(DateTime), typeof(DateTime) }),
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        new[] { typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?) }),
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset) }),
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffYear),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?) }),
                    "yyyy"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        new[] { typeof(DbFunctions), typeof(DateTime), typeof(DateTime) }),
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        new[] { typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?) }),
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset) }),
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMonth),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?) }),
                    "m"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        new[] { typeof(DbFunctions), typeof(DateTime), typeof(DateTime) }),
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        new[] { typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?) }),
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset) }),
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffDay),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?) }),
                    "d"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        new[] { typeof(DbFunctions), typeof(DateTime), typeof(DateTime) }),
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        new[] { typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?) }),
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset) }),
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?) }),
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        new[] { typeof(DbFunctions), typeof(TimeSpan), typeof(TimeSpan) }),
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffHour),
                        new[] { typeof(DbFunctions), typeof(TimeSpan?), typeof(TimeSpan?) }),
                    "h"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        new[] { typeof(DbFunctions), typeof(DateTime), typeof(DateTime) }),
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        new[] { typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?) }),
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset) }),
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?) }),
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        new[] { typeof(DbFunctions), typeof(TimeSpan), typeof(TimeSpan) }),
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffMinute),
                        new[] { typeof(DbFunctions), typeof(TimeSpan?), typeof(TimeSpan?) }),
                    "n"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        new[] { typeof(DbFunctions), typeof(DateTime), typeof(DateTime) }),
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        new[] { typeof(DbFunctions), typeof(DateTime?), typeof(DateTime?) }),
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset), typeof(DateTimeOffset) }),
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        new[] { typeof(DbFunctions), typeof(DateTimeOffset?), typeof(DateTimeOffset?) }),
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        new[] { typeof(DbFunctions), typeof(TimeSpan), typeof(TimeSpan) }),
                    "s"
                },
                {
                    typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
                        nameof(JetDbFunctionsExtensions.DateDiffSecond),
                        new[] { typeof(DbFunctions), typeof(TimeSpan?), typeof(TimeSpan?) }),
                    "s"
                },
            };

        private readonly ISqlExpressionFactory _sqlExpressionFactory;

        public JetDateDiffFunctionsTranslator(
            ISqlExpressionFactory sqlExpressionFactory)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
        }

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_methodInfoDateDiffMapping.TryGetValue(method, out var datePart))
            {
                var startDate = arguments[1];
                var endDate = arguments[2];
                var typeMapping = ExpressionExtensions.InferTypeMapping(startDate, endDate);

                startDate = _sqlExpressionFactory.ApplyTypeMapping(startDate, typeMapping);
                endDate = _sqlExpressionFactory.ApplyTypeMapping(endDate, typeMapping);

                return _sqlExpressionFactory.Function(
                    "DATEDIFF",
                    new[] { _sqlExpressionFactory.Constant(datePart), startDate, endDate },
                    false,
                    new[] {false}, 
                    typeof(int));
            }

            return null;
        }
    }
}
