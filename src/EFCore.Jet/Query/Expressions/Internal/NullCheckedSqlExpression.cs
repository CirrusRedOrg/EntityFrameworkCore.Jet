using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    /// <summary>
    /// Generic function that checks if a value is null then if is null it returns null; otherwise it returns the value
    /// </summary>
    /// <seealso cref="EntityFrameworkCore.Jet.Query.Expressions.Internal.IIfSqlFunctionExpression" />
    public class NullCheckedSqlExpression : IIfSqlFunctionExpression
    {
        public NullCheckedSqlExpression(
            [NotNull] SqlExpression expression,
            RelationalTypeMapping typeMapping = null)
            : this(
                expression,
                expression,
                typeMapping)
        {
        }

        public NullCheckedSqlExpression(
            [NotNull] SqlExpression checkExpression,
            [NotNull] SqlExpression notNullExpression,
            RelationalTypeMapping typeMapping = null)
            : base(
                new IsNullSqlExpression(checkExpression),
                null,
                notNullExpression,
                notNullExpression.Type,
                typeMapping ?? notNullExpression.TypeMapping
            )
        {
        }
    }
}