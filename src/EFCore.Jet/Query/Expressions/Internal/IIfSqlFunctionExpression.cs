using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query.Expressions;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    public class IIfSqlFunctionExpression:SqlFunctionExpression
    {
        public IIfSqlFunctionExpression(Type returnType, Expression condition, Expression ifTrue, Expression ifFalse)
            :base(
                 functionName: "IIf",
                 returnType: returnType,
                 arguments: new []{ condition, ifTrue ?? Expression.Constant(null), ifFalse??Expression.Constant(null) }
                 )
        {
            
        }
    }
}
