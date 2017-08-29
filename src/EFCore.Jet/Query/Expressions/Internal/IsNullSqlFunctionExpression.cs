using System;
using Microsoft.EntityFrameworkCore.Query.Expressions;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    /// <summary>
    /// Function that checks if a value is null
    /// </summary>
    /// <seealso cref="Microsoft.EntityFrameworkCore.Query.Expressions.SqlFunctionExpression" />
    public class IsNullSqlFunctionExpression : SqlFunctionExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IsNullSqlFunctionExpression"/> class.
        /// </summary>
        /// <param name="nullableExpression">The nullable value to check.</param>
        public IsNullSqlFunctionExpression(Expression nullableExpression) :
            base(
                functionName: "IsNull",
                returnType: typeof(bool),
                arguments: new[] {nullableExpression})
        {
        }
    }
}
