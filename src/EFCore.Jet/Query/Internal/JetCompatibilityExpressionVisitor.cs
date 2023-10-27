// Copyright (c) Pomelo Foundation. All rights reserved.
// Licensed under the MIT. See LICENSE in the project root for license information.

using System;
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
            
            ShapedQueryExpression shapedQueryExpression => shapedQueryExpression.Update(Visit(shapedQueryExpression.QueryExpression), Visit(shapedQueryExpression.ShaperExpression)),
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

    protected virtual Expression TranslationFailed(Expression expression)
        => throw new InvalidOperationException(CoreStrings.TranslationFailed(expression.Print()));
}