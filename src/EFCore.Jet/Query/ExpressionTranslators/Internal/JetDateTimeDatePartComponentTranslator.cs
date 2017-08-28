// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.Expressions;
using Microsoft.EntityFrameworkCore.Query.ExpressionTranslators;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeDatePartComponentTranslator : IMemberTranslator
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Expression Translate(MemberExpression memberExpression)
        {
            string datePart;
            if (memberExpression.Expression != null
                && (memberExpression.Expression.Type == typeof(DateTime) || memberExpression.Expression.Type == typeof(DateTimeOffset))
                && (datePart = GetDatePart(memberExpression.Member.Name)) != null)
            {
                return new SqlFunctionExpression(
                    functionName: "DatePart",
                    returnType: memberExpression.Type,
                    arguments: new[]
                    {
                        new SqlFragmentExpression("'" + datePart + "'"),
                        memberExpression.Expression
                    });
            }
            return null;
        }

        private static string GetDatePart(string memberName)
        {
            switch (memberName)
            {
                case nameof(DateTime.Year):
                    return "yyyy";
                //case nameof(DateTime.Quarter):
                //    return "q";
                case nameof(DateTime.Month):
                    return "m";
                case nameof(DateTime.DayOfYear):
                    return "y";
                case nameof(DateTime.Day):
                    return "d";
                //case nameof(DateTime.DayOfWeek):
                //    return "w";
                //case nameof(DateTime.Week):
                //    return "ww";
                case nameof(DateTime.Hour):
                    return "h";
                case nameof(DateTime.Minute):
                    return "n";
                case nameof(DateTime.Second):
                    return "s";
                case nameof(DateTime.Millisecond):
                //    return "millisecond";
                    throw new NotSupportedException("JET does not support milliseconds in DateTime");
                default:
                    return null;
            }
        }
    }
}
