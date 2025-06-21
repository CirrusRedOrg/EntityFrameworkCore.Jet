// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Utilities;
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
        private static readonly Dictionary<string, string> _convertMappings = new()
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
            { nameof(TimeOnly), "TIMEVALUE" },
        };

        private readonly ITypeMappingSource _typeMappingSource;
        private readonly IJetOptions _options;

        private readonly ISqlGenerationHelper _sqlGenerationHelper;
        //private readonly JetSqlExpressionFactory _sqlExpressionFactory;
        private List<string> _nullNumerics = [];
        private Stack<Expression> parent = new();
        private CoreTypeMapping? _boolTypeMapping;
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGenerator(
            QuerySqlGeneratorDependencies dependencies,
            ITypeMappingSource typeMappingSource,
            IJetOptions options)
            : base(dependencies)
        {
            _typeMappingSource = typeMappingSource;
            _options = options;
            _sqlGenerationHelper = dependencies.SqlGenerationHelper;
            _boolTypeMapping = _typeMappingSource.FindMapping(typeof(bool));
        }

        protected override bool TryGenerateWithoutWrappingSelect(SelectExpression selectExpression)
        {
            parent.TryPeek(out var exp);
            if (exp is InExpression)
            {
                return false;
            }
            return base.TryGenerateWithoutWrappingSelect(selectExpression);
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

                GenerateTop(selectExpression);

                if (selectExpression.Projection.Any())
                {
                    GenerateList(selectExpression.Projection, e => Visit(e));
                }
                else
                {
                    if (selectExpression.Predicate == null)
                    {
                        Sql.Append("1");
                    }
                    else
                    {
                        //The WHERE clause can only refer to columns in the projection
                        List<ColumnExpression> cols = [];
                        if (selectExpression.Predicate is SqlBinaryExpression binaryExpression)
                        {
                            cols = ExtractColumnExpressions(binaryExpression);
                        }
                        else if (selectExpression.Predicate is SqlUnaryExpression unaryExpression)
                        {
                            cols = ExtractColumnExpressions(unaryExpression);
                        }

                        var collist = cols.Where(c =>
                            selectExpression.Tables.Select(d => d.Alias).Contains(c.TableAlias)).ToList();
                        parent.TryPeek(out var parentExpression);
                        if (selectExpression.Tables.Count > 1 && selectExpression.Tables.Any(c => c is InnerJoinExpression) && collist.Count > 0 && parentExpression is SqlUnaryExpression or SqlBinaryExpression)
                        {
                            Visit(collist[0]);
                        }
                        else
                        {
                            Sql.Append("1");
                        }
                    }
                }

                VisitJetTables(selectExpression.Tables, true, out var colexp);

                if (selectExpression.Predicate != null || colexp.Count > 0)
                {
                    Sql.AppendLine()
                        .Append("WHERE ");

                    if (selectExpression.Predicate != null)
                    {
                        if (colexp.Count > 0) Sql.Append("(");
                        parent.Push(selectExpression.Predicate);
                        Visit(selectExpression.Predicate);
                        parent.Pop();
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

        private void VisitJetTables(IReadOnlyList<TableExpressionBase> Tables, bool addfromsql, out List<ColumnExpression> colexp)
        {
            colexp = [];
            // Implement Jet's non-standard JOIN syntax and DUAL table workaround.
            // TODO: This does not properly handle all cases (especially when cross joins are involved).
            if (Tables.Any())
            {
                if (addfromsql)
                {
                    Sql.AppendLine().Append("FROM ");
                }

                const int maxTablesWithoutBrackets = 2;

                Sql.Append(
                    new string(
                        '(',
                        Math.Max(
                            0,
                            Tables
                                .Count(t => !(t is CrossJoinExpression or CrossApplyExpression)) -
                            maxTablesWithoutBrackets)));

                for (var index = 0; index < Tables.Count; index++)
                {
                    var tableExpression = Tables[index];

                    var isApplyExpression = tableExpression is CrossApplyExpression or OuterApplyExpression;

                    var isCrossExpression = tableExpression is CrossJoinExpression or CrossApplyExpression;

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
                        if (expression.JoinPredicate is SqlBinaryExpression binaryJoin)
                        {
                            tempcolexp = ExtractColumnExpressions(binaryJoin!);
                        }
                        else if (expression.JoinPredicate is SqlUnaryExpression unaryJoin)
                        {
                            tempcolexp = ExtractColumnExpressions(unaryJoin!);
                        }
                        else
                        {
                            tempcolexp = [];
                        }

                        bool refrencesfirsttable = false;
                        foreach (ColumnExpression col in tempcolexp)
                        {
                            if (col.TableAlias == Tables[0].Alias)
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

                        colexp.AddRange(tempcolexp);
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
        }

        private List<ColumnExpression> ExtractColumnExpressions(SqlBinaryExpression binaryexp)
        {
            List<ColumnExpression> result = [];
            switch (binaryexp.Left)
            {
                case SqlBinaryExpression left:
                    result.AddRange(ExtractColumnExpressions(left));
                    break;
                case ColumnExpression colLeft:
                    result.Add(colLeft);
                    break;
            }

            switch (binaryexp.Right)
            {
                case SqlBinaryExpression right:
                    result.AddRange(ExtractColumnExpressions(right));
                    break;
                case ColumnExpression colRight:
                    result.Add(colRight);
                    break;
            }

            return result;
        }
        private List<ColumnExpression> ExtractColumnExpressions(SqlUnaryExpression unaryexp)
        {
            List<ColumnExpression> result = [];
            switch (unaryexp.Operand)
            {
                case SqlBinaryExpression left:
                    result.AddRange(ExtractColumnExpressions(left));
                    break;
                case ColumnExpression colLeft:
                    result.Add(colLeft);
                    break;
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
                    Sql.Append("IIF(");
                    base.VisitColumn(columnExpression);
                    Sql.Append(" IS NULL, NULL, ");
                    Sql.Append(function);
                    Sql.Append("(");
                    base.VisitColumn(columnExpression);
                    Sql.Append(")");
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
                && selectExpression is { IsDistinct: false, Predicate: null, Having: null, Orderings.Count: 0, GroupBy.Count: 0, Tables: [SetOperationBase setOperation] }
                && selectExpression.Projection.Count == setOperation.Source1.Projection.Count
                && selectExpression.Projection.Select(
                        (pe, index) => pe.Expression is ColumnExpression column
                            && string.Equals(column.TableAlias, setOperation.Alias,
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
            if (sqlParameterExpression.Type == typeof(TimeOnly) && sqlParameterExpression.TypeMapping is JetTimeOnlyTypeMapping)
            {
                Sql.Append("TIMEVALUE(");
                base.VisitSqlParameter(sqlParameterExpression);
                Sql.Append(")");
                return sqlParameterExpression;
            }


            //GroupBy_param_Select_Sum_Min_Key_Max_Avg
            //Subquery has parameter as a projection with alias
            //Parent query references that alias in the GroupBy and in the outer projection
            //Query returns a variant object of value NULL instead of the expected type (in this case integer)
            //Nullable object must have a value
            //Specifically converting the parameter to its type fixes the problem
            //This does do it for all cases which changes the SQL
            //Don't have the required info in this function to detect the specific prerequisites for this problem
            //TODO: Optimize elsewhere if possible - Expression Visitor?
            if (parent.TryPeek(out var parentexp))
            {
                if (parentexp is ProjectionExpression { Alias: not null })
                {
                    if (_convertMappings.TryGetValue(sqlParameterExpression.Type.Name, out var conv))
                    {
                        Sql.Append($"{conv}(");
                        base.VisitSqlParameter(sqlParameterExpression);
                        Sql.Append(")");
                        return sqlParameterExpression;
                    }
                }
            }
            return base.VisitSqlParameter(sqlParameterExpression);
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            Check.NotNull(sqlBinaryExpression, nameof(sqlBinaryExpression));

            if (sqlBinaryExpression.OperatorType == ExpressionType.Coalesce)
            {
                SqlConstantExpression nullcons = new(null,typeof(string), RelationalTypeMapping.NullMapping);
                SqlUnaryExpression isnullexp = new(ExpressionType.Equal, sqlBinaryExpression.Left, typeof(bool), null);
                List<CaseWhenClause> whenclause = [new CaseWhenClause(isnullexp, sqlBinaryExpression.Right)];
                CaseExpression caseexp = new(whenclause, sqlBinaryExpression.Left);
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
            parent.Push(inExpression);
            base.GenerateIn(inExpression, negated);
            parent.Pop();
        }

        protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
        {
            if (sqlConstantExpression.TypeMapping == RelationalTypeMapping.NullMapping && sqlConstantExpression.Value is DateTime)
            {
                sqlConstantExpression = (SqlConstantExpression)sqlConstantExpression.ApplyTypeMapping(new JetDateTimeTypeMapping("datetime", _options));
            }

            parent.TryPeek(out var exp);
            if (sqlConstantExpression.Value is null && exp is ProjectionExpression && (sqlConstantExpression.Type.IsNumeric() || sqlConstantExpression.Type.IsEnum || sqlConstantExpression.Type == typeof(bool)))
            {
                Sql.Append("CVar(");
                Sql.Append(sqlConstantExpression.TypeMapping!.GenerateSqlLiteral(sqlConstantExpression.Value));
                Sql.Append(")");
                return sqlConstantExpression;
            }

            if (sqlConstantExpression.TypeMapping is BoolTypeMapping and not JetBoolTypeMapping)
            {
                Sql.Append((bool)sqlConstantExpression.Value! ? "TRUE" : "FALSE");
                return sqlConstantExpression;
            }
            return base.VisitSqlConstant(sqlConstantExpression);
        }

        protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
        {
            switch (sqlUnaryExpression.OperatorType)
            {
                case ExpressionType.Convert:
                    return VisitJetConvertExpression(sqlUnaryExpression);
                case ExpressionType.Not when sqlUnaryExpression.Type != typeof(bool):
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
                default:
                    return base.VisitSqlUnary(sqlUnaryExpression);
            }
        }


        private Expression VisitJetConvertExpression(SqlUnaryExpression convertExpression)
        {
            var typeMapping = convertExpression.TypeMapping ?? throw new InvalidOperationException(
                    RelationalStrings.UnsupportedType(convertExpression.Type.ShortDisplayName()));

            // We are explicitly converting to the target type (convertExpression.Type) and not the CLR type of the
            // accociated type mapping. This allows for conversions on the database side (e.g. CDBL()) but handling
            // of the returned value using a different (unaligned) type mapping (e.g. date/time related ones).
            if (_convertMappings.TryGetValue(convertExpression.Type.Name, out var function))
            {
                SqlExpression checksqlexp = convertExpression.Operand;

                SqlExpression? notnullsqlexp = null;
                if (convertExpression.TypeMapping is ByteArrayTypeMapping)
                {
                    notnullsqlexp = checksqlexp;
                }
                else
                {
                    notnullsqlexp = new SqlFunctionExpression(function, [convertExpression.Operand],
                        false, [false], typeMapping.ClrType, null);
                }

                SqlConstantExpression nullcons = new(null,typeof(string), RelationalTypeMapping.NullMapping);
                SqlUnaryExpression isnullexp = new(ExpressionType.Equal, checksqlexp, typeof(bool), null);
                List<CaseWhenClause> whenclause =
                [
                    new CaseWhenClause(isnullexp, nullcons)
                ];
                CaseExpression caseexp = new(whenclause, notnullsqlexp);
                switch (checksqlexp)
                {
                    case ColumnExpression { IsNullable: true }:
                        Visit(caseexp);
                        break;
                    case ColumnExpression columnExpression:
                        Visit(notnullsqlexp);
                        break;
                    case SqlFunctionExpression { IsNullable: true, ArgumentsPropagateNullability: not null } functionExpression when functionExpression.ArgumentsPropagateNullability.Any(d => d):
                        Visit(caseexp);
                        break;
                    case SqlFunctionExpression functionExpression:
                        Visit(notnullsqlexp);
                        break;
                    case SqlBinaryExpression binaryExpression:
                    {
                        ColumnExpression? columnExpressionLeft = binaryExpression.Left as ColumnExpression;
                        SqlFunctionExpression? functionExpressionLeft = binaryExpression.Left as SqlFunctionExpression;
                        ColumnExpression? columnExpressionRight = binaryExpression.Right as ColumnExpression;
                        SqlFunctionExpression? functionExpressionRight = binaryExpression.Right as SqlFunctionExpression;
                        var leftnull = columnExpressionLeft is { IsNullable: true } ||
                            functionExpressionLeft is { IsNullable: true };
                        var rightnull = columnExpressionRight is { IsNullable: true } ||
                            functionExpressionRight is { IsNullable: true };

                        if (leftnull || rightnull)
                        {
                            Visit(caseexp);
                        }
                        else
                        {
                            Visit(notnullsqlexp);
                        }

                        break;
                    }
                    case SqlUnaryExpression unaryExpression:
                    case SqlConstantExpression { Value: not null }:
                        Visit(notnullsqlexp);
                        break;
                    default:
                        Visit(caseexp);
                        break;
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

        protected override string GetOperator(SqlBinaryExpression binaryExpression)
            => binaryExpression.OperatorType switch
            {
                ExpressionType.Add when binaryExpression.Type == typeof(string) => " & ",
                ExpressionType.And => " BAND ",
                ExpressionType.Modulo => " MOD ",
                ExpressionType.Or => " BOR ",
                ExpressionType.Not => " BNOT ",
                ExpressionType.ExclusiveOr => " BXOR ",
                ExpressionType.Divide when binaryExpression.Type == typeof(int) => " \\ ",
                _ => base.GetOperator(binaryExpression),
            };

        protected override Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
        {
            Visit(crossJoinExpression.Table);
            return crossJoinExpression;
        }

        private Expression VisitRowValuePrivate(RowValueExpression rowValueExpression, IReadOnlyList<string> columnNames)
        {
            var values = rowValueExpression.Values;
            var count = values.Count;
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }

                Visit(values[i]);
                Sql.Append(" AS ");
                Sql.Append(_sqlGenerationHelper.DelimitIdentifier(columnNames[i]));
            }

            return rowValueExpression;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override Expression VisitValues(ValuesExpression valuesExpression)
        {
            base.VisitValues(valuesExpression);

            return valuesExpression;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override void GenerateValues(ValuesExpression valuesExpression)
        {
            if (valuesExpression.RowValues is null || valuesExpression.RowValues.Count == 0)
            {
                throw new InvalidOperationException(RelationalStrings.EmptyCollectionNotSupportedAsInlineQueryRoot);
            }

            var rowValues = valuesExpression.RowValues;

            for (var i = 0; i < rowValues.Count; i++)
            {
                Sql.Append("SELECT ");

                VisitRowValuePrivate(valuesExpression.RowValues[i], valuesExpression.ColumnNames);
                GeneratePseudoFromClause();
                var alias = valuesExpression.Alias;
                Sql.Append(" AS ");
                Sql.Append(_sqlGenerationHelper.DelimitIdentifier(alias + "_" + i));
                if (i != rowValues.Count - 1)
                {
                    Sql.AppendLine();
                    Sql.AppendLine("UNION");
                }
            }
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

            if (sqlFunctionExpression.Name.Equals("COALESCE", StringComparison.OrdinalIgnoreCase) && sqlFunctionExpression.Arguments is
                {
                    Count: > 1
                })
            {
                int start = sqlFunctionExpression.Arguments.Count - 1;
                CaseExpression? lastcaseexp = null;
                for (int A = start; A >= 1; A--)
                {
                    SqlUnaryExpression isnullexp = new(ExpressionType.Equal, sqlFunctionExpression.Arguments[A - 1], typeof(bool), null);
                    List<CaseWhenClause> whenclause =
                        [new CaseWhenClause(isnullexp, lastcaseexp ?? sqlFunctionExpression.Arguments[A])];
                    lastcaseexp = new CaseExpression(whenclause, sqlFunctionExpression.Arguments[A - 1]);
                }

                Visit(lastcaseexp);
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name.Equals("MID", StringComparison.OrdinalIgnoreCase) &&
                sqlFunctionExpression.Arguments is { Count: > 2 })
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
                if (sqlFunctionExpression.Arguments[2] is SqlUnaryExpression { OperatorType: ExpressionType.Convert, Operand: ColumnExpression { IsNullable: true } or SqlFunctionExpression { IsNullable: true } } unaryExpression)
                {
                    Sql.Append("IIF(");
                    Visit(unaryExpression.Operand);
                    Sql.Append(" IS NULL, NULL, ");
                    base.VisitSqlFunction(sqlFunctionExpression);
                    Sql.Append(")");
                    return sqlFunctionExpression;
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

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override Expression VisitDelete(DeleteExpression deleteExpression)
        {
            var selectExpression = deleteExpression.SelectExpression;

            if (selectExpression.Offset == null
                && selectExpression.Having == null
                && selectExpression.Orderings.Count == 0
                && selectExpression.GroupBy.Count == 0
                && selectExpression.Projection.Count == 0)
            {
                Sql.Append("DELETE ");

                if (selectExpression.Tables.Count > 1)
                {
                    Sql.Append($"{Dependencies.SqlGenerationHelper.DelimitIdentifier(deleteExpression.Table.Alias)}.*");
                    Sql.AppendLine();
                }

                Sql.Append("FROM ");
                VisitJetTables(selectExpression.Tables, false, out _);

                if (selectExpression.Predicate != null)
                {
                    Sql.AppendLine().Append("WHERE ");

                    Visit(selectExpression.Predicate);
                }

                GenerateLimitOffset(selectExpression);

                return deleteExpression;
            }

            throw new InvalidOperationException(
                RelationalStrings.ExecuteOperationWithUnsupportedOperatorInSqlGeneration(nameof(EntityFrameworkQueryableExtensions.ExecuteDelete)));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override Expression VisitUpdate(UpdateExpression updateExpression)
        {
            var selectExpression = updateExpression.SelectExpression;

            if (selectExpression.Offset == null
                && selectExpression.Having == null
                && selectExpression.Orderings.Count == 0
                && selectExpression.GroupBy.Count == 0
                && selectExpression.Projection.Count == 0
                && selectExpression.Limit == null)
            {
                Sql.Append("UPDATE ");

                VisitJetTables(selectExpression.Tables, false, out _);

                Sql.AppendLine().Append("SET ");
                Visit(updateExpression.ColumnValueSetters[0].Column);
                Sql.Append(" = ");
                Visit(updateExpression.ColumnValueSetters[0].Value);

                using (Sql.Indent())
                {
                    foreach (var columnValueSetter in updateExpression.ColumnValueSetters.Skip(1))
                    {
                        Sql.AppendLine(",");
                        Visit(columnValueSetter.Column);
                        Sql.Append(" = ");
                        Visit(columnValueSetter.Value);
                    }
                }

                if (selectExpression.Predicate != null)
                {
                    Sql.AppendLine().Append("WHERE ");
                    Visit(selectExpression.Predicate);
                }

                return updateExpression;
            }

            throw new InvalidOperationException(
                RelationalStrings.ExecuteOperationWithUnsupportedOperatorInSqlGeneration(nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)));
        }

        /// <inheritdoc />
        protected override void CheckComposableSqlTrimmed(ReadOnlySpan<char> sql)
        {
            base.CheckComposableSqlTrimmed(sql);

            if (sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(RelationalStrings.FromSqlNonComposable);
            }
        }
    }
}