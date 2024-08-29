// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    public class JetIsDateFunctionTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
    {
        private static readonly MethodInfo _methodInfo = typeof(JetDbFunctionsExtensions)
            .GetRuntimeMethod(nameof(JetDbFunctionsExtensions.IsDate), new[] { typeof(DbFunctions), typeof(string) })!;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            return _methodInfo.Equals(method)
                ? sqlExpressionFactory.Convert(
                    sqlExpressionFactory.Function(
                        "ISDATE",
                        new[] { arguments[1] },
                        false,
                        new[] {false},
                        _methodInfo.ReturnType),
                    _methodInfo.ReturnType)
                : null;
        }
    }
}
