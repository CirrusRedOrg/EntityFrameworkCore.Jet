// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Jet;
using System.Linq;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Query.Expressions.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Expressions;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Internal;
using Remotion.Linq.Clauses;

namespace EntityFrameworkCore.Jet.Query.Sql.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetQuerySqlGenerator : DefaultQuerySqlGenerator, IJetExpressionVisitor
    {

        private static readonly Dictionary<ExpressionType, string> _operatorMap = new Dictionary<ExpressionType, string>
        {
            { ExpressionType.And, " BAND " },
            { ExpressionType.Or, " BOR " }
        };


        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGenerator(
            [NotNull] QuerySqlGeneratorDependencies dependencies,
            [NotNull] SelectExpression selectExpression)
            : base(dependencies, selectExpression)
        {

        }

        /// <summary>The default true literal SQL.</summary>
        protected override string TypedTrueLiteral => "True";

        /// <summary>The default false literal SQL.</summary>
        protected override string TypedFalseLiteral => "False";



        public override Expression VisitSelect(SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            IDisposable subQueryIndent = null;

            if (selectExpression.Alias != null)
            {
                Sql.AppendLine("(");

                subQueryIndent = Sql.Indent();
            }

            Sql.Append("SELECT ");

            if (selectExpression.IsDistinct)
                Sql.Append("DISTINCT ");

            GenerateTop(selectExpression);

            var projectionAdded = false;

            if (selectExpression.IsProjectStar)
            {
                var tableAlias = selectExpression.ProjectStarTable.Alias;

                Sql
                    .Append(SqlGenerator.DelimitIdentifier(tableAlias))
                    .Append(".*");

                projectionAdded = true;
            }

            if (selectExpression.Projection.Any())
            {
                if (selectExpression.IsProjectStar)
                    Sql.Append(", ");

                ProcessExpressionList(selectExpression.Projection, GenerateProjection);

                projectionAdded = true;
            }

            if (!projectionAdded)
                Sql.Append("1");

            if (selectExpression.Tables.Any())
            {
                Sql.AppendLine()
                    .Append("FROM ");

                for (int index = 0; index < selectExpression.Tables.Count - 1; index++)
                    Sql.Append("(");
                ProcessExpressionList(selectExpression.Tables, sql => sql.Append(")").AppendLine());
            }
            else
            {
                Sql.AppendLine()
                    .Append("FROM " + JetConfiguration.DUAL);
            }

            if (selectExpression.Predicate != null)
                GeneratePredicate(selectExpression.Predicate);

            if (selectExpression.OrderBy.Any())
            {
                Sql.AppendLine();

                GenerateOrderBy(selectExpression.OrderBy);
            }

            GenerateLimitOffset(selectExpression);

            if (subQueryIndent != null)
            {
                subQueryIndent.Dispose();

                Sql.AppendLine()
                    .Append(")");

                if (selectExpression.Alias.Length > 0)
                {
                    Sql.Append(" AS ")
                        .Append(SqlGenerator.DelimitIdentifier(selectExpression.Alias));
                }
            }

            return selectExpression;
        }


        private void ProcessExpressionList(
            IReadOnlyList<Expression> expressions, Action<IRelationalCommandBuilder> joinAction = null)
            => ProcessExpressionList(expressions, e => Visit(e), joinAction);

        private void ProcessExpressionList<T>(
            IReadOnlyList<T> items, Action<T> itemAction, Action<IRelationalCommandBuilder> joinAction = null)
        {
            joinAction = joinAction ?? (isb => isb.Append(", "));

            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    joinAction(Sql);
                }

                itemAction(items[i]);
            }
        }

        public override Expression VisitFromSql(FromSqlExpression fromSqlExpression)
        {
            return base.VisitFromSql(fromSqlExpression);
        }

        protected override void GenerateFromSql(string sql, Expression arguments, IReadOnlyDictionary<string, object> parameters)
        {
            base.GenerateFromSql(sql, arguments, parameters);
        }

        public override Expression VisitInnerJoin(InnerJoinExpression innerJoinExpression)
        {
            return base.VisitInnerJoin(innerJoinExpression);
        }


        public override Expression VisitLeftOuterJoin(LeftOuterJoinExpression leftOuterJoinExpression)
        {
            return base.VisitLeftOuterJoin(leftOuterJoinExpression);
        }


        /// <summary>
        ///     Generates a single ordering in an SQL ORDER BY clause.
        /// </summary>
        /// <param name="ordering"> The ordering. </param>
        protected override void GenerateOrdering([NotNull] Ordering ordering)
        {
            Check.NotNull<Ordering>(ordering, nameof(ordering));
            Expression expression = ordering.Expression;
            AliasExpression aliasExpression;
            if ((aliasExpression = expression as AliasExpression) == null)
            {
                base.GenerateOrdering(ordering);
                return;
            }

            if (aliasExpression.Expression is ColumnExpression)
            {
                var columnExpression = aliasExpression.Expression as ColumnExpression;
                Sql.Append(SqlGenerator
                    .DelimitIdentifier(columnExpression.Table.Alias))
                    .Append(".")
                    .Append(SqlGenerator.DelimitIdentifier(columnExpression.Name));
            }
            else
                Sql.Append(SqlGenerator.DelimitIdentifier(aliasExpression.Alias));

            if (ordering.OrderingDirection == OrderingDirection.Desc)
                Sql.Append(" DESC");
        }

        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            Check.NotNull(binaryExpression, nameof(binaryExpression));

            switch (binaryExpression.NodeType)
            {
                case ExpressionType.Coalesce:
                    {
                        Sql.Append("IIf(IsNull(");
                        Visit(binaryExpression.Left);
                        Sql.Append("), ");
                        Visit(binaryExpression.Right);
                        Sql.Append(", ");
                        Visit(binaryExpression.Left);
                        Sql.Append(")");
                        return binaryExpression;
                    }
                default:
                    return base.VisitBinary(binaryExpression);
            }

        }

        public override Expression VisitExplicitCast(ExplicitCastExpression explicitCastExpression)
        {
            var typeMapping = Dependencies.RelationalTypeMapper.FindMapping(explicitCastExpression.Type);

            if (typeMapping == null)
                throw new InvalidOperationException(RelationalStrings.UnsupportedType(explicitCastExpression.Type.ShortDisplayName()));

            string function;


            switch (typeMapping.ClrType.Name)
            {
                case "String":
                    Sql.Append("(");
                    Visit(explicitCastExpression.Operand);
                    Sql.Append("&\"\")");
                    return explicitCastExpression;
                case "TimeSpan":
                case "DateTime":
                    function = "CDate";
                    break;
                case "Single":
                    function = "CSng";
                    break;
                case "Double":
                    function = "CDbl";
                    break;
                case "Byte":
                    function = "CByte";
                    break;
                case "Int16":
                case "Int32":
                    function = "CInt";
                    break;
                case "Int64":
                    function = "CLng";
                    break;
                case "Boolean":
                    function = "CBool";
                    break;
                case "Currency":
                    function = "CCur";
                    break;
                case "UInt16":
                case "UInt32":
                case "UInt64":
                    function = "CLng";
                    break;
                case "SByte":
                    function = "CInt";
                    break;
                case "Decimal":
                    // CDec does not work https://support.microsoft.com/it-it/help/225931/error-message-when-you-use-the-cdec-function-in-an-access-query-the-ex
                    //function = "CDec";
                    function = "CCur";
                    break;
                case "VarNumeric":
                case "Xml":
                case "Binary":
                case "Guid":
                default:
                    throw new InvalidOperationException(string.Format("invalid type for cast(): cannot handle type {0} with Jet", typeMapping.ClrType.Name));
            }

            Sql.Append(function + "(IIf(IsNull(");
            Visit(explicitCastExpression.Operand);
            Sql.Append("),0,");
            Visit(explicitCastExpression.Operand);
            Sql.Append("))");

            return explicitCastExpression;
        }

        /// <summary>
        ///     Attempts to generate binary operator for a given expression type.
        /// </summary>
        /// <param name="op"> The operation. </param>
        /// <param name="result"> [out] The SQL binary operator. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        protected override bool TryGenerateBinaryOperator(ExpressionType op, out string result)
        {
            if (_operatorMap.TryGetValue(op, out result))
                return true;
            return base.TryGenerateBinaryOperator(op, out result);
        }

        /// <summary>
        ///     Generates SQL for a given binary operation type.
        /// </summary>
        /// <param name="op"> The operation. </param>
        /// <returns>
        ///     The binary operator.
        /// </returns>
        protected override string GenerateBinaryOperator(ExpressionType op)
        {
            string result;
            if (TryGenerateBinaryOperator(op, out result))
                return result;
            return _operatorMap[op];
        }

        /// <summary>
        ///     Generates an SQL operator for a given expression.
        /// </summary>
        /// <param name="expression"> The expression. </param>
        /// <returns>
        ///     The operator.
        /// </returns>
        protected override string GenerateOperator(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Extension:
                    {
                        if (expression is StringCompareExpression asStringCompareExpression)
                        {
                            return GenerateBinaryOperator(asStringCompareExpression.Operator);
                        }
                        goto default;
                    }
                default:
                    {
                        string op;
                        if (expression is BinaryExpression)
                        {
                            if (!TryGenerateBinaryOperator(expression.NodeType, out op))
                            {
                                throw new ArgumentOutOfRangeException();
                            }
                            return op;
                        }
                        if (!_operatorMap.TryGetValue(expression.NodeType, out op))
                        {
                            return base.GenerateOperator(expression);
                        }
                        return op;
                    }
            }
        }



        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override Expression VisitCrossJoinLateral(CrossJoinLateralExpression crossJoinLateralExpression)
        {
            Check.NotNull(crossJoinLateralExpression, nameof(crossJoinLateralExpression));

            Sql.Append(", ");

            Visit(crossJoinLateralExpression.TableExpression);

            return crossJoinLateralExpression;
        }

        public override Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
        {
            Check.NotNull(crossJoinExpression, nameof(crossJoinExpression));

            Sql.Append(", ");

            Visit(crossJoinExpression.TableExpression);

            return crossJoinExpression;
        }


        /// <summary>Generates the TOP part of the SELECT statement,</summary>
        /// <param name="selectExpression"> The select expression. </param>
        protected override void GenerateTop(SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, "selectExpression");
            if (selectExpression.Limit == null)
                return;

            Sql.Append("TOP ");
            if (selectExpression.Offset == null)
                Visit(selectExpression.Limit);
            else
            {
                Visit(selectExpression.Limit);
                Sql.Append("+");
                Visit(selectExpression.Offset);
            }
            Sql.Append(" ");
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void GenerateLimitOffset(SelectExpression selectExpression)
        {
            if (selectExpression.Offset == null)
                return;

            if (!selectExpression.OrderBy.Any())
                Sql.AppendLine().Append("ORDER BY 'a'");

            Sql.AppendLine().Append(" SKIP ");
            Visit(selectExpression.Offset);
        }


        /// <summary>
        ///     Visit a ConditionalExpression.
        /// </summary>
        /// <param name="conditionalExpression"> The conditional expression to visit. </param>
        /// <returns>
        ///     An Expression.
        /// </returns>
        protected override Expression VisitConditional(ConditionalExpression conditionalExpression)
        {
            Check.NotNull(conditionalExpression, nameof(conditionalExpression));

            ConstantExpression constantIfTrue = conditionalExpression.IfTrue as ConstantExpression;
            ConstantExpression constantIfFalse = conditionalExpression.IfFalse as ConstantExpression;

            /*
            Enabling this lines breaks
            AsyncQueryTestBase.Where_bool_member_and_parameter_compared_to_binary_expression_nested
            and other tests
            if (
                constantIfTrue != null && constantIfTrue.Type == typeof(bool) &&
                constantIfFalse != null && constantIfFalse.Type == typeof(bool)
                )
            {
                // Just return true or false
                Visit(conditionalExpression.Test);
                return conditionalExpression;
            }
            */

            Sql.AppendLine("IIf(");

            using (Sql.Indent())
            {
                Visit(conditionalExpression.Test);

                Sql.Append(",");
                Sql.AppendLine();

                if (constantIfTrue != null && constantIfTrue.Type == typeof(bool))
                {
                    Sql.Append((bool)constantIfTrue.Value ? TypedTrueLiteral : TypedFalseLiteral);
                }
                else
                {
                    Visit(conditionalExpression.IfTrue);
                }

                Sql.AppendLine(",");

                if (constantIfFalse != null && constantIfFalse.Type == typeof(bool))
                {
                    Sql.Append((bool)constantIfFalse.Value ? TypedTrueLiteral : TypedFalseLiteral);
                }
                else
                {
                    Visit(conditionalExpression.IfFalse);
                }

                Sql.AppendLine();
            }

            Sql.Append(")");

            return conditionalExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
        {
            Check.NotNull(rowNumberExpression, nameof(rowNumberExpression));

            Sql.Append("ROW_NUMBER() OVER(");
            GenerateOrderBy(rowNumberExpression.Orderings);
            Sql.Append(")");

            return rowNumberExpression;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
            if (sqlFunctionExpression.FunctionName.StartsWith("@@", StringComparison.Ordinal))
            {
                Sql.Append(sqlFunctionExpression.FunctionName);

                return sqlFunctionExpression;
            }

            return base.VisitSqlFunction(sqlFunctionExpression);
        }

        protected override void GenerateProjection(Expression projection)
        {
            var aliasedProjection = projection as AliasExpression;
            var expressionToProcess = aliasedProjection?.Expression ?? projection;
            var updatedExpression = ExplicitCastToBool(expressionToProcess);

            expressionToProcess = aliasedProjection != null
                ? new AliasExpression(aliasedProjection.Alias, updatedExpression)
                : updatedExpression;

            base.GenerateProjection(expressionToProcess);
        }

        private Expression ExplicitCastToBool(Expression expression)
        {
            return (expression as BinaryExpression)?.NodeType == ExpressionType.Coalesce
                   && expression.Type.UnwrapNullableType() == typeof(bool)
                ? new ExplicitCastExpression(expression, expression.Type)
                : expression;
        }

    }
}
