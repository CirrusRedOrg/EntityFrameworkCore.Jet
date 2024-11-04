using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;
#pragma warning disable CS9113
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
[EntityFrameworkInternal]
public class JetLiftOrderByPostprocessor(IRelationalTypeMappingSource typeMappingSource,
    ISqlExpressionFactory sqlExpressionFactory,
    SqlAliasManager sqlAliasManager)
    : ExpressionVisitor 
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual Expression Process(Expression expression)
    {
        return Visit(expression);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    [return: NotNullIfNotNull(nameof(expression))]
    public override Expression? Visit(Expression? expression)
    {
        switch (expression)
        {
            case ShapedQueryExpression shapedQueryExpression:
                shapedQueryExpression = shapedQueryExpression
                    .UpdateQueryExpression(Visit(shapedQueryExpression.QueryExpression))
                    .UpdateShaperExpression(Visit(shapedQueryExpression.ShaperExpression));

                return shapedQueryExpression.UpdateShaperExpression(Visit(shapedQueryExpression.ShaperExpression));
            case RelationalSplitCollectionShaperExpression relationalSplitCollectionShaperExpression:
                var newSelect = Visit(relationalSplitCollectionShaperExpression.SelectExpression);
                var newInner = Visit(relationalSplitCollectionShaperExpression.InnerShaper);
                relationalSplitCollectionShaperExpression = relationalSplitCollectionShaperExpression.Update(
                    relationalSplitCollectionShaperExpression.ParentIdentifier,
                    relationalSplitCollectionShaperExpression.ChildIdentifier, (SelectExpression)newSelect, newInner);
                return relationalSplitCollectionShaperExpression;
            case NonQueryExpression nonQueryExpression:
                return nonQueryExpression;
            case SelectExpression selectExpression:
                {
                    Dictionary<int, (int? indexcol, OrderingExpression? orderexp, bool ascend, bool rewrite, bool referstocurouter)> columnsToRewrite = [];
                    bool isscalarselect = selectExpression is { Limit: SqlConstantExpression { Value: 1 }, Projection.Count: 1 };
                    for (int i = 0; i < selectExpression.Orderings.Count; i++)
                    {
                        var sqlExpression = selectExpression.Orderings[i].Expression;
                        if (sqlExpression is not ColumnExpression && sqlExpression is not SqlConstantExpression && sqlExpression is not SqlParameterExpression)
                        {
                            var locate = new JetLocateScalarSubqueryVisitor(typeMappingSource, sqlExpressionFactory);
                            var locatedExpression = locate.Visit(sqlExpression);
                            bool containsscalar = locatedExpression is ScalarSubqueryExpression or ExistsExpression;
                            if (containsscalar)
                            {
                                int index = selectExpression.AddToProjection(sqlExpression);
                                columnsToRewrite.Add(i, (index, null, selectExpression.Orderings[i].IsAscending, true, false));
                                continue;
                            }

                            var existingIndex = selectExpression.Projection.ToList().FindIndex(pe => pe.Expression.Equals(sqlExpression));
                            if (existingIndex != -1)
                            {
                                columnsToRewrite.Add(i, (existingIndex, null, selectExpression.Orderings[i].IsAscending, true, false));
                            }
                        }
                        else
                        {
                            var existingIndex = selectExpression.Projection.ToList().FindIndex(pe => pe.Expression.Equals(sqlExpression));
                            if (existingIndex != -1)
                            {
                                bool referouter = sqlExpression is ColumnExpression colexp1 &&
                                    selectExpression.Tables.Select(d => d.Alias).Contains(colexp1.TableAlias);
                                columnsToRewrite.Add(i,
                                    (existingIndex, selectExpression.Orderings[i], selectExpression.Orderings[i].IsAscending, false, referouter));
                            }
                            
                        }
                    }

                    if (columnsToRewrite.Count == 0 || columnsToRewrite.All(p => p.Value.rewrite == false))
                    {
                        return base.Visit(expression);
                    }

                    selectExpression.ClearOrdering();
                    //Keep the limit in parent expression
                    if (selectExpression.Limit != null)
                    {
                        var limit = selectExpression.Limit;
                        MethodInfo? dynMethod1 = selectExpression.GetType().GetMethod("set_Limit",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        dynMethod1?.Invoke(selectExpression, [null]);

                        //This doesn't work. Update returns a new select expression but without the sql alias manager. Pushdown requires the alias manager
                        /*selectExpression = selectExpression.Update(selectExpression.Tables,
                            selectExpression.Predicate, selectExpression.GroupBy, selectExpression.Having, selectExpression.Projection,
                            selectExpression.Orderings, null, null);*/
                        selectExpression = AddAliasManager(selectExpression);
                        selectExpression.PushdownIntoSubquery();
                        selectExpression.ApplyLimit(limit);
                    }
                    else
                    {
                        selectExpression = AddAliasManager(selectExpression);
                        selectExpression.PushdownIntoSubquery();
                    }

                    foreach (var colr in columnsToRewrite)
                    {
                        (int? index, OrderingExpression? oexp, bool ascending, bool rewrite, bool referstocurouter) = colr.Value;
                        if (index.HasValue)
                        {
                            var proj = selectExpression.Projection[index.Value];
                            selectExpression.AppendOrdering(new OrderingExpression(proj.Expression, ascending));
                        }
                        else if (oexp != null)
                        {
                            var col = oexp.Expression as ColumnExpression;
                            var newcolexp = selectExpression.CreateColumnExpression(selectExpression.Tables[0], col!.Name, col.Type,
                                col.TypeMapping, col.IsNullable);
                            selectExpression.AppendOrdering(new OrderingExpression(newcolexp, ascending));
                        }
                    }

                    if (isscalarselect && selectExpression.Projection.Count > 1)
                    {
                        List<ProjectionExpression> newProjections = [selectExpression.Projection[0]];
                        selectExpression = selectExpression.Update(selectExpression.Tables, selectExpression.Predicate,
                            selectExpression.GroupBy, selectExpression.Having, newProjections, selectExpression.Orderings,
                            selectExpression.Offset, selectExpression.Limit);
                    }
                    var result = base.Visit(selectExpression);
                    return result;
                }
            case RelationalGroupByShaperExpression relationalGroupByShaperExpression:
            {
                return base.VisitExtension(relationalGroupByShaperExpression);
            }
        }

        return base.Visit(expression);
    }

    private SelectExpression AddAliasManager(SelectExpression selectExpression)
    {
        //get private IsMutable property
        var ismutable = selectExpression.GetType().GetProperty("IsMutable", BindingFlags.NonPublic | BindingFlags.Instance);
        //get value
        var ismut = (bool)ismutable?.GetValue(selectExpression)!;

        //create new selectexp from selectexpression with aliasmanager
        var newselect = new SelectExpression(selectExpression.Alias,
            [.. selectExpression.Tables],
            selectExpression.Predicate, [.. selectExpression.GroupBy], selectExpression.Having,
            [.. selectExpression.Projection], selectExpression.IsDistinct,
            [.. selectExpression.Orderings], selectExpression.Offset, selectExpression.Limit,
            selectExpression.Tags, new Dictionary<string, IAnnotation>(), sqlAliasManager, ismut);

        //do private stuff
        //_projectionMapping = newProjectionMappings,
        //_clientProjections = newClientProjections,
        var clientProj = selectExpression.GetType().GetField("_clientProjections", BindingFlags.NonPublic | BindingFlags.Instance);
        var projMap = selectExpression.GetType().GetField("_projectionMapping", BindingFlags.NonPublic | BindingFlags.Instance);
        clientProj?.SetValue(newselect, clientProj.GetValue(selectExpression));
        projMap?.SetValue(newselect, projMap.GetValue(selectExpression));
        return newselect;
    }
}
