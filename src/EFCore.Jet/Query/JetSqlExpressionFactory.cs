using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query
{
    public class JetSqlExpressionFactory(SqlExpressionFactoryDependencies dependencies) : SqlExpressionFactory(dependencies)
    {

        #region Expression factory methods

        public SqlBinaryExpression? NullChecked(
            SqlExpression sqlExpression,
            RelationalTypeMapping? typeMapping = null)
            => (SqlBinaryExpression?)MakeBinary(
                ExpressionType.Coalesce,
                sqlExpression,
                Constant(
                    null,typeof(string),
                    RelationalTypeMapping.NullMapping),
                typeMapping);

        public CaseExpression NullChecked(
            SqlExpression checkSqlExpression,
            SqlExpression notNullSqlExpression)
            => (CaseExpression)Case(
                [
                    new CaseWhenClause(
                        IsNull(checkSqlExpression),
                        Constant(
                            null,typeof(string),
                            RelationalTypeMapping.NullMapping))
                ],
                notNullSqlExpression);

        #endregion Expression factory methods
    }
}