using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query
{
    public class JetSqlExpressionFactory(SqlExpressionFactoryDependencies dependencies) : SqlExpressionFactory(dependencies)
    {

        #region Expression factory methods

        public virtual SqlBinaryExpression? NullChecked(
            SqlExpression sqlExpression,
            RelationalTypeMapping? typeMapping = null)
            => (SqlBinaryExpression?)MakeBinary(
                ExpressionType.Coalesce,
                sqlExpression,
                Constant(
                    null,typeof(string),
                    RelationalTypeMapping.NullMapping),
                typeMapping);

        public virtual CaseExpression NullChecked(
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

        public virtual CaseExpression DateTimeNullChecked(
            SqlExpression checkSqlExpression,
            SqlExpression notNullSqlExpression)
            => (CaseExpression)Case(
                [
                    new CaseWhenClause(
                        IsNull(checkSqlExpression),
                        Constant(
                            null,typeof(DateTime),
                            notNullSqlExpression.TypeMapping))
                ],
                notNullSqlExpression);

        public virtual CaseExpression TimeSpanNullChecked(
            SqlExpression checkSqlExpression,
            SqlExpression notNullSqlExpression)
            => (CaseExpression)Case(
                [
                    new CaseWhenClause(
                        IsNull(checkSqlExpression),
                        Constant(
                            null,typeof(TimeSpan),
                            notNullSqlExpression.TypeMapping))
                ],
                notNullSqlExpression);
        #endregion Expression factory methods
    }
}