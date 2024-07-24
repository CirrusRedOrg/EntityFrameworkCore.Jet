// Copyright (c) Pomelo Foundation. All rights reserved.
// Licensed under the MIT. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;

public class JetCompatibilityExpressionVisitor : ExpressionVisitor
{
    protected override Expression VisitExtension(Expression extensionExpression)
        => extensionExpression switch
        {
            RowNumberExpression rowNumberExpression => VisitRowNumber(rowNumberExpression),
            CrossApplyExpression crossApplyExpression => VisitCrossApply(crossApplyExpression),
            OuterApplyExpression outerApplyExpression => VisitOuterApply(outerApplyExpression),
            ExceptExpression exceptExpression => VisitExcept(exceptExpression),
            IntersectExpression intersectExpression => VisitIntersect(intersectExpression),
            JsonScalarExpression jsonScalarExpression => VisitJsonScalar(jsonScalarExpression),
            InnerJoinExpression innerJoinExpression => VisitInnerJoin(innerJoinExpression),
            LeftJoinExpression leftJoinExpression => VisitLeftJoin(leftJoinExpression),
            SelectExpression selectExpression => VisitSelect(selectExpression),
            ShapedQueryExpression shapedQueryExpression => shapedQueryExpression.Update(Visit(shapedQueryExpression.QueryExpression), Visit(shapedQueryExpression.ShaperExpression)),
            CrossJoinExpression crossJoinExpression => VisitCrossJoin(crossJoinExpression),
            _ => base.VisitExtension(extensionExpression)
        };

    protected virtual Expression VisitRowNumber(RowNumberExpression rowNumberExpression)
        => TranslationFailed(rowNumberExpression);

    protected virtual Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
        => TranslationFailed(crossApplyExpression);

    protected virtual Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
        => TranslationFailed(outerApplyExpression);

    protected virtual Expression VisitExcept(ExceptExpression exceptExpression)
        => TranslationFailed(exceptExpression);

    protected virtual Expression VisitIntersect(IntersectExpression intersectExpression)
        => TranslationFailed(intersectExpression);

    protected virtual Expression VisitJsonScalar(JsonScalarExpression jsonScalarExpression)
        => jsonScalarExpression.Path.Count > 0
            ? TranslationFailed(jsonScalarExpression)
            : jsonScalarExpression;

    protected virtual Expression VisitLeftJoin(LeftJoinExpression leftJoinExpression)
    {
        if (leftJoinExpression.JoinPredicate is SqlBinaryExpression sqlBinaryExpression)
        {
            if (ContainsUnsupportCol(sqlBinaryExpression))
            {
                return TranslationFailed(leftJoinExpression.JoinPredicate);
            }
        }

        if (leftJoinExpression.JoinPredicate is SqlUnaryExpression)
        {
            return TranslationFailed(leftJoinExpression.JoinPredicate);
        }
        return base.VisitExtension(leftJoinExpression);
    }

    protected virtual Expression VisitInnerJoin(InnerJoinExpression innerJoinExpression)
    {
        if (innerJoinExpression.JoinPredicate is SqlBinaryExpression sqlBinaryExpression)
        {
            if (ContainsUnsupportCol(sqlBinaryExpression))
            {
                return TranslationFailed(innerJoinExpression.JoinPredicate);
            }
        }

        if (innerJoinExpression.JoinPredicate is SqlUnaryExpression)
        {
            return TranslationFailed(innerJoinExpression.JoinPredicate);
        }
        return base.VisitExtension(innerJoinExpression);
    }

    protected virtual Expression VisitSelect(SelectExpression selectExpression)
    {

        return base.VisitExtension(selectExpression);
    }

    protected virtual Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
    {
        if (crossJoinExpression.Table is SelectExpression selectExpression)
        {
            return TranslationFailed(selectExpression);
        }
        return base.VisitExtension(crossJoinExpression);
    }

    protected virtual Expression TranslationFailed(Expression expression)
        => throw new InvalidOperationException("Unsupported Jet expression: " + expression.Print());

    private bool ContainsUnsupportCol(SqlBinaryExpression binaryexp)
    {
        bool containsunsupported = false;
        if (binaryexp.Left is SqlBinaryExpression left)
        {
            containsunsupported = ContainsUnsupportCol(left) ^ containsunsupported;
        }
        else if (binaryexp.Left is SqlConstantExpression or SqlFunctionExpression or ScalarSubqueryExpression)
        {
            containsunsupported = true;
        }

        if (binaryexp.Right is SqlBinaryExpression right)
        {
            containsunsupported = ContainsUnsupportCol(right) ^ containsunsupported;
        }
        else if (binaryexp.Right is SqlConstantExpression or SqlFunctionExpression or ScalarSubqueryExpression)
        {
            containsunsupported = true;
        }

        return containsunsupported;
    }
}