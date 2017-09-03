using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Expressions;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    /// <summary>
    /// Generic function that checks if a value is null then if is null it returns null; otherwise it returns the value
    /// </summary>
    /// <seealso cref="EntityFrameworkCore.Jet.Query.Expressions.Internal.IIfSqlFunctionExpression" />
    public class NullCheckedConvertSqlFunctionExpression : IIfSqlFunctionExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NullCheckedConvertSqlFunctionExpression"/> class.
        /// </summary>
        /// <param name="functionName">Name of the function to call if the value is not null.</param>
        /// <param name="returnType">Type of the return value.</param>
        /// <param name="expression">The value.</param>
        public NullCheckedConvertSqlFunctionExpression([NotNull]string functionName, [NotNull]Type returnType, [NotNull]Expression expression) :
            base(
                returnType, 
                new IsNullSqlFunctionExpression(expression),
                null,
                new SqlFunctionExpression(
                    functionName,
                    returnType,
                    new[]
                    {
                        expression
                    })
                )
        { }

    }
}
