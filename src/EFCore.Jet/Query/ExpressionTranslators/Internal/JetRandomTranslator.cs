// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetRandomTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
    {
        private static readonly MethodInfo[] _methodInfo =
        [
            typeof(DbFunctionsExtensions).GetRuntimeMethod(nameof(DbFunctionsExtensions.Random), [
                typeof(DbFunctions)
            ])!,
            typeof(JetDbFunctionsExtensions).GetRuntimeMethod(nameof(JetDbFunctionsExtensions.Random), [
                typeof(DbFunctions)
            ])!
        ];
        private readonly JetSqlExpressionFactory _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            return _methodInfo.Contains(method)
                ? _sqlExpressionFactory.Function(
                    "Rnd",
                    [],
                    false,
                    [],
                    method.ReturnType)
                : null;
        }
    }
}