// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetStringMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMemberTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            => member.Name == nameof(string.Length) &&
               instance?.Type == typeof(string)
                ? _sqlExpressionFactory.Convert(
                    _sqlExpressionFactory.Function(
                        "LEN", [instance], true, [true], returnType),
                    typeof(int))
                : null;
    }
}