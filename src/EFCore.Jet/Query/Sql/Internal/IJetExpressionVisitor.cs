// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Query.Expressions.Internal;
using JetBrains.Annotations;

namespace EntityFrameworkCore.Jet.Query.Sql.Internal
{
    public interface IJetExpressionVisitor
    {
        Expression VisitRowNumber([NotNull] RowNumberExpression rowNumberExpression);
    }
}
