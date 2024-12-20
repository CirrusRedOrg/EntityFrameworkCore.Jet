using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public class JetSkipTakePostprocessor : ExpressionVisitor
{
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private Stack<SelectExpression> parent = new();
    private readonly QuerySplittingBehavior? _splittingBehavior;
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetSkipTakePostprocessor(
        IRelationalTypeMappingSource typeMappingSource,
        ISqlExpressionFactory sqlExpressionFactory,
        QuerySplittingBehavior? splittingBehavior)
    {
        (_typeMappingSource, _sqlExpressionFactory) = (typeMappingSource, sqlExpressionFactory);
        _splittingBehavior = splittingBehavior;
    }

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
                return shapedQueryExpression.UpdateQueryExpression(Visit(shapedQueryExpression.QueryExpression));
            case SelectExpression selectExpression:
                {
                    if (selectExpression.Orderings.Count == 0 && selectExpression.Offset is not null && _splittingBehavior == QuerySplittingBehavior.SplitQuery)
                    {
                        throw new InvalidOperationException(JetStrings.SplitQueryOffsetWithoutOrderBy);
                    }
                    else if (selectExpression.Offset is not null && selectExpression.Limit is not null)
                    {
                        SqlExpression offset = selectExpression.Offset!;
                        SqlExpression limit = selectExpression.Limit!;
                        if (offset is ColumnExpression || limit is ColumnExpression)
                        {
                            throw new InvalidOperationException(
                                "Unsupported Jet expression: Limit or offset can not reference a column");
                        }
                        var total = new SqlBinaryExpression(ExpressionType.Add, offset, limit, typeof(int),
                            RelationalTypeMapping.NullMapping);
                        MethodInfo? dynMethodO = selectExpression.GetType().GetMethod("set_Offset",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        MethodInfo? dynMethod1 = selectExpression.GetType().GetMethod("set_Limit",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        SqlExpression mynullexp = null!;
                        dynMethodO?.Invoke(selectExpression, [mynullexp]);
                        dynMethod1?.Invoke(selectExpression, [total]);

                        selectExpression.ReverseOrderings();

                        selectExpression.ApplyLimit(limit);

                        parent.TryPeek(out var parentselect);
                        if (parentselect != null && parentselect.Orderings.Count > 0)
                        {
                        }
                        else
                        {
                            selectExpression.ReverseOrderings();
                        }
                    }

                    parent.Push(selectExpression);
                    var newselectExpression = (SelectExpression)base.Visit(selectExpression);
                    parent.Pop();
                    return newselectExpression;
                }
            default:
                return base.Visit(expression);
        }
    }
}
