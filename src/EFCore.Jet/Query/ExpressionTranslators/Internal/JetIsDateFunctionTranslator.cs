// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    public class JetIsDateFunctionTranslator : IMethodCallTranslator
    {
        private readonly ISqlExpressionFactory _sqlExpressionFactory;

        private static readonly MethodInfo _methodInfo = typeof(JetDbFunctionsExtensions)
            .GetRuntimeMethod(nameof(JetDbFunctionsExtensions.IsDate), new[] { typeof(DbFunctions), typeof(string) });

        public JetIsDateFunctionTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = sqlExpressionFactory;

        public virtual SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments)
        {
            return _methodInfo.Equals(method)
                ? _sqlExpressionFactory.Convert(
                    _sqlExpressionFactory.Function(
                        "ISDATE",
                        new[] { arguments[1] },
                        _methodInfo.ReturnType),
                    _methodInfo.ReturnType)
                : null;
        }
    }
}
