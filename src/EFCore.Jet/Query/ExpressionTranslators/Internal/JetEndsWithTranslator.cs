// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query.Expressions;
using Microsoft.EntityFrameworkCore.Query.ExpressionTranslators;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetEndsWithTranslator : IMethodCallTranslator
    {
        private static readonly MethodInfo _methodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), new[] { typeof(string) });

        private static readonly MethodInfo _concat
            = typeof(string).GetRuntimeMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) });

        public virtual Expression Translate(MethodCallExpression methodCallExpression)
        {
            if (Equals(methodCallExpression.Method, _methodInfo))
            {
                return new LikeExpression
                (
                    // ReSharper disable once AssignNullToNotNullAttribute
                    methodCallExpression.Object,
                    Expression.Add(Expression.Constant("%", typeof(string)), methodCallExpression.Arguments[0],_concat)
                );
            }

            return null;
        }
    }
}
