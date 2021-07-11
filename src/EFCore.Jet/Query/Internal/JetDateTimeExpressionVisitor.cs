using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.Internal
{
    public class JetDateTimeExpressionVisitor : ExpressionVisitor
    {
        private readonly ISqlExpressionFactory _sqlExpressionFactory;
        private readonly IRelationalTypeMappingSource _relationalTypeMappingSource;
        
        public JetDateTimeExpressionVisitor(
            ISqlExpressionFactory sqlExpressionFactory,
            IRelationalTypeMappingSource relationalTypeMappingSource)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
            _relationalTypeMappingSource = relationalTypeMappingSource;
        }

        protected override Expression VisitExtension(Expression extensionExpression)
            => extensionExpression switch
            {
                SelectExpression selectExpression => VisitSelect(selectExpression),
                _ => base.VisitExtension(extensionExpression)
            };

        protected virtual SelectExpression VisitSelect(SelectExpression selectExpression)
        {
            //
            // Most outer SELECT expressions will convert types that can contain a time related value to DOUBLE:
            //

            var newProjections = selectExpression.Projection.Select(
                    projection => projection.Expression.TypeMapping.ClrType.IsTimeRelatedType()
                        ? projection.Update(
                            _sqlExpressionFactory.Convert(
                                projection.Expression,
                                typeof(double),
                                _relationalTypeMappingSource.FindMapping(typeof(double))))
                        : projection)
                .ToList();

            var expression = selectExpression.Update(
                newProjections,
                selectExpression.Tables.ToList(),
                selectExpression.Predicate,
                selectExpression.GroupBy.ToList(),
                selectExpression.Having,
                selectExpression.Orderings.ToList(),
                selectExpression.Limit,
                selectExpression.Offset);
            
            return expression;
        }
    }
}