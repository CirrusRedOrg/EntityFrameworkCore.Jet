using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query
{
    public class JetSqlExpressionFactory : SqlExpressionFactory
    {
        public JetSqlExpressionFactory(SqlExpressionFactoryDependencies dependencies)
            : base(dependencies)
        {
        }

        #region Expression factory methods

        public SqlBinaryExpression NullChecked(
            SqlExpression sqlExpression,
            RelationalTypeMapping typeMapping = null)
            => MakeBinary(
                ExpressionType.Coalesce,
                sqlExpression,
                Constant(
                    null,
                    RelationalTypeMapping.NullMapping),
                typeMapping);

        public CaseExpression NullChecked(
            SqlExpression checkSqlExpression,
            SqlExpression notNullSqlExpression)
            => Case(
                new[]
                {
                    new CaseWhenClause(
                        IsNull(checkSqlExpression),
                        Constant(
                            null,
                            RelationalTypeMapping.NullMapping))
                },
                notNullSqlExpression);

        #endregion Expression factory methods
    }
}