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
                ShapedQueryExpression shapedQueryExpression => shapedQueryExpression.Update(Visit(shapedQueryExpression.QueryExpression), Visit(shapedQueryExpression.ShaperExpression)),
                _ => base.VisitExtension(extensionExpression)
            };

        protected virtual SelectExpression VisitSelect(SelectExpression selectExpression)
        {
            //
            // Most outer SELECT expressions will convert types that can contain a time related value to DOUBLE:
            //

            // We are explicitly using a conversion to a target CLR type of System.Double together with a date/time
            // related type mapping (we support this in JetQuerySqlGenerator).
            // This will result in JetDataReader.GetDateTime() being called for the System.Double returned from the
            // database. JetDataReader.GetDateTime() explicitly supports this and handles the value appropriately.
            var newProjections = selectExpression.Projection.Select(
                    projection => projection.Expression.TypeMapping.ClrType.IsTimeRelatedType()
                        ? projection.Update(
                            new SqlUnaryExpression(
                                ExpressionType.Convert,
                                projection.Expression,
                                typeof(double),
                                _relationalTypeMappingSource.FindMapping(projection.Expression.TypeMapping.ClrType)))
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