using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Query.Sql.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    /// <summary>
    /// Function that checks if a value is null
    /// </summary>
    /// <seealso cref="SqlFunctionExpression" />
    public class IsNullSqlExpression : SqlExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IsNullSqlExpression"/> class.
        /// </summary>
        /// <param name="nullableExpression">The nullable value to check.</param>
        public IsNullSqlExpression(SqlExpression nullableExpression) :
            base(typeof(bool), null)
        {
            NullableExpression = nullableExpression;
        }

        public virtual SqlExpression NullableExpression { get; }

        protected override Expression Accept(ExpressionVisitor visitor)
            => visitor is JetQuerySqlGenerator mySqlQuerySqlGenerator
                ? mySqlQuerySqlGenerator.VisitJetIsNull(this)
                : base.Accept(visitor);

        protected override Expression VisitChildren(ExpressionVisitor visitor)
            => Update((SqlExpression) visitor.Visit(NullableExpression));

        public virtual IsNullSqlExpression Update(SqlExpression nullableExpression)
            => Equals(nullableExpression, NullableExpression)
                ? this
                : new IsNullSqlExpression(NullableExpression);

        public override void Print(ExpressionPrinter expressionPrinter)
        {
            expressionPrinter.Visit(NullableExpression);
            expressionPrinter.Append(" Is Null");
        }

        public override bool Equals(object obj)
            => obj is IsNullSqlExpression other &&
               Equals(obj);

        private bool Equals(IsNullSqlExpression other)
            => ReferenceEquals(this, other) ||
               base.Equals(other) &&
               Equals(NullableExpression, other.NullableExpression);

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), NullableExpression);
        
        public override string ToString() => $"{NullableExpression} Is Null";
    }
}