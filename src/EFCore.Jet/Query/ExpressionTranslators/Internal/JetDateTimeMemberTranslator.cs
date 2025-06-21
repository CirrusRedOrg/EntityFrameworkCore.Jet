// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource)
        : IMemberTranslator
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

        private readonly JetSqlExpressionFactory _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;
        private readonly IRelationalTypeMappingSource _typeMappingSource = typeMappingSource;

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
                        [
                            _sqlExpressionFactory.Constant(datePart),
                            instance!,
                        ],
                        false,
                        [false, false],
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
                    nameof(DateTime.Now) => _sqlExpressionFactory.Function("NOW", [],
                        false, [], returnType),
                    nameof(DateTime.UtcNow) => _sqlExpressionFactory.Function(
                    "DATEADD",
                    [
                        new SqlConstantExpression("n", null),
                        new SqlConstantExpression(-1 * TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes, null) ,
                        _sqlExpressionFactory.Function("NOW", [],
                            false, [], returnType)
                    ],
                    true,
                    argumentsPropagateNullability: [false, false, true],
                    returnType),
                    nameof(DateTime.Today) => _sqlExpressionFactory.Function(
                        "DATEVALUE",
                        [
                            _sqlExpressionFactory.Function("DATE", [], false, [],
                                returnType)
                        ],
                        false,
                        [false],
                        returnType),

                    nameof(DateTime.Date) => _sqlExpressionFactory.DateTimeNullChecked(
                        instance!,
                        _sqlExpressionFactory.Function(
                            "DATEVALUE",
                            [instance!],
                            false,
                            [false],
                            returnType)),
                    nameof(DateTime.TimeOfDay) => _sqlExpressionFactory.TimeSpanNullChecked(
                        instance!,
                        _sqlExpressionFactory.Function(
                            "TIMEVALUE",
                            [instance!],
                            false,
                            [false],
                            returnType)),

                    nameof(DateTime.Ticks) => null,

                    _ => null
                };
            }
            return null;
        }
    }
}