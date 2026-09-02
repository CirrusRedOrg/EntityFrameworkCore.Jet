using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public class JetLocateScalarSubqueryVisitor : ExpressionVisitor
{
    // EF 11 removed SqlExpressionVisitor, which dispatched each SqlExpression node type to its own VisitXxx.
    // Every one of those nodes is an Expression with NodeType Extension, so the dispatch is reproduced here and
    // the per-node methods below are unchanged. Anything unrecognised falls through to the base, which visits
    // the node's children — the same default the removed base class applied.
    protected override Expression VisitExtension(Expression extensionExpression)
        => extensionExpression switch
        {
            AtTimeZoneExpression e => VisitAtTimeZone(e),
            CaseExpression e => VisitCase(e),
            CollateExpression e => VisitCollate(e),
            ColumnExpression e => VisitColumn(e),
            CrossApplyExpression e => VisitCrossApply(e),
            CrossJoinExpression e => VisitCrossJoin(e),
            DeleteExpression e => VisitDelete(e),
            DistinctExpression e => VisitDistinct(e),
            ExceptExpression e => VisitExcept(e),
            ExistsExpression e => VisitExists(e),
            FromSqlExpression e => VisitFromSql(e),
            InExpression e => VisitIn(e),
            InnerJoinExpression e => VisitInnerJoin(e),
            IntersectExpression e => VisitIntersect(e),
            JsonScalarExpression e => VisitJsonScalar(e),
            LeftJoinExpression e => VisitLeftJoin(e),
            LikeExpression e => VisitLike(e),
            OrderingExpression e => VisitOrdering(e),
            OuterApplyExpression e => VisitOuterApply(e),
            ProjectionExpression e => VisitProjection(e),
            RightJoinExpression e => VisitRightJoin(e),
            RowNumberExpression e => VisitRowNumber(e),
            RowValueExpression e => VisitRowValue(e),
            ScalarSubqueryExpression e => VisitScalarSubquery(e),
            SelectExpression e => VisitSelect(e),
            SqlBinaryExpression e => VisitSqlBinary(e),
            SqlConstantExpression e => VisitSqlConstant(e),
            SqlFragmentExpression e => VisitSqlFragment(e),
            SqlFunctionExpression e => VisitSqlFunction(e),
            SqlParameterExpression e => VisitSqlParameter(e),
            SqlUnaryExpression e => VisitSqlUnary(e),
            TableExpression e => VisitTable(e),
            TableValuedFunctionExpression e => VisitTableValuedFunction(e),
            UnionExpression e => VisitUnion(e),
            UpdateExpression e => VisitUpdate(e),
            ValuesExpression e => VisitValues(e),
            _ => base.VisitExtension(extensionExpression),
        };

    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetLocateScalarSubqueryVisitor(
        IRelationalTypeMappingSource typeMappingSource,
        ISqlExpressionFactory sqlExpressionFactory)
    {
        (_typeMappingSource, _sqlExpressionFactory) = (typeMappingSource, sqlExpressionFactory);
    }

    protected virtual Expression VisitAtTimeZone(AtTimeZoneExpression atTimeZoneExpression)
    {
        var operand = (SqlExpression)Visit(atTimeZoneExpression.Operand);
        var timeZone = (SqlExpression)Visit(atTimeZoneExpression.TimeZone);
        return atTimeZoneExpression.Update(operand, timeZone);
    }

    protected virtual Expression VisitCase(CaseExpression caseExpression)
    {
        var operand = (SqlExpression?)Visit(caseExpression.Operand);
        var whenClauses = new List<CaseWhenClause>();
        foreach (var whenClause in caseExpression.WhenClauses)
        {
            var test = (SqlExpression)Visit(whenClause.Test);
            var result = (SqlExpression)Visit(whenClause.Result);
            whenClauses.Add(new CaseWhenClause(test, result));
        }
        var elseResult = (SqlExpression?)Visit(caseExpression.ElseResult);

        return caseExpression.Update(operand, whenClauses, elseResult);
    }

    protected virtual Expression VisitCollate(CollateExpression collateExpression)
    {
        var operand = (SqlExpression)Visit(collateExpression.Operand);

        return collateExpression.Update(operand);
    }

    protected virtual Expression VisitColumn(ColumnExpression columnExpression)
    {
        return columnExpression;
    }

    protected virtual Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
    {
        var table = (TableExpressionBase)Visit(crossApplyExpression.Table);
        return crossApplyExpression.Update(table);
    }

    protected virtual Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
    {
        var table = (TableExpressionBase)Visit(crossJoinExpression.Table);
        return crossJoinExpression.Update(table);
    }

    protected virtual Expression VisitDelete(DeleteExpression deleteExpression)
    {
        return deleteExpression.Update(deleteExpression.Table,(SelectExpression)Visit(deleteExpression.SelectExpression));
    }

    protected virtual Expression VisitDistinct(DistinctExpression distinctExpression)
    {
        var operand = (SqlExpression)Visit(distinctExpression.Operand);
        return distinctExpression.Update(operand);
    }

    protected virtual Expression VisitExcept(ExceptExpression exceptExpression)
    {
        var source1 = (SelectExpression)Visit(exceptExpression.Source1);
        var source2 = (SelectExpression)Visit(exceptExpression.Source2);
        return exceptExpression.Update(source1, source2);
    }

    protected virtual Expression VisitExists(ExistsExpression existsExpression)
    {
        var subquery = (SelectExpression)Visit(existsExpression.Subquery);

        return existsExpression.Update(subquery);
    }

    protected virtual Expression VisitFromSql(FromSqlExpression fromSqlExpression)
    {
        return fromSqlExpression;
    }

    protected virtual Expression VisitIn(InExpression inExpression)
    {
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
        return inExpression.Update(item, subquery, newValues ?? values, valuesParameter);
    }

    protected virtual Expression VisitIntersect(IntersectExpression intersectExpression)
    {
        var source1 = (SelectExpression)Visit(intersectExpression.Source1);
        var source2 = (SelectExpression)Visit(intersectExpression.Source2);
        return intersectExpression.Update(source1, source2);
    }

    protected virtual Expression VisitLike(LikeExpression likeExpression)
    {
        var match = (SqlExpression)Visit(likeExpression.Match);
        var pattern = (SqlExpression)Visit(likeExpression.Pattern);
        var escapeChar = (SqlExpression?)Visit(likeExpression.EscapeChar);

        return likeExpression.Update(match, pattern, escapeChar);
    }

    protected virtual Expression VisitInnerJoin(InnerJoinExpression innerJoinExpression)
    {
        var table = (TableExpressionBase)Visit(innerJoinExpression.Table);
        var joinPredicate = (SqlExpression)Visit(innerJoinExpression.JoinPredicate);
        return innerJoinExpression.Update(table, joinPredicate);
    }

    protected virtual Expression VisitLeftJoin(LeftJoinExpression leftJoinExpression)
    {
        var table = (TableExpressionBase)Visit(leftJoinExpression.Table);
        var joinPredicate = (SqlExpression)Visit(leftJoinExpression.JoinPredicate);
        return leftJoinExpression.Update(table, joinPredicate);
    }

    protected virtual Expression VisitOrdering(OrderingExpression orderingExpression)
    {
        var expression = (SqlExpression)Visit(orderingExpression.Expression);
        return orderingExpression.Update(expression);
    }

    protected virtual Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
    {
        var table = (TableExpressionBase)Visit(outerApplyExpression.Table);
        return outerApplyExpression.Update(table);
    }

    protected virtual Expression VisitProjection(ProjectionExpression projectionExpression)
    {
        var expression = (SqlExpression)Visit(projectionExpression.Expression);

        return projectionExpression.Update(expression);
    }

    protected virtual Expression VisitRightJoin(RightJoinExpression rightJoinExpression)
    {
        throw new NotImplementedException();
    }

    protected virtual Expression VisitTableValuedFunction(TableValuedFunctionExpression tableValuedFunctionExpression)
    {
        var arguments = new SqlExpression[tableValuedFunctionExpression.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = (SqlExpression)Visit(tableValuedFunctionExpression.Arguments[i]);
        }

        return tableValuedFunctionExpression.Update(arguments);
    }

    protected virtual Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
    {
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
        return rowNumberExpression.Update(partitions, orderings);
    }

    protected virtual Expression VisitRowValue(RowValueExpression rowValueExpression)
    {
        var values = new SqlExpression[rowValueExpression.Values.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (SqlExpression)Visit(rowValueExpression.Values[i]);
        }
        return rowValueExpression.Update(values);
    }

    protected virtual Expression VisitScalarSubquery(ScalarSubqueryExpression scalarSubqueryExpression)
    {
        return scalarSubqueryExpression;
    }

    protected virtual Expression VisitSelect(SelectExpression selectExpression)
    {
        var changed = false;
        var projections = new List<ProjectionExpression>();
        foreach (var item in selectExpression.Projection)
        {
            var updatedProjection = (ProjectionExpression)Visit(item);
            projections.Add(updatedProjection);
            changed |= updatedProjection != item;
        }

        var tables = new List<TableExpressionBase>();
        foreach (var table in selectExpression.Tables)
        {
            var newTable = (TableExpressionBase)Visit(table);
            changed |= newTable != table;
            tables.Add(newTable);
        }

        var predicate = (SqlExpression?)Visit(selectExpression.Predicate);
        changed |= predicate != selectExpression.Predicate;

        var groupBy = new List<SqlExpression>();
        foreach (var groupingKey in selectExpression.GroupBy)
        {
            var newGroupingKey = (SqlExpression)Visit(groupingKey);
            changed |= newGroupingKey != groupingKey;
            groupBy.Add(newGroupingKey);
        }

        var havingExpression = (SqlExpression?)Visit(selectExpression.Having);
        changed |= havingExpression != selectExpression.Having;

        var orderings = new List<OrderingExpression>();
        foreach (var ordering in selectExpression.Orderings)
        {
            var orderingExpression = (SqlExpression)Visit(ordering.Expression);
            changed |= orderingExpression != ordering.Expression;
            orderings.Add(ordering.Update(orderingExpression));
        }

        var offset = (SqlExpression?)Visit(selectExpression.Offset);
        changed |= offset != selectExpression.Offset;

        var limit = (SqlExpression?)Visit(selectExpression.Limit);
        changed |= limit != selectExpression.Limit;

        return changed
            ? selectExpression.Update(
                tables, predicate, groupBy, havingExpression, projections, orderings, offset, limit)
            : selectExpression;
    }

    protected virtual Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
    {
        var newLeft = (SqlExpression)Visit(sqlBinaryExpression.Left);
        var newRight = (SqlExpression)Visit(sqlBinaryExpression.Right);
        if (newLeft is ScalarSubqueryExpression)
        {
            return newLeft;
        }

        if (newRight is ScalarSubqueryExpression)
        {
            return newRight;
        }
        sqlBinaryExpression = sqlBinaryExpression.Update(newLeft, newRight);
        return sqlBinaryExpression;
    }

    protected virtual Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
    {
        return sqlConstantExpression;
    }

    protected virtual Expression VisitSqlFragment(SqlFragmentExpression sqlFragmentExpression)
    {
        return sqlFragmentExpression;
    }

    protected virtual Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
    {
        var instance = (SqlExpression?)Visit(sqlFunctionExpression.Instance);
        SqlExpression[]? arguments = default;
        if (!sqlFunctionExpression.IsNiladic)
        {
            arguments = new SqlExpression[sqlFunctionExpression.Arguments.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = (SqlExpression)Visit(sqlFunctionExpression.Arguments[i]);
                if (arguments[i] is ScalarSubqueryExpression)
                {
                    return arguments[i];
                }
            }
        }
        var newFunction = sqlFunctionExpression.Update(instance, arguments);

        return newFunction;
    }

    protected virtual Expression VisitSqlParameter(SqlParameterExpression sqlParameterExpression)
    {
        return sqlParameterExpression;
    }

    protected virtual Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
    {
        var operand = (SqlExpression)Visit(sqlUnaryExpression.Operand);
        if (operand is ScalarSubqueryExpression)
        {
            return operand;
        }
        return sqlUnaryExpression.Update(operand);
    }

    protected virtual Expression VisitTable(TableExpression tableExpression)
    {
        return tableExpression;
    }

    protected virtual Expression VisitUnion(UnionExpression unionExpression)
    {
        var source1 = (SelectExpression)Visit(unionExpression.Source1);
        var source2 = (SelectExpression)Visit(unionExpression.Source2);
        return unionExpression.Update(source1, source2);
    }

    protected virtual Expression VisitUpdate(UpdateExpression updateExpression)
    {
        var selectExpression = (SelectExpression)Visit(updateExpression.SelectExpression);
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

        return updateExpression.Update(selectExpression, columnValueSetters ?? updateExpression.ColumnValueSetters);
    }

    protected virtual Expression VisitJsonScalar(JsonScalarExpression jsonScalarExpression)
    {
        return jsonScalarExpression;
    }

    protected virtual Expression VisitValues(ValuesExpression valuesExpression)
    {
        switch (valuesExpression)
        {
            case { RowValues: not null }:
                var rowValues = new RowValueExpression[valuesExpression.RowValues!.Count];
                for (var i = 0; i < rowValues.Length; i++)
                {
                    rowValues[i] = (RowValueExpression)Visit(valuesExpression.RowValues[i]);
                }
                return valuesExpression.Update(rowValues);

            case { ValuesParameter: not null }:
                var valuesParameter = (SqlParameterExpression)Visit(valuesExpression.ValuesParameter);
                return valuesExpression.Update(valuesParameter);

            default:
                throw new UnreachableException();
        }
    }
}
