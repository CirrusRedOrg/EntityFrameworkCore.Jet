// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeMemberTranslator : IMemberTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        public JetDateTimeMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public SqlExpression Translate(SqlExpression instance, MemberInfo member, Type returnType)
        {
            if (member.DeclaringType != typeof(DateTime) ||
                member.DeclaringType != typeof(DateTimeOffset))
                return null;

            if (instance == null)
            {
                return member.Name switch
                {
                    nameof(DateTime.Now) => _sqlExpressionFactory.Function("Now", Array.Empty<SqlExpression>(), returnType),
                    nameof(DateTime.UtcNow) => _sqlExpressionFactory.Function("Now", Array.Empty<SqlExpression>(), returnType),
                    _ => null,
                };
            }

            return member.Name switch
            {
                nameof(DateTime.Today) => _sqlExpressionFactory.Function(
                        "DateValue",
                        new[] {_sqlExpressionFactory.Function("Now", Array.Empty<SqlExpression>(), returnType)},
                        returnType),

                nameof(DateTime.Year) => GetDatePartExpression(instance, returnType, "yyyy"),
                nameof(DateTime.Month) => GetDatePartExpression(instance, returnType, "m"),
                nameof(DateTime.DayOfYear) => GetDatePartExpression(instance, returnType, "y"),
                nameof(DateTime.Day) => GetDatePartExpression(instance, returnType, "d"),
                nameof(DateTime.Hour) => GetDatePartExpression(instance, returnType, "h"),
                nameof(DateTime.Minute) => GetDatePartExpression(instance, returnType, "n"),
                nameof(DateTime.Second) => GetDatePartExpression(instance, returnType, "s"),
                nameof(DateTime.Millisecond) => null, // Not supported in Jet

                nameof(DateTime.DayOfWeek) => _sqlExpressionFactory.Subtract(
                    GetDatePartExpression(instance, returnType, "w"),
                    _sqlExpressionFactory.Constant(1)),

                nameof(DateTime.Date) => _sqlExpressionFactory.JetNullChecked(
                    instance,
                    _sqlExpressionFactory.Function(
                        "DateValue",
                        new[] {instance},
                        returnType)),
                nameof(DateTime.TimeOfDay) => _sqlExpressionFactory.JetNullChecked(
                    instance,
                    _sqlExpressionFactory.Function(
                        "TimeValue",
                        new[] {instance},
                        returnType)),

                nameof(DateTime.Ticks) => null,

                _ => null
            };
        }

        private SqlExpression GetDatePartExpression(
            SqlExpression instance,
            Type returnType,
            string datePart)
        {
            return _sqlExpressionFactory.Function(
                "DatePart",
                new[]
                {
                    _sqlExpressionFactory.Constant(datePart),
                    instance,
                },
                returnType);
        }
    }
}