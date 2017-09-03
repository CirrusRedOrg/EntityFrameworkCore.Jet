using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Expressions;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    public class IIfSqlFunctionExpression:SqlFunctionExpression
    {
        public IIfSqlFunctionExpression([NotNull]Type returnType, [NotNull]Expression condition, [NotNull]Expression ifTrue, [NotNull]Expression ifFalse)
            :base(
                 functionName: "IIf",
                 returnType: returnType,
                 arguments: new []{ condition, ifTrue ?? Expression.Constant(null), ifFalse??Expression.Constant(null) }
                 )
        {
            
        }
    }
}
