using EntityFrameworkCore.Jet.Query.Expressions.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query
{
    public class JetSqlExpressionFactory : SqlExpressionFactory
    {
        private readonly IRelationalTypeMappingSource _typeMappingSource;
        
        public JetSqlExpressionFactory(SqlExpressionFactoryDependencies dependencies)
            : base(dependencies)
        {
            _typeMappingSource = dependencies.TypeMappingSource;
        }

        #region Expression factory methods

        public IIfSqlFunctionExpression JetIIf(
            [NotNull] SqlExpression condition,
            SqlExpression ifTrue,
            SqlExpression ifFalse,
            [CanBeNull] RelationalTypeMapping typeMapping = null)
            => new IIfSqlFunctionExpression(
                new[] {condition, ifTrue, ifFalse},
                typeMapping);

        public IsNullSqlExpression JetIsNull(SqlExpression sqlExpression)
            => new IsNullSqlExpression(sqlExpression);
        
        public NullCheckedSqlExpression JetNullChecked(
            SqlExpression sqlExpression,
            RelationalTypeMapping typeMapping = null)
            => new NullCheckedSqlExpression(sqlExpression, typeMapping);

        public NullCheckedSqlExpression JetNullChecked(
            SqlExpression checkSqlExpression,
            SqlExpression notNullSqlExpression,
            RelationalTypeMapping typeMapping = null)
            => new NullCheckedSqlExpression(checkSqlExpression, notNullSqlExpression, typeMapping);

        #endregion Expression factory methods

        public override SqlExpression ApplyTypeMapping(SqlExpression sqlExpression, RelationalTypeMapping typeMapping)
        {
            if (sqlExpression == null
                || sqlExpression.TypeMapping != null)
            {
                return sqlExpression;
            }

            switch (sqlExpression)
            {
                case IIfSqlFunctionExpression e:
                    return ApplyTypeMappingOnJetIIfExpression(e);

                default:
                    return base.ApplyTypeMapping(sqlExpression, typeMapping);
            }
        }

        private IIfSqlFunctionExpression ApplyTypeMappingOnJetIIfExpression(IIfSqlFunctionExpression expression)
        {
            var inferredTypeMapping = _typeMappingSource.FindMapping(expression.Type);

            return new IIfSqlFunctionExpression(
                expression.Arguments,
                inferredTypeMapping ?? expression.TypeMapping);
        }
    }
}