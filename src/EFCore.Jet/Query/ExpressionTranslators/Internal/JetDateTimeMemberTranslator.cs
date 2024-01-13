// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeMemberTranslator : IMemberTranslator
    {
        private static readonly Dictionary<string, string> DatePartMapping
            = new()
            {
                { nameof(DateTime.Year), "yyyy" },
                { nameof(DateTime.Month), "m" },
                { nameof(DateTime.DayOfYear), "y" },
                { nameof(DateTime.Day), "d" },
                { nameof(DateTime.Hour), "h" },
                { nameof(DateTime.Minute), "n" },
                { nameof(DateTime.Second), "s" },
                { nameof(DateTime.DayOfWeek), "w" },
                //{ nameof(DateTime.Millisecond), "millisecond" }
            };

        private readonly JetSqlExpressionFactory _sqlExpressionFactory;
        private readonly IRelationalTypeMappingSource _typeMappingSource;

        public JetDateTimeMemberTranslator(
            ISqlExpressionFactory sqlExpressionFactory,
            IRelationalTypeMappingSource typeMappingSource)
        {
            _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;
            _typeMappingSource = typeMappingSource;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (member.DeclaringType == typeof(DateTime) ||
                member.DeclaringType == typeof(DateTimeOffset))
            {
                if (DatePartMapping.TryGetValue(member.Name, out var datePart))
                {
                    var datePartFunc = _sqlExpressionFactory.Function(
                        "DATEPART",
                        new[]
                        {
                            _sqlExpressionFactory.Constant(datePart),
                            instance!,
                        },
                        false,
                        new[] { false },
                        returnType);
                    if (datePart == "w")
                    {
                        return _sqlExpressionFactory.Subtract(
                            datePartFunc,
                            _sqlExpressionFactory.Constant(1));
                    }

                    return datePartFunc;
                }

                return member.Name switch
                {
                    nameof(DateTime.Now) => _sqlExpressionFactory.Function("NOW", Array.Empty<SqlExpression>(),
                        false, new[] { false }, returnType),
                    nameof(DateTime.UtcNow) => _sqlExpressionFactory.Function("NOW", Array.Empty<SqlExpression>(),
                        false, new[] { false }, returnType),
                    nameof(DateTime.Today) => _sqlExpressionFactory.Function(
                        "DATEVALUE",
                        new[]
                        {
                            _sqlExpressionFactory.Function("DATE", Array.Empty<SqlExpression>(), false, new[] { false },
                                returnType)
                        },
                        false,
                        new[] { false },
                        returnType),

                    nameof(DateTime.Date) => _sqlExpressionFactory.NullChecked(
                        instance!,
                        _sqlExpressionFactory.Function(
                            "DATEVALUE",
                            new[] { instance! },
                            false,
                            new[] { false },
                            returnType)),
                    nameof(DateTime.TimeOfDay) => _sqlExpressionFactory.NullChecked(
                        instance!,
                        _sqlExpressionFactory.Function(
                            "TIMEVALUE",
                            new[] { instance! },
                            false,
                            new[] { false },
                            returnType)),

                    nameof(DateTime.Ticks) => null,

                    _ => null
                };
            }
            return null;
        }
    }
}