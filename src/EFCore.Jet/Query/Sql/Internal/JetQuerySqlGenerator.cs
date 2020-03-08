// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Jet;
using System.Linq;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Query.Expressions.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Sql.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetQuerySqlGenerator : QuerySqlGenerator, IJetExpressionVisitor
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        private static readonly Dictionary<string, string> _convertMappings = new Dictionary<string, string>
        {
            {nameof(Boolean), "CBool"},
            {nameof(SByte), "CInt"},
            {nameof(Byte), "CByte"},
            {nameof(Int16), "CInt"},
            {nameof(Int32), "CLng"},
            {nameof(Single), "CSng"},
            {nameof(Double), "CDbl"},
            {nameof(Decimal), "CCur"},
            {nameof(DateTime), "CDate"},
        };

        private readonly ISqlGenerationHelper _sqlGenerationHelper;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGenerator(
            [NotNull] QuerySqlGeneratorDependencies dependencies,
            ISqlExpressionFactory sqlExpressionFactory)
            : base(dependencies)
        {
            _sqlExpressionFactory = (JetSqlExpressionFactory) sqlExpressionFactory;
            _sqlGenerationHelper = dependencies.SqlGenerationHelper;
        }

        protected override Expression VisitSelect(SelectExpression selectExpression)
        {
            // Copy & pasted from `QuerySqlGenerator` to implement Jet's non-standard JOIN syntax and DUAL table
            // workaround.
            // Should be kept in sync with the base class.

            if (IsNonComposedSetOperation(selectExpression))
            {
                // Naked set operation
                GenerateSetOperation((SetOperationBase) selectExpression.Tables[0]);

                return selectExpression;
            }

            IDisposable subQueryIndent = null;

            if (selectExpression.Alias != null)
            {
                Sql.AppendLine("(");
                subQueryIndent = Sql.Indent();
            }

            Sql.Append("SELECT ");

            if (selectExpression.IsDistinct)
            {
                Sql.Append("DISTINCT ");
            }

            GenerateTop(selectExpression);

            if (selectExpression.Projection.Any())
            {
                GenerateList(selectExpression.Projection, e => Visit(e));
            }
            else
            {
                Sql.Append("1");
            }

            // Implement Jet's non-standard JOIN syntax and DUAL table workaround.
            if (selectExpression.Tables.Any())
            {
                Sql.AppendLine()
                    .Append("FROM ");

                Sql.Append(
                    new string(
                        '(', Math.Max(
                            0, selectExpression
                                .Tables
                                .Count(t => !(t is CrossJoinExpression || t is CrossApplyExpression)) - 1)));

                for (var index = 0; index < selectExpression.Tables.Count; index++)
                {
                    TableExpressionBase tableExpression = selectExpression.Tables[index];
                    Visit(tableExpression);
                    if (!(tableExpression is CrossJoinExpression || tableExpression is CrossApplyExpression))
                    {
                        if (index > 0)
                            Sql.Append(")");
                    }

                    if (index != selectExpression.Tables.Count - 1)
                        Sql.AppendLine();
                }
            }
            else
            {
                GeneratePseudoFromClause();
            }

            if (selectExpression.Predicate != null)
            {
                Sql.AppendLine()
                    .Append("WHERE ");

                Visit(selectExpression.Predicate);
            }

            if (selectExpression.GroupBy.Count > 0)
            {
                Sql.AppendLine()
                    .Append("GROUP BY ");

                GenerateList(selectExpression.GroupBy, e => Visit(e));
            }

            if (selectExpression.Having != null)
            {
                Sql.AppendLine()
                    .Append("HAVING ");

                Visit(selectExpression.Having);
            }

            GenerateOrderings(selectExpression);
            GenerateLimitOffset(selectExpression);

            if (selectExpression.Alias != null)
            {
                subQueryIndent.Dispose();

                Sql.AppendLine()
                    .Append(")" + AliasSeparator + _sqlGenerationHelper.DelimitIdentifier(selectExpression.Alias));
            }

            return selectExpression;
        }

        private bool IsNonComposedSetOperation(SelectExpression selectExpression)
            => selectExpression.Offset == null
               && selectExpression.Limit == null
               && !selectExpression.IsDistinct
               && selectExpression.Predicate == null
               && selectExpression.Having == null
               && selectExpression.Orderings.Count == 0
               && selectExpression.GroupBy.Count == 0
               && selectExpression.Tables.Count == 1
               && selectExpression.Tables[0] is SetOperationBase setOperation
               && selectExpression.Projection.Count == setOperation.Source1.Projection.Count
               && selectExpression.Projection.Select(
                       (pe, index) => pe.Expression is ColumnExpression column
                                      && string.Equals(column.Table.Alias, setOperation.Alias, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(
                                          column.Name, setOperation.Source1.Projection[index]
                                              .Alias, StringComparison.OrdinalIgnoreCase))
                   .All(e => e);

        private void GeneratePseudoFromClause()
        {
            Sql.AppendLine()
                .Append("FROM " + JetConfiguration.DUAL);
        }

        private void GenerateList<T>(
            IReadOnlyList<T> items,
            Action<T> generationAction,
            Action<IRelationalCommandBuilder> joinAction = null)
        {
            joinAction ??= (isb => isb.Append(", "));

            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    joinAction(Sql);
                }

                generationAction(items[i]);
            }
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            Check.NotNull(sqlBinaryExpression, nameof(sqlBinaryExpression));

            if (sqlBinaryExpression.OperatorType == ExpressionType.Coalesce)
            {
                Sql.Append("IIf(");
                Visit(sqlBinaryExpression.Left);
                Sql.Append(" IS NULL, ");
                Visit(sqlBinaryExpression.Right);
                Sql.Append(", ");
                Visit(sqlBinaryExpression.Left);
                Sql.Append(")");
                return sqlBinaryExpression;
            }

            return base.VisitSqlBinary(sqlBinaryExpression);
        }

        protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
            => sqlUnaryExpression.OperatorType == ExpressionType.Convert
                ? VisitJetConvertExpression(sqlUnaryExpression)
                : base.VisitSqlUnary(sqlUnaryExpression);

        protected Expression VisitJetConvertExpression(SqlUnaryExpression convertExpression)
        {
            var typeMapping = convertExpression.TypeMapping;

            if (typeMapping == null)
                throw new InvalidOperationException(RelationalStrings.UnsupportedType(convertExpression.Type.ShortDisplayName()));

            if (_convertMappings.TryGetValue(typeMapping.ClrType.Name, out var function))
            {
                Visit(
                    _sqlExpressionFactory.JetNullChecked(
                        _sqlExpressionFactory.Function(
                            function,
                            new[] {convertExpression.Operand},
                            typeMapping.ClrType)));

                return convertExpression;
            }

            if (typeMapping.ClrType.Name == nameof(String))
            {
                Sql.Append("(");
                Visit(convertExpression.Operand);
                Sql.Append(@" & """")");
                return convertExpression;
            }

            throw new InvalidOperationException($"Cannot cast to CLR type '{typeMapping.ClrType.Name}' with Jet.");
        }

        /// <summary>
        ///     Visit a LikeExpression.
        /// </summary>
        /// <param name="likeExpression"> The like expression. </param>
        /// <returns>
        ///     An Expression.
        /// </returns>
        protected override Expression VisitLike(LikeExpression likeExpression)
        {
            Check.NotNull(likeExpression, nameof(likeExpression));

            if (likeExpression.EscapeChar != null)
                base.VisitLike(_sqlExpressionFactory.Like(likeExpression.Match, likeExpression.Pattern));
            else
                base.VisitLike(likeExpression);

            return likeExpression;
        }
        
        protected override string GenerateOperator(SqlBinaryExpression e)
        {
            return e.OperatorType switch
            {
                ExpressionType.Add when e.Type == typeof(string) => " & ",
                ExpressionType.And => " BAND ",
                ExpressionType.Modulo => " MOD ",
                ExpressionType.Or => " BOR ",
                _ => base.GenerateOperator(e),
            };
        }
        
        protected override Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
        {
            Sql.Append(", ");
            Visit(crossJoinExpression.Table);

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
            // LIMIT is not natively supported by Jet.
            // The System.Data.Jet tries to mitigate this by supporting a proprietary extension SKIP, but can easily
            // fail, e.g. when the SKIP happens in a subquery.

            if (selectExpression.Offset == null)
               return;

            // CHECK: Needed?
            if (!selectExpression.Orderings.Any())
                Sql.AppendLine()
                    .Append("ORDER BY 0");

            Sql.AppendLine()
               .Append("SKIP ");
            Visit(selectExpression.Offset);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
            if (sqlFunctionExpression.Name.StartsWith("@@", StringComparison.Ordinal))
            {
                Sql.Append(sqlFunctionExpression.Name);
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == "Pow")
            {
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append("^");
                Visit(sqlFunctionExpression.Arguments[1]);
                return sqlFunctionExpression;
            }

            return base.VisitSqlFunction(sqlFunctionExpression);
        }
        
        protected override Expression VisitCase(CaseExpression caseExpression)
        {
            using (Sql.Indent())
            {
                foreach (var whenClause in caseExpression.WhenClauses)
                {
                    Sql.Append("IIf(");

                    if (caseExpression.Operand != null)
                    {
                        Visit(caseExpression.Operand);
                        Sql.Append(" = ");
                    }

                    Visit(whenClause.Test);
                    
                    Sql.Append(", ");
                    
                    Visit(whenClause.Result);
                    
                    Sql.Append(", ");
                }

                if (caseExpression.ElseResult != null)
                {
                    Visit(caseExpression.ElseResult);
                }
                else
                {
                    Sql.Append("NULL");
                }

                Sql.Append(new string(')', caseExpression.WhenClauses.Count));
            }

            return caseExpression;
        }

        public Expression VisitJetIsNull(IsNullSqlExpression isNullSqlExpression)
        {
            Visit(isNullSqlExpression.NullableExpression);
            Sql.Append(" IS NULL");

            return isNullSqlExpression;
        }
    }
}