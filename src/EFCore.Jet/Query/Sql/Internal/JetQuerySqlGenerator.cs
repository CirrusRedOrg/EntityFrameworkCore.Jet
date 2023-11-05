// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Jet.Data;
using System.Linq;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage.Internal;

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
            { nameof(Boolean), "CBOOL" },
            { nameof(Byte), "CBYTE" },
            { nameof(SByte), "CINT" },
            { nameof(Int16), "CINT" },
            { nameof(Int32), "CLNG" },
            { nameof(Int64), "CLNG" },
            { nameof(Single), "CSNG" },
            { nameof(Double), "CDBL" },
            { nameof(Decimal), "CDEC" },
            { nameof(DateTime), "CDATE" },
        };

        private readonly ITypeMappingSource _typeMappingSource;
        private readonly IJetOptions _options;

        private readonly ISqlGenerationHelper _sqlGenerationHelper;
        //private readonly JetSqlExpressionFactory _sqlExpressionFactory;
        private List<string> _nullNumerics = new List<string>();
        private Stack<Expression> parent = new Stack<Expression>();
        private CoreTypeMapping? _boolTypeMapping;
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGenerator(
            [JetBrains.Annotations.NotNull] QuerySqlGeneratorDependencies dependencies,
            //ISqlExpressionFactory sqlExpressionFactory,
            [JetBrains.Annotations.NotNull] ITypeMappingSource typeMappingSource,
            IJetOptions options)
            : base(dependencies)
        {
            //_sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;
            _typeMappingSource = typeMappingSource;
            _options = options;
            _sqlGenerationHelper = dependencies.SqlGenerationHelper;
            _boolTypeMapping = _typeMappingSource.FindMapping(typeof(bool));
            //_jetSqlExpressionFactory = jetSqlExpressionFactory;
        }

        protected override Expression VisitSelect(SelectExpression selectExpression)
        {
            // Copy & pasted from `QuerySqlGenerator` to implement Jet's non-standard JOIN syntax and DUAL table
            // workaround.
            // Should be kept in sync with the base class.

            IDisposable? subQueryIndent = null;

            if (selectExpression.Alias != null)
            {
                Sql.AppendLine("(");
                subQueryIndent = Sql.Indent();
            }

            if (!TryGenerateWithoutWrappingSelect(selectExpression))
            {

                Sql.Append("SELECT ");

                if (selectExpression.IsDistinct)
                {
                    Sql.Append("DISTINCT ");
                }

                if (selectExpression.Tags.Contains("DeepSkip"))
                {

                }
                else
                {
                    GenerateTop(selectExpression);
                }


                if (selectExpression.Projection.Any())
                {
                    GenerateList(selectExpression.Projection, e => Visit(e));
                }
                else
                {
                    Sql.Append("1");
                }

                List<ColumnExpression> colexp = new List<ColumnExpression>();
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
                                    .Count(t => !(t is CrossJoinExpression || t is CrossApplyExpression)) -
                                maxTablesWithoutBrackets)));

                    for (var index = 0; index < selectExpression.Tables.Count; index++)
                    {
                        var tableExpression = selectExpression.Tables[index];

                        var isApplyExpression = tableExpression is CrossApplyExpression ||
                                                tableExpression is OuterApplyExpression;

                        var isCrossExpression = tableExpression is CrossJoinExpression ||
                                                tableExpression is CrossApplyExpression;

                        if (isApplyExpression)
                        {
                            throw new UnreachableException();
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

                        List<ColumnExpression> tempcolexp;
                        if (tableExpression is InnerJoinExpression expression)
                        {
                            SqlBinaryExpression? binaryJoin = expression.JoinPredicate as SqlBinaryExpression;
                            SqlUnaryExpression? unaryJoin = expression.JoinPredicate as SqlUnaryExpression;
                            if (binaryJoin != null)
                            {
                                tempcolexp = ExtractColumnExpressions(binaryJoin!);
                            }
                            else if (unaryJoin != null)
                            {
                                tempcolexp = ExtractColumnExpressions(unaryJoin!);
                            }
                            else
                            {
                                tempcolexp = new List<ColumnExpression>();
                            }

                            bool refrencesfirsttable = false;
                            foreach (ColumnExpression col in tempcolexp)
                            {
                                if (col.Table == selectExpression.Tables[0])
                                {
                                    refrencesfirsttable = true;
                                    break;
                                }
                            }

                            if (refrencesfirsttable)
                            {
                                Visit(tableExpression);
                                continue;
                            }
                            else
                            {
                                colexp.AddRange(tempcolexp);
                            }

                            /*if (expression.JoinPredicate is SqlBinaryExpression { Left: ColumnExpression left, Right: ColumnExpression right })
                            {
                                var lt = left.Table == selectExpression.Tables[0];
                                var rt = right.Table == selectExpression.Tables[0];
                                if (lt || rt)
                                {
                                    Visit(tableExpression);
                                    continue;
                                }
                                else
                                {
                                    colexp.Add(left);
                                    colexp.Add(right);
                                }
                            }*/
                            Sql.Append("LEFT JOIN ");
                            Visit(expression.Table);
                            Sql.Append(" ON ");
                            Visit(expression.JoinPredicate);
                        }
                        else
                        {
                            Visit(tableExpression);
                        }
                    }
                }
                else
                {
                    GeneratePseudoFromClause();
                }

                if (selectExpression.Predicate != null || colexp.Count > 0)
                {
                    Sql.AppendLine()
                        .Append("WHERE ");

                    if (selectExpression.Predicate != null)
                    {
                        if (colexp.Count > 0) Sql.Append("(");
                        Visit(selectExpression.Predicate);
                        if (colexp.Count > 0) Sql.Append(")");
                    }

                    if (selectExpression.Predicate != null && colexp.Count > 0)
                    {
                        Sql.Append(" AND (");
                    }

                    if (colexp.Count > 0)
                    {
                        int ct = 0;
                        foreach (var exp in colexp)
                        {
                            if (!string.IsNullOrEmpty(exp.TableAlias))
                            {
                                Sql.Append($"`{exp.TableAlias}`.");
                            }

                            Sql.Append($"`{exp.Name}` IS NOT NULL");
                            if (ct < colexp.Count - 1)
                            {
                                ct++;
                                Sql.Append(" AND ");
                            }
                        }
                    }

                    if (selectExpression.Predicate != null && colexp.Count > 0)
                    {
                        Sql.Append(")");
                    }
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

            }

            if (selectExpression.Alias != null)
            {
                subQueryIndent!.Dispose();

                Sql.AppendLine()
                    .Append(")")
                    .Append(AliasSeparator)
                    .Append(_sqlGenerationHelper.DelimitIdentifier(selectExpression.Alias));
            }

            return selectExpression;
        }

        private List<ColumnExpression> ExtractColumnExpressions(SqlBinaryExpression binaryexp)
        {
            List<ColumnExpression> result = new List<ColumnExpression>();
            if (binaryexp.Left is SqlBinaryExpression left)
            {
                result.AddRange(ExtractColumnExpressions(left));
            }
            else if (binaryexp.Left is ColumnExpression colLeft)
            {
                result.Add(colLeft);
            }

            if (binaryexp.Right is SqlBinaryExpression right)
            {
                result.AddRange(ExtractColumnExpressions(right));
            }
            else if (binaryexp.Right is ColumnExpression colRight)
            {
                result.Add(colRight);
            }

            return result;
        }
        private List<ColumnExpression> ExtractColumnExpressions(SqlUnaryExpression unaryexp)
        {
            List<ColumnExpression> result = new List<ColumnExpression>();
            if (unaryexp.Operand is SqlBinaryExpression left)
            {
                result.AddRange(ExtractColumnExpressions(left));
            }
            else if (unaryexp.Operand is ColumnExpression colLeft)
            {
                result.Add(colLeft);
            }

            return result;
        }

        protected override Expression VisitProjection(ProjectionExpression projectionExpression)
        {
            if (projectionExpression.Expression is SqlConstantExpression { Value: null } constantExpression && (constantExpression.Type == typeof(int) || constantExpression.Type == typeof(double) || constantExpression.Type == typeof(float) || constantExpression.Type == typeof(decimal) || constantExpression.Type == typeof(short)))
            {
                _nullNumerics.Add(projectionExpression.Alias);
            }
            parent.Push(projectionExpression);
            var result = base.VisitProjection(projectionExpression);
            parent.Pop();
            return result;
        }

        protected override Expression VisitColumn(ColumnExpression columnExpression)
        {
            if (columnExpression.IsNullable && _nullNumerics.Contains(columnExpression.Name) && _convertMappings.TryGetValue(columnExpression.Type.Name, out var function))
            {

                if (parent.TryPeek(out var exp) && exp is SqlBinaryExpression)
                {
                    Sql.Append(function);
                    Sql.Append("(");
                    base.VisitColumn(columnExpression);
                    Sql.Append(")");
                    return columnExpression;
                }
            }
            return base.VisitColumn(columnExpression);
        }

        protected override Expression VisitJsonScalar(JsonScalarExpression jsonScalarExpression)
        {
            var path = jsonScalarExpression.Path;
            if (path.Count == 0)
            {
                Visit(jsonScalarExpression.Json);
                return jsonScalarExpression;
            }

            throw new UnreachableException();
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
                                      && string.Equals(column.Table.Alias, setOperation.Alias,
                                          StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(
                                          column.Name, setOperation.Source1.Projection[index]
                                              .Alias, StringComparison.OrdinalIgnoreCase))
                   .All(e => e);

        protected override void GeneratePseudoFromClause()
        {
            Sql.AppendLine()
                .Append("FROM " + "(SELECT COUNT(*) FROM `" + (string.IsNullOrEmpty(JetConfiguration.CustomDualTableName) ? JetConfiguration.DetectedDualTableName : JetConfiguration.CustomDualTableName) + "`)");
        }

        private void GenerateList<T>(
            IReadOnlyList<T> items,
            Action<T> generationAction,
            Action<IRelationalCommandBuilder>? joinAction = null)
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

            if (orderingExpression.Expression.TypeMapping?.GetType() == typeof(JetBoolTypeMapping))
            {
                orderingExpression = new OrderingExpression(
                    new SqlUnaryExpression(
                        ExpressionType.Not,
                        orderingExpression.Expression,
                        orderingExpression.Expression.Type,
                        orderingExpression.Expression.TypeMapping),
                    orderingExpression.IsAscending);
            }

            if (orderingExpression.Expression is SqlConstantExpression or SqlParameterExpression)
            {
                Sql.Append("1");
            }
            else
            {
                Visit(orderingExpression.Expression);
            }

            if (!orderingExpression.IsAscending)
            {
                Sql.Append(" DESC");
            }

            return orderingExpression;
        }

        protected override Expression VisitSqlParameter(SqlParameterExpression sqlParameterExpression)
        {
            if (sqlParameterExpression.Type == typeof(DateTime) && sqlParameterExpression.TypeMapping is JetDateTimeTypeMapping or NullTypeMapping)
            {
                Sql.Append("CDATE(");
                base.VisitSqlParameter(sqlParameterExpression);
                Sql.Append(")");
                return sqlParameterExpression;
            }
            return base.VisitSqlParameter(sqlParameterExpression);
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            Check.NotNull(sqlBinaryExpression, nameof(sqlBinaryExpression));

            if (sqlBinaryExpression.OperatorType == ExpressionType.Coalesce)
            {
                SqlConstantExpression nullcons = new SqlConstantExpression(Expression.Constant(null), RelationalTypeMapping.NullMapping);
                SqlUnaryExpression isnullexp = new SqlUnaryExpression(ExpressionType.Equal, sqlBinaryExpression.Left, typeof(bool), null);
                List<CaseWhenClause> whenclause = new List<CaseWhenClause>
                {
                    new CaseWhenClause(isnullexp, sqlBinaryExpression.Right)
                };
                CaseExpression caseexp = new CaseExpression(whenclause, sqlBinaryExpression.Left);
                Visit(caseexp);
                return sqlBinaryExpression;
            }
            parent.Push(sqlBinaryExpression);
            var res = base.VisitSqlBinary(sqlBinaryExpression);
            parent.Pop();
            return res;
        }

        protected override void GenerateIn(InExpression inExpression, bool negated)
        {
            ///TODO: recheck how this works in net 8
            /*var valuesConstant = (SqlConstantExpression?)inExpression.Values;
            if (valuesConstant != null)
            {
                var isdt = (IEnumerable<object>)valuesConstant?.Value!;
                var enumerable = isdt.ToList();
                if (enumerable.Any())
                {
                    var dtf = enumerable.FirstOrDefault();
                    //Need to use a specific Jet DateTime format - when used in an IN section the mapping isn't automatic so set it up explicitly
                    if (dtf is DateTime)
                    {
                        var newexp = new InExpression(inExpression.Item,
                            valuesConstant!.ApplyTypeMapping(new JetDateTimeTypeMapping("datetime", _options)),
                            new JetDateTimeTypeMapping("datetime", _options));
                        base.GenerateIn(newexp, negated);
                    }
                }
            }*/

            base.GenerateIn(inExpression, negated);
        }

        protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
        {
            if (sqlConstantExpression.TypeMapping == RelationalTypeMapping.NullMapping && sqlConstantExpression.Value is DateTime)
            {
                sqlConstantExpression = (SqlConstantExpression)sqlConstantExpression.ApplyTypeMapping(new JetDateTimeTypeMapping("datetime", _options));
            }

            parent.TryPeek(out var exp);
            if (sqlConstantExpression.Value is null && exp is ProjectionExpression && (sqlConstantExpression.Type.IsNumeric() || sqlConstantExpression.Type == typeof(bool)))
            {
                Sql.Append("CVar(");
                Sql.Append(sqlConstantExpression.TypeMapping!.GenerateSqlLiteral(sqlConstantExpression.Value));
                Sql.Append(")");
                return sqlConstantExpression;
            }
            return base.VisitSqlConstant(sqlConstantExpression);
        }

        protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
        {
            if (sqlUnaryExpression.OperatorType == ExpressionType.Convert)
            {
                return VisitJetConvertExpression(sqlUnaryExpression);
            }
            else if (sqlUnaryExpression.OperatorType == ExpressionType.Not && sqlUnaryExpression.Type != typeof(bool))
            {
                Sql.Append(" (BNOT");

                var requiresBrackets = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append(")");
                }

                Sql.Append(")");

                return sqlUnaryExpression;
            }
            return base.VisitSqlUnary(sqlUnaryExpression);
        }


        protected Expression VisitJetConvertExpression(SqlUnaryExpression convertExpression)
        {
            var typeMapping = convertExpression.TypeMapping;

            if (typeMapping == null)
                throw new InvalidOperationException(
                    RelationalStrings.UnsupportedType(convertExpression.Type.ShortDisplayName()));

            // We are explicitly converting to the target type (convertExpression.Type) and not the CLR type of the
            // accociated type mapping. This allows for conversions on the database side (e.g. CDBL()) but handling
            // of the returned value using a different (unaligned) type mapping (e.g. date/time related ones).
            if (_convertMappings.TryGetValue(convertExpression.Type.Name, out var function))
            {
                /*  Visit(
                    _sqlExpressionFactory.NullChecked(
                        convertExpression.Operand,
                        _sqlExpressionFactory.Function(
                            function,
                            new[] { convertExpression.Operand },
                            false,
                            new[] { false },
                            typeMapping.ClrType)));
                */
                SqlExpression checksqlexp = convertExpression.Operand;

                SqlFunctionExpression notnullsqlexp = new SqlFunctionExpression(function, new SqlExpression[] { convertExpression.Operand },
                  false, new[] { false }, typeMapping.ClrType, null);

                SqlConstantExpression nullcons = new SqlConstantExpression(Expression.Constant(null), RelationalTypeMapping.NullMapping);
                SqlUnaryExpression isnullexp = new SqlUnaryExpression(ExpressionType.Equal, checksqlexp, typeof(bool), null);
                List<CaseWhenClause> whenclause = new List<CaseWhenClause>
                {
                    new CaseWhenClause(isnullexp, nullcons)
                };
                CaseExpression caseexp = new CaseExpression(whenclause, notnullsqlexp);
                if (checksqlexp is ColumnExpression columnExpression)
                {
                    if (columnExpression.IsNullable)
                    {
                        Visit(caseexp);
                    }
                    else
                    {
                        Visit(notnullsqlexp);
                    }
                }
                else if (checksqlexp is SqlFunctionExpression functionExpression)
                {
                    if (functionExpression is { IsNullable: true, ArgumentsPropagateNullability: not null } && functionExpression.ArgumentsPropagateNullability.Any(d => d))
                    {
                        Visit(caseexp);
                    }
                    else
                    {
                        Visit(notnullsqlexp);
                    }
                }
                else if (checksqlexp is SqlBinaryExpression binaryExpression)
                {
                    bool leftnull = false, rightnull = false;
                    ColumnExpression? columnExpressionLeft = binaryExpression.Left as ColumnExpression;
                    SqlFunctionExpression? functionExpressionLeft = binaryExpression.Left as SqlFunctionExpression;
                    ColumnExpression? columnExpressionRight = binaryExpression.Right as ColumnExpression;
                    SqlFunctionExpression? functionExpressionRight = binaryExpression.Right as SqlFunctionExpression;
                    leftnull = columnExpressionLeft != null && columnExpressionLeft.IsNullable ||
                               functionExpressionLeft != null && functionExpressionLeft.IsNullable;
                    rightnull = columnExpressionRight != null && columnExpressionRight.IsNullable ||
                                functionExpressionRight != null && functionExpressionRight.IsNullable;

                    if (leftnull || rightnull)
                    {
                        Visit(caseexp);
                    }
                    else
                    {
                        Visit(notnullsqlexp);
                    }
                }
                else if (checksqlexp is SqlUnaryExpression unaryExpression)
                {
                    Visit(notnullsqlexp);
                }
                else if (checksqlexp is SqlConstantExpression { Value: not null })
                {
                    Visit(notnullsqlexp);
                }
                else
                {
                    Visit(caseexp);
                }

                return notnullsqlexp;
            }

            if (typeMapping.ClrType.Name == nameof(String))
            {
                Sql.Append("(");
                Visit(convertExpression.Operand);
                Sql.Append(@" & '')");
                return convertExpression;
            }

            //Just pass the operand in the default case
            //If we have a type mapping on the operand, then it seems to work fine
            //Jet appears to be fairly flexible when types aren't specifically mentioned
            //Keep an eye on this for any further problems - doesn't show anything in the tests right now
            Visit(convertExpression.Operand);
            return convertExpression;
        }

        protected override string GetOperator([JetBrains.Annotations.NotNull] SqlBinaryExpression binaryExpression)
            => binaryExpression.OperatorType switch
            {
                ExpressionType.Add when binaryExpression.Type == typeof(string) => " & ",
                ExpressionType.And => " BAND ",
                ExpressionType.Modulo => " MOD ",
                ExpressionType.Or => " BOR ",
                ExpressionType.Not => " BNOT ",
                ExpressionType.Divide when binaryExpression.Type == typeof(Int32) => " \\ ",
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
                    throw new InvalidOperationException(
                        "Jet does not support skipping rows. Switch to client evaluation explicitly by inserting a call to either AsEnumerable(), AsAsyncEnumerable(), ToList(), or ToListAsync() if needed.");
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

            if (sqlFunctionExpression.Name.Equals("POW", StringComparison.OrdinalIgnoreCase) && sqlFunctionExpression.Arguments != null)
            {
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append("^");
                Visit(sqlFunctionExpression.Arguments[1]);
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name.Equals("COALESCE", StringComparison.OrdinalIgnoreCase) && sqlFunctionExpression.Arguments != null && sqlFunctionExpression.Arguments.Count > 1)
            {
                int start = sqlFunctionExpression.Arguments.Count - 1;
                CaseExpression? lastcaseexp = null;
                for (int A = start; A >= 1; A--)
                {
                    SqlUnaryExpression isnullexp = new SqlUnaryExpression(ExpressionType.Equal, sqlFunctionExpression.Arguments[A - 1], typeof(bool), null);
                    List<CaseWhenClause> whenclause = new List<CaseWhenClause>
                    {
                        new CaseWhenClause(isnullexp, lastcaseexp ?? sqlFunctionExpression.Arguments[A])
                    };
                    lastcaseexp = new CaseExpression(whenclause, sqlFunctionExpression.Arguments[A - 1]);
                }

                Visit(lastcaseexp);
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name.Equals("MID", StringComparison.OrdinalIgnoreCase) &&
                sqlFunctionExpression.Arguments != null && sqlFunctionExpression.Arguments.Count > 2)
            {
                if (sqlFunctionExpression.Arguments[2] is ColumnExpression { IsNullable: true })
                {
                    Sql.Append("IIF(");
                    Visit(sqlFunctionExpression.Arguments[2]);
                    Sql.Append(" IS NULL, NULL, ");
                    base.VisitSqlFunction(sqlFunctionExpression);
                    Sql.Append(")");
                    return sqlFunctionExpression;
                }
                if (sqlFunctionExpression.Arguments[2] is SqlUnaryExpression { OperatorType: ExpressionType.Convert } unaryExpression)
                {
                    if (unaryExpression.Operand is ColumnExpression { IsNullable: true } || unaryExpression.Operand is SqlFunctionExpression { IsNullable: true })
                    {
                        Sql.Append("IIF(");
                        Visit(unaryExpression.Operand);
                        Sql.Append(" IS NULL, NULL, ");
                        base.VisitSqlFunction(sqlFunctionExpression);
                        Sql.Append(")");
                        return sqlFunctionExpression;
                    }
                }
            }
            parent.Push(sqlFunctionExpression);
            var result = base.VisitSqlFunction(sqlFunctionExpression);
            parent.Pop();
            return result;
        }

        protected override Expression VisitCase(CaseExpression caseExpression)
        {
            using (Sql.Indent())
            {
                parent.Push(caseExpression);
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
                parent.Pop();
            }

            return caseExpression;
        }

        protected override Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
            => throw new UnreachableException();
    }
}