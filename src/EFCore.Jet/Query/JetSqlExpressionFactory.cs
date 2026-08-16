using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query
{
    public class JetSqlExpressionFactory(SqlExpressionFactoryDependencies dependencies) : SqlExpressionFactory(dependencies)
    {
        /// <summary>
        ///     Jet stores booleans as VARIANT_BOOL, so True is -1, but converting one to a number has to yield
        ///     .NET's 1. The flip happens here because every conversion funnels through this method - Convert.ToXxx
        ///     via JetConvertTranslator, and plain casts built by EF - and because a multiplication survives where
        ///     a cast does not: EF drops a Convert whose operand already has the target's store type, and bool maps
        ///     to smallint, so Convert.ToInt16(bool) previously reached SQL as a bare column and `WHERE Bool = 1`
        ///     matched nothing. Conversion to string is left alone; ACE renders CStr(True) as "-1".
        /// </summary>
        public override SqlExpression Convert(
            SqlExpression operand,
            Type type,
            RelationalTypeMapping? typeMapping = null)
            => base.Convert(
                operand.Type == typeof(bool) && type.UnwrapNullableType().IsNumeric()
                    ? new SqlBinaryExpression(
                        ExpressionType.Multiply,
                        operand,
                        Constant(-1, IntTypeMapping.Default),
                        typeof(int),
                        IntTypeMapping.Default)
                    : operand,
                type,
                typeMapping);

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