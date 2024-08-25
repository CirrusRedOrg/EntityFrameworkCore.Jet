// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
#nullable enable

namespace EntityFrameworkCore.Jet.Query.Internal;

public class SearchConditionConvertingExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
    : SqlExpressionVisitor
{
    private bool _isSearchCondition;

    private SqlExpression ApplyConversion(SqlExpression sqlExpression, bool condition)
        => _isSearchCondition
            ? ConvertToSearchCondition(sqlExpression, condition)
            : ConvertToValue(sqlExpression, condition);

    private SqlExpression ConvertToSearchCondition(SqlExpression sqlExpression, bool condition)
        => condition
            ? sqlExpression
            : BuildCompareToExpression(sqlExpression);

    private SqlExpression ConvertToValue(SqlExpression sqlExpression, bool condition)
    {
        return condition
            ? sqlExpressionFactory.Case(
                new[]
                {
                    new CaseWhenClause(
                        SimplifyNegatedBinary(sqlExpression),
                        sqlExpressionFactory.ApplyDefaultTypeMapping(sqlExpressionFactory.Constant(true)))
                },
                sqlExpressionFactory.Constant(false))
            : sqlExpression;
    }

    private SqlExpression BuildCompareToExpression(SqlExpression sqlExpression)
        => sqlExpression is SqlConstantExpression sqlConstantExpression
            && sqlConstantExpression.Value is bool boolValue
                ? sqlExpressionFactory.Equal(
                    boolValue
                        ? sqlExpressionFactory.Constant(1)
                        : sqlExpressionFactory.Constant(0),
                    sqlExpressionFactory.Constant(1))
                : sqlExpressionFactory.Equal(
                    sqlExpression,
                    sqlExpressionFactory.Constant(true));


    private SqlExpression SimplifyNegatedBinary(SqlExpression sqlExpression)
    {
        if (sqlExpression is SqlUnaryExpression { OperatorType: ExpressionType.Not } sqlUnaryExpression
            && sqlUnaryExpression.Type == typeof(bool)
            && sqlUnaryExpression.Operand is SqlBinaryExpression
            {
                OperatorType: ExpressionType.Equal
            } sqlBinaryOperand)
        {
            if (sqlBinaryOperand.Left.Type == typeof(bool)
                && sqlBinaryOperand.Right.Type == typeof(bool)
                && (sqlBinaryOperand.Left is SqlConstantExpression
                    || sqlBinaryOperand.Right is SqlConstantExpression))
            {
                var constant = sqlBinaryOperand.Left as SqlConstantExpression ?? (SqlConstantExpression)sqlBinaryOperand.Right;
                if (sqlBinaryOperand.Left is SqlConstantExpression)
                {
                    return sqlExpressionFactory.MakeBinary(
                        ExpressionType.Equal,
                        sqlExpressionFactory.Constant(!(bool)constant.Value!, constant.TypeMapping),
                        sqlBinaryOperand.Right,
                        sqlBinaryOperand.TypeMapping)!;
                }

                return sqlExpressionFactory.MakeBinary(
                    ExpressionType.Equal,
                    sqlBinaryOperand.Left,
                    sqlExpressionFactory.Constant(!(bool)constant.Value!, constant.TypeMapping),
                    sqlBinaryOperand.TypeMapping)!;
            }

            return sqlExpressionFactory.MakeBinary(
                sqlBinaryOperand.OperatorType == ExpressionType.Equal
                    ? ExpressionType.NotEqual
                    : ExpressionType.Equal,
                sqlBinaryOperand.Left,
                sqlBinaryOperand.Right,
                sqlBinaryOperand.TypeMapping)!;
        }

        return sqlExpression;
    }

    protected override Expression VisitCase(CaseExpression caseExpression)
    {
        var parentSearchCondition = _isSearchCondition;

        var testIsCondition = caseExpression.Operand == null;
        _isSearchCondition = false;
        var operand = (SqlExpression?)Visit(caseExpression.Operand);
        var whenClauses = new List<CaseWhenClause>();
        foreach (var whenClause in caseExpression.WhenClauses)
        {
            _isSearchCondition = testIsCondition;
            var test = (SqlExpression)Visit(whenClause.Test);
            _isSearchCondition = false;
            var result = (SqlExpression)Visit(whenClause.Result);
            whenClauses.Add(new CaseWhenClause(test, result));
        }

        _isSearchCondition = false;
        var elseResult = (SqlExpression?)Visit(caseExpression.ElseResult);

        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(sqlExpressionFactory.Case(operand, whenClauses, elseResult, caseExpression), condition: false);
    }

    protected override Expression VisitCollate(CollateExpression collateExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var operand = (SqlExpression)Visit(collateExpression.Operand);
        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(collateExpression.Update(operand), condition: false);
    }

    protected override Expression VisitColumn(ColumnExpression columnExpression)
    {
        return ApplyConversion(columnExpression, condition: false);
    }

    protected override Expression VisitDelete(DeleteExpression deleteExpression)
        => deleteExpression.Update(deleteExpression.Table, (SelectExpression)Visit(deleteExpression.SelectExpression));

    protected override Expression VisitDistinct(DistinctExpression distinctExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var operand = (SqlExpression)Visit(distinctExpression.Operand);
        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(distinctExpression.Update(operand), condition: false);
    }

    protected override Expression VisitExists(ExistsExpression existsExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var subquery = (SelectExpression)Visit(existsExpression.Subquery);
        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(existsExpression.Update(subquery), condition: true);
    }

    protected override Expression VisitFromSql(FromSqlExpression fromSqlExpression)
    {
        Check.NotNull(fromSqlExpression, nameof(fromSqlExpression));

        return fromSqlExpression;
    }

    protected override Expression VisitIn(InExpression inExpression)
    {
        var parentSearchCondition = _isSearchCondition;

        _isSearchCondition = false;
        var item = (SqlExpression)Visit(inExpression.Item);
        var subquery = (SelectExpression?)Visit(inExpression.Subquery);

        var values = inExpression.Values;
        SqlExpression[]? newValues = null;
        if (values is not null)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var newValue = (SqlExpression)Visit(value);

                if (newValue != value && newValues is null)
                {
                    newValues = new SqlExpression[values.Count];
                    for (var j = 0; j < i; j++)
                    {
                        newValues[j] = values[j];
                    }
                }

                if (newValues is not null)
                {
                    newValues[i] = newValue;
                }
            }
        }

        var valuesParameter = (SqlParameterExpression?)Visit(inExpression.ValuesParameter);
        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(inExpression.Update(item, subquery, newValues ?? values, valuesParameter), condition: true);
    }

    protected override Expression VisitLike(LikeExpression likeExpression)
    {
        Check.NotNull(likeExpression, nameof(likeExpression));

        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var match = (SqlExpression)Visit(likeExpression.Match);
        var pattern = (SqlExpression)Visit(likeExpression.Pattern);
        var escapeChar = (SqlExpression?)Visit(likeExpression.EscapeChar);
        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(likeExpression.Update(match, pattern, escapeChar), condition: true);
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var parentSearchCondition = _isSearchCondition;

        _isSearchCondition = false;

        var projections = this.VisitAndConvert(selectExpression.Projection);
        var tables = this.VisitAndConvert(selectExpression.Tables);
        var groupBy = this.VisitAndConvert(selectExpression.GroupBy);
        var orderings = this.VisitAndConvert(selectExpression.Orderings);
        var offset = (SqlExpression?)Visit(selectExpression.Offset);
        var limit = (SqlExpression?)Visit(selectExpression.Limit);

        _isSearchCondition = true;

        var predicate = (SqlExpression?)Visit(selectExpression.Predicate);
        var havingExpression = (SqlExpression?)Visit(selectExpression.Having);

        _isSearchCondition = parentSearchCondition;

        return selectExpression.Update(tables, predicate, groupBy, havingExpression, projections, orderings, offset, limit);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitAtTimeZone(AtTimeZoneExpression atTimeZoneExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var operand = (SqlExpression)Visit(atTimeZoneExpression.Operand);
        var timeZone = (SqlExpression)Visit(atTimeZoneExpression.TimeZone);
        _isSearchCondition = parentSearchCondition;

        return atTimeZoneExpression.Update(operand, timeZone);
    }

    protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
    {
        var parentIsSearchCondition = _isSearchCondition;

        switch (sqlBinaryExpression.OperatorType)
        {
            // Only logical operations need conditions on both sides
            case ExpressionType.AndAlso:
            case ExpressionType.OrElse:
                _isSearchCondition = true;
                break;
            default:
                _isSearchCondition = false;
                break;
        }

        var newLeft = (SqlExpression)Visit(sqlBinaryExpression.Left);
        var newRight = (SqlExpression)Visit(sqlBinaryExpression.Right);

        _isSearchCondition = parentIsSearchCondition;

        if (!parentIsSearchCondition
            && (newLeft.Type == typeof(bool) || newLeft.Type.IsEnum || newLeft.Type.IsInteger())
            && (newRight.Type == typeof(bool) || newRight.Type.IsEnum || newRight.Type.IsInteger())
            && sqlBinaryExpression.OperatorType is ExpressionType.NotEqual or ExpressionType.Equal)
        {
            // "lhs != rhs" is the same as "CAST(lhs ^ rhs AS BIT)", except that
            // the first is a boolean, the second is a BIT
            var result = sqlExpressionFactory.MakeBinary(
                ExpressionType.ExclusiveOr,
                newLeft,
                newRight,
                null)!;

            if (result.Type != typeof(bool))
            {
                result = sqlExpressionFactory.Convert(result, typeof(bool), sqlBinaryExpression.TypeMapping);
            }

            // "lhs == rhs" is the same as "NOT(lhs == rhs)" aka "lhs ^ rhs ^ 1"
            if (sqlBinaryExpression.OperatorType is ExpressionType.Equal)
            {
                result = sqlExpressionFactory.MakeBinary(
                    ExpressionType.ExclusiveOr,
                    result,
                    sqlExpressionFactory.Constant(true, result.TypeMapping),
                    result.TypeMapping
                )!;
            }

            return result;
        }

        sqlBinaryExpression = sqlBinaryExpression.Update(newLeft, newRight);
        var condition = sqlBinaryExpression.OperatorType is ExpressionType.AndAlso
            or ExpressionType.OrElse
            or ExpressionType.Equal
            or ExpressionType.NotEqual
            or ExpressionType.GreaterThan
            or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan
            or ExpressionType.LessThanOrEqual;

        return ApplyConversion(sqlBinaryExpression, condition);
    }

    protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        bool resultCondition;
        switch (sqlUnaryExpression.OperatorType)
        {
            case ExpressionType.Not
                when sqlUnaryExpression.Type == typeof(bool):
            {
                // when possible, avoid converting to/from predicate form
                if (!_isSearchCondition && sqlUnaryExpression.Operand is not (ExistsExpression or InExpression or LikeExpression))
                {
                    var negatedOperand = (SqlExpression)Visit(sqlUnaryExpression.Operand);
                    return sqlExpressionFactory.MakeBinary(
                        ExpressionType.ExclusiveOr,
                        negatedOperand,
                        sqlExpressionFactory.Constant(true, negatedOperand.TypeMapping),
                        negatedOperand.TypeMapping
                    )!;
                }

                _isSearchCondition = true;
                resultCondition = true;
                break;
            }

            case ExpressionType.Not:
                _isSearchCondition = false;
                resultCondition = false;
                break;

            case ExpressionType.Convert:
            case ExpressionType.Negate:
                _isSearchCondition = false;
                resultCondition = false;
                break;

            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
                _isSearchCondition = false;
                resultCondition = true;
                break;

            default:
                throw new InvalidOperationException(
                    RelationalStrings.UnsupportedOperatorForSqlExpression(
                        sqlUnaryExpression.OperatorType, typeof(SqlUnaryExpression)));
        }

        var operand = (SqlExpression)Visit(sqlUnaryExpression.Operand);

        _isSearchCondition = parentSearchCondition;

        return SimplifyNegatedBinary(
            ApplyConversion(
                sqlUnaryExpression.Update(operand),
                condition: resultCondition));
    }

    protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
        => ApplyConversion(sqlConstantExpression, condition: false);

    protected override Expression VisitSqlFragment(SqlFragmentExpression sqlFragmentExpression)
        => sqlFragmentExpression;

    protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var instance = (SqlExpression?)Visit(sqlFunctionExpression.Instance);
        SqlExpression[]? arguments = default;
        if (!sqlFunctionExpression.IsNiladic)
        {
            arguments = new SqlExpression[sqlFunctionExpression.Arguments.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = (SqlExpression)Visit(sqlFunctionExpression.Arguments[i]);
            }
        }

        _isSearchCondition = parentSearchCondition;
        var newFunction = sqlFunctionExpression.Update(instance, arguments);

        // var condition = string.Equals(sqlFunctionExpression.Name, "FREETEXT")
        //    || string.Equals(sqlFunctionExpression.Name, "CONTAINS");

        return ApplyConversion(newFunction, /* condition */ false);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitTableValuedFunction(TableValuedFunctionExpression tableValuedFunctionExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;

        var arguments = new SqlExpression[tableValuedFunctionExpression.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = (SqlExpression)Visit(tableValuedFunctionExpression.Arguments[i]);
        }

        _isSearchCondition = parentSearchCondition;
        return tableValuedFunctionExpression.Update(arguments);
    }

    protected override Expression VisitSqlParameter(SqlParameterExpression sqlParameterExpression)
        => ApplyConversion(sqlParameterExpression, condition: false);

    protected override Expression VisitTable(TableExpression tableExpression)
        => tableExpression;

    protected override Expression VisitProjection(ProjectionExpression projectionExpression)
    {
        var expression = (SqlExpression)Visit(projectionExpression.Expression);

        return projectionExpression.Update(expression);
    }

    protected override Expression VisitOrdering(OrderingExpression orderingExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;

        var expression = (SqlExpression)Visit(orderingExpression.Expression);

        _isSearchCondition = parentSearchCondition;

        return orderingExpression.Update(expression);
    }

    protected override Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var table = (TableExpressionBase)Visit(crossJoinExpression.Table);
        _isSearchCondition = parentSearchCondition;

        return crossJoinExpression.Update(table);
    }

    protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var table = (TableExpressionBase)Visit(crossApplyExpression.Table);
        _isSearchCondition = parentSearchCondition;

        return crossApplyExpression.Update(table);
    }

    protected override Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var table = (TableExpressionBase)Visit(outerApplyExpression.Table);
        _isSearchCondition = parentSearchCondition;

        return outerApplyExpression.Update(table);
    }

    protected override Expression VisitInnerJoin(InnerJoinExpression innerJoinExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var table = (TableExpressionBase)Visit(innerJoinExpression.Table);
        _isSearchCondition = true;
        var joinPredicate = (SqlExpression)Visit(innerJoinExpression.JoinPredicate);
        _isSearchCondition = parentSearchCondition;

        return innerJoinExpression.Update(table, joinPredicate);
    }

    protected override Expression VisitLeftJoin(LeftJoinExpression leftJoinExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var table = (TableExpressionBase)Visit(leftJoinExpression.Table);
        _isSearchCondition = true;
        var joinPredicate = (SqlExpression)Visit(leftJoinExpression.JoinPredicate);
        _isSearchCondition = parentSearchCondition;

        return leftJoinExpression.Update(table, joinPredicate);
    }

    protected override Expression VisitScalarSubquery(ScalarSubqueryExpression scalarSubqueryExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        var subquery = (SelectExpression)Visit(scalarSubqueryExpression.Subquery);
        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(scalarSubqueryExpression.Update(subquery), condition: false);
    }

    protected override Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var partitions = new List<SqlExpression>();
        foreach (var partition in rowNumberExpression.Partitions)
        {
            var newPartition = (SqlExpression)Visit(partition);
            partitions.Add(newPartition);
        }

        var orderings = new List<OrderingExpression>();
        foreach (var ordering in rowNumberExpression.Orderings)
        {
            var newOrdering = (OrderingExpression)Visit(ordering);
            orderings.Add(newOrdering);
        }

        _isSearchCondition = parentSearchCondition;

        return ApplyConversion(rowNumberExpression.Update(partitions, orderings), condition: false);
    }

    protected override Expression VisitRowValue(RowValueExpression rowValueExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;

        var values = new SqlExpression[rowValueExpression.Values.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (SqlExpression)Visit(rowValueExpression.Values[i]);
        }

        _isSearchCondition = parentSearchCondition;
        return rowValueExpression.Update(values);
    }

    protected override Expression VisitExcept(ExceptExpression exceptExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var source1 = (SelectExpression)Visit(exceptExpression.Source1);
        var source2 = (SelectExpression)Visit(exceptExpression.Source2);
        _isSearchCondition = parentSearchCondition;

        return exceptExpression.Update(source1, source2);
    }

    protected override Expression VisitIntersect(IntersectExpression intersectExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var source1 = (SelectExpression)Visit(intersectExpression.Source1);
        var source2 = (SelectExpression)Visit(intersectExpression.Source2);
        _isSearchCondition = parentSearchCondition;

        return intersectExpression.Update(source1, source2);
    }

    protected override Expression VisitUnion(UnionExpression unionExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        var source1 = (SelectExpression)Visit(unionExpression.Source1);
        var source2 = (SelectExpression)Visit(unionExpression.Source2);
        _isSearchCondition = parentSearchCondition;

        return unionExpression.Update(source1, source2);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitUpdate(UpdateExpression updateExpression)
    {
        var selectExpression = (SelectExpression)Visit(updateExpression.SelectExpression);
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;
        List<ColumnValueSetter>? columnValueSetters = null;
        for (var (i, n) = (0, updateExpression.ColumnValueSetters.Count); i < n; i++)
        {
            var columnValueSetter = updateExpression.ColumnValueSetters[i];
            var newValue = (SqlExpression)Visit(columnValueSetter.Value);
            if (columnValueSetters != null)
            {
                columnValueSetters.Add(new ColumnValueSetter(columnValueSetter.Column, newValue));
            }
            else if (!ReferenceEquals(newValue, columnValueSetter.Value))
            {
                columnValueSetters = [];
                for (var j = 0; j < i; j++)
                {
                    columnValueSetters.Add(updateExpression.ColumnValueSetters[j]);
                }

                columnValueSetters.Add(new ColumnValueSetter(columnValueSetter.Column, newValue));
            }
        }

        _isSearchCondition = parentSearchCondition;
        return updateExpression.Update(selectExpression, columnValueSetters ?? updateExpression.ColumnValueSetters);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitJsonScalar(JsonScalarExpression jsonScalarExpression)
        => jsonScalarExpression;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitValues(ValuesExpression valuesExpression)
    {
        var parentSearchCondition = _isSearchCondition;
        _isSearchCondition = false;

        var rowValues = new RowValueExpression[valuesExpression.RowValues.Count];
        for (var i = 0; i < rowValues.Length; i++)
        {
            rowValues[i] = (RowValueExpression)Visit(valuesExpression.RowValues[i]);
        }

        _isSearchCondition = parentSearchCondition;
        return valuesExpression.Update(rowValues);
    }
}