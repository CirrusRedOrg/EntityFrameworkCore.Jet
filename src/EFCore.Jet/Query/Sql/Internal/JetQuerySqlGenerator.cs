// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Data;
using System.Linq;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
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
        private static readonly Dictionary<string, string> _convertMappings = new Dictionary<string, string>
        {
            {nameof(Boolean), "CBOOL"},
            {nameof(Byte), "CBYTE"},
            {nameof(SByte), "CINT"},
            {nameof(Int16), "CINT"},
            {nameof(Int32), "CLNG"},
            {nameof(Single), "CSNG"},
            {nameof(Double), "CDBL"},
            {nameof(Decimal), "CCUR"},
            {nameof(DateTime), "CDATE"},
        };

        private readonly ITypeMappingSource _typeMappingSource;
        private readonly IJetOptions _options;
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;
        private readonly ISqlGenerationHelper _sqlGenerationHelper;
        private CoreTypeMapping _boolTypeMapping;
        
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGenerator(
            [NotNull] QuerySqlGeneratorDependencies dependencies,
            ISqlExpressionFactory sqlExpressionFactory,
            ITypeMappingSource typeMappingSource,
            IJetOptions options)
            : base(dependencies)
        {
            _sqlExpressionFactory = (JetSqlExpressionFactory) sqlExpressionFactory;
            _typeMappingSource = typeMappingSource;
            _options = options;
            _sqlGenerationHelper = dependencies.SqlGenerationHelper;
            _boolTypeMapping = _typeMappingSource.FindMapping(typeof(bool));
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
            // TODO: This does not properly handle all cases (especially when cross joins are involved).
            if (selectExpression.Tables.Any())
            {
                Sql.AppendLine()
                    .Append("FROM ");

                const int maxTablesWithoutBrackets = 2;

                Sql.Append(
                    new string(
                        '(',
                        Math.Max(
                            0,
                            selectExpression
                                .Tables
                                .Count(t => !(t is CrossJoinExpression || t is CrossApplyExpression)) - maxTablesWithoutBrackets)));

                for (var index = 0; index < selectExpression.Tables.Count; index++)
                {
                    var tableExpression = selectExpression.Tables[index];

                    var isApplyExpression = tableExpression is CrossApplyExpression ||
                                            tableExpression is OuterApplyExpression;
                    
                    var isCrossExpression = tableExpression is CrossJoinExpression ||
                                            tableExpression is CrossApplyExpression;

                    if (isApplyExpression)
                    {
                        throw new InvalidOperationException("Jet does not support APPLY statements. Switch to client evaluation explicitly by inserting a call to either AsEnumerable(), AsAsyncEnumerable(), ToList(), or ToListAsync() if needed.");
                    }

                    if (index > 0)
                    {
                        if (isCrossExpression)
                        {
                            Sql.Append(",");
                        }
                        else if (index >= maxTablesWithoutBrackets)
                        {
                            Sql.Append(")");
                        }

                        Sql.AppendLine();
                    }

                    Visit(tableExpression);
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

        protected override void GeneratePseudoFromClause()
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

        protected override Expression VisitOrdering(OrderingExpression orderingExpression)
        {
            // Jet uses the value -1 as True, so ordering by a boolean expression will first list the True values
            // before the False values, which is the opposite of what .NET and other DBMS do, which are using 1 as True.
            
            if (orderingExpression.Expression.TypeMapping == _boolTypeMapping)
            {
                orderingExpression = new OrderingExpression(
                    orderingExpression.Expression,
                    !orderingExpression.IsAscending);
            }
            
            return base.VisitOrdering(orderingExpression);
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            Check.NotNull(sqlBinaryExpression, nameof(sqlBinaryExpression));

            if (sqlBinaryExpression.OperatorType == ExpressionType.Coalesce)
            {
                Visit(
                    _sqlExpressionFactory.Case(
                        new[]
                        {
                            new CaseWhenClause(
                                _sqlExpressionFactory.IsNull(sqlBinaryExpression.Left),
                                sqlBinaryExpression.Right)
                        },
                        sqlBinaryExpression.Left));
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

            // We are explicitly converting to the target type (convertExpression.Type) and not the CLR type of the
            // accociated type mapping. This allows for conversions on the database side (e.g. CDBL()) but handling
            // of the returned value using a different (unaligned) type mapping (e.g. date/time related ones).
            if (_convertMappings.TryGetValue(convertExpression.Type.Name, out var function))
            {
                Visit(
                    _sqlExpressionFactory.NullChecked(
                        convertExpression.Operand,
                        _sqlExpressionFactory.Function(
                            function,
                            new[] {convertExpression.Operand},
                            false,
                            new[] {false}, 
                            typeMapping.ClrType)));

                return convertExpression;
            }

            if (typeMapping.ClrType.Name == nameof(String))
            {
                Sql.Append("(");
                Visit(convertExpression.Operand);
                Sql.Append(@" & '')");
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

        protected override string GetOperator([NotNull] SqlBinaryExpression binaryExpression)
            => binaryExpression.OperatorType switch
            {
                ExpressionType.Add when binaryExpression.Type == typeof(string) => " & ",
                ExpressionType.And => " BAND ",
                ExpressionType.Modulo => " MOD ",
                ExpressionType.Or => " BOR ",
                _ => base.GetOperator(binaryExpression),
            };

        protected override Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
        {
            Visit(crossJoinExpression.Table);
            return crossJoinExpression;
        }

        /// <summary>Generates the TOP part of the SELECT statement,</summary>
        /// <param name="selectExpression"> The select expression. </param>
        protected override void GenerateTop(SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            if (selectExpression.Offset != null)
            {
                if (_options.UseOuterSelectSkipEmulationViaDataReader)
                {
                    Sql.Append("SKIP ");
                    Visit(selectExpression.Offset);
                    Sql.Append(" ");
                }
                else
                {
                    throw new InvalidOperationException("Jet does not support skipping rows. Switch to client evaluation explicitly by inserting a call to either AsEnumerable(), AsAsyncEnumerable(), ToList(), or ToListAsync() if needed.");
                }
            }

            if (selectExpression.Limit != null)
            {
                Sql.Append("TOP ");
                Visit(selectExpression.Limit);
                Sql.Append(" ");
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void GenerateLimitOffset(SelectExpression selectExpression)
        {
            // This has already been applied by GenerateTop().
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

            if (sqlFunctionExpression.Name.Equals("POW", StringComparison.OrdinalIgnoreCase))
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
                    Sql.Append("IIF(");

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

        protected override Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
            => throw new InvalidOperationException(CoreStrings.TranslationFailed(rowNumberExpression));
    }
}