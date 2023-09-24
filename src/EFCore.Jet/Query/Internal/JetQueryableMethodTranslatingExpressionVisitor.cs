// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class JetQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
        QueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected JetQueryableMethodTranslatingExpressionVisitor(
        JetQueryableMethodTranslatingExpressionVisitor parentVisitor)
        : base(parentVisitor)
    {
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor()
        => new JetQueryableMethodTranslatingExpressionVisitor(this);

    protected override ShapedQueryExpression? TranslateLeftJoin(ShapedQueryExpression outer, ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        //Jet can't handle a left join following a cross join
        //so we push the cross join into a subquery wrapped by parentheses and then do the left join on that
        if (outer.QueryExpression is SelectExpression selectExpression && selectExpression.Tables.Last() is CrossJoinExpression)
        {
            selectExpression.PushdownIntoSubquery();
        }
        return base.TranslateLeftJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector);
    }

    protected override ShapedQueryExpression? TranslateSkip(ShapedQueryExpression source, Expression count)
    {
        var selectExpression = (SelectExpression)source.QueryExpression;
        /*var translation = TranslateExpression(count);
        if (translation == null)
        {
            return null;
        }

        if (selectExpression.Orderings.Count == 0)
        {
            //_queryCompilationContext.Logger.RowLimitingOperationWithoutOrderByWarning();
        }

        var clone = selectExpression.Clone();
        var limit = clone.Limit;
        if (limit != null)
        {
            var total = new SqlBinaryExpression(ExpressionType.Add, limit, translation, typeof(int),
                RelationalTypeMapping.NullMapping);
            selectExpression.ApplyLimit(total);
        }
        else
        {
            selectExpression.ApplyLimit(translation);
            selectExpression.Tags.Add("DeepSkip");
        }
        selectExpression.ReverseOrderings();
        selectExpression.ApplyLimit(translation);
        selectExpression.ReverseOrderings();*/
        selectExpression.Tags.Add("DeepSkip");
        return base.TranslateSkip(source, count);
    }

    protected override ShapedQueryExpression? TranslateTake(ShapedQueryExpression source, Expression count)
    {
        var selectExpression = (SelectExpression)source.QueryExpression;
        var translation = TranslateExpression(count);
        if (translation == null)
        {
            return null;
        }
        if (selectExpression.Tags.Contains("DeepSkip"))
        {
            SqlExpression offset = selectExpression.Offset!;
            MethodInfo? dynMethodO = selectExpression.GetType().GetMethod("set_Offset",
                BindingFlags.NonPublic | BindingFlags.Instance);
            SqlExpression mynullexp = null!;
            dynMethodO?.Invoke(selectExpression, new object[] { mynullexp });

            var total = new SqlBinaryExpression(ExpressionType.Add, offset, translation, typeof(int),
                RelationalTypeMapping.NullMapping);
            selectExpression.ApplyLimit(total);

            selectExpression.ReverseOrderings();
            selectExpression.ApplyLimit(translation);
            selectExpression.ReverseOrderings();
            selectExpression.Tags.Remove("DeepSkip");
            return source;

            /*var f1 = selectExpression.Tables.First(d => d is SelectExpression) as SelectExpression;
            var f2 = f1?.Tables.First(d => d is SelectExpression) as SelectExpression;
            var limit = f2?.Limit;
            if (limit != null)
            {
                
                var tp = f2!.GetType().GetMember("Limit").First();
                MethodInfo? dynMethod2 = f2.GetType().GetMethod("set_Limit",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                dynMethod2?.Invoke(f2, new object[] { total });
                MethodInfo? dynMethod1 = f1!.GetType().GetMethod("set_Limit",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                dynMethod1?.Invoke(f1, new object[] { translation });
                selectExpression.Tags.Remove("DeepSkip");
                return source;
            }*/

        }
        return base.TranslateTake(source, count);
    }

    protected override ShapedQueryExpression? TranslateFirstOrDefault(ShapedQueryExpression source, LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        var selectExpression = (SelectExpression)source.QueryExpression;
        selectExpression.Tags.Remove("DeepSkip");
        return base.TranslateFirstOrDefault(source, predicate, returnType, returnDefault);
    }

    protected override ShapedQueryExpression? TranslateDefaultIfEmpty(ShapedQueryExpression source, Expression? defaultValue)
    {
        return base.TranslateDefaultIfEmpty(source, defaultValue);
    }
}