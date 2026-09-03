// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal
{
    /// <summary>
    ///     Guards <b>row-independent</b> projections — literals and SQL fragments — inside a subquery on the
    ///     nullable side of a <c>LEFT JOIN</c>, so Jet/ACE nulls them out on a no-match row like every other column.
    ///     <para>
    ///         Jet/ACE evaluates a derived table's projected expression <em>after</em> the join, against the
    ///         all-NULL row, instead of treating the absent row as NULL wholesale. Correctness then depends purely
    ///         on whether the expression propagates NULL: <c>(col * 0) + 1</c> and aggregates come back NULL
    ///         (right), while a bare literal has no NULL input and survives the join unchanged (wrong). Allen
    ///         Browne documented the same defect for <c>&amp;</c> concatenation in March 2008 and it still
    ///         reproduces unchanged on ACE 2010 and on Office 365's ACE 16, x86 and x64.
    ///     </para>
    ///     <para>
    ///         Verified against ACE, for a subquery row that matched nothing:
    ///         <code>
    ///         1                      -> 1     WRONG (no NULL input to propagate)
    ///         IIF(key IS NULL, 1, 1) -> 1     WRONG (still row-independent; the trap)
    ///         a &amp; '.' &amp; b          -> '.'   WRONG (Access's '&amp;' coerces NULL to "")
    ///         a + '.' + b            -> NULL  right
    ///         (col * 0) + 1          -> NULL  right
    ///         MIN(1), MIN(col), COUNT(*) -> NULL  right
    ///         IIF(key IS NULL, NULL, 1)  -> NULL  right — the form used here
    ///         </code>
    ///     </para>
    ///     <para>
    ///         This matters because EF Core 11 (dotnet/efcore#30915, PR #38479) fixed whole-object materialization
    ///         off the nullable side of a group join by injecting <c>1 AS marker</c> into that subquery and gating
    ///         on <c>marker == null ? null : new { ... }</c>. On ACE the marker returns <c>1</c> for a row that
    ///         matched nothing, so the gate concludes "matched" and the shaper throws
    ///         <c>Nullable object must have a value</c> unwrapping a genuinely NULL aggregate.
    ///     </para>
    ///     <para>
    ///         The rewrite emits <c>CASE WHEN anchor IS NULL THEN NULL ELSE &lt;original&gt; END</c>, which
    ///         <c>JetQuerySqlGenerator</c> renders as <c>IIF(anchor IS NULL, NULL, &lt;original&gt;)</c>. The NULL
    ///         branch is essential — <c>IIF(anchor IS NULL, 1, 1)</c> is still row-independent and equally wrong.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this runs where it does.</b> It is deliberately not a query-translation postprocessor. The
    ///         guard's test, <c>anchor IS NULL</c>, is provably false — the anchor is non-nullable within the
    ///         subquery — so <c>SqlNullabilityProcessor</c> folds the whole <c>CASE</c> straight back to its
    ///         <c>ELSE</c> branch, restoring the bare literal. Applied in
    ///         <c>JetParameterBasedSqlProcessor</c> <em>after</em> <c>base.Process</c>, it runs past that
    ///         optimization and survives to SQL generation. Moving it earlier silently reverts it: the tree looks
    ///         correct on the way out and the generator still receives the original literal.
    ///     </para>
    ///     <para>
    ///         <b>Why it mutates in place.</b> <c>SelectExpression.Update</c> returns a select stripped of its SQL
    ///         alias manager (see the note in <c>JetLiftOrderByPostprocessor</c>), and substituting a new subquery
    ///         instance would leave the parent's <c>ColumnExpression</c>s bound to the old one. Replacing entries
    ///         in the select's own projection list keeps every existing reference valid.
    ///     </para>
    ///     <para>
    ///         This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///         the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///         any release.
    ///     </para>
    /// </remarks>
    public sealed class JetOuterJoinProjectionGuardExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
        : ExpressionVisitor
    {
        /// <summary>The select's own projection list, replaced in place — see the remarks on this type.</summary>
        private static readonly FieldInfo SelectExpressionProjectionField =
            typeof(SelectExpression).GetField("_projection", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SelectExpression._projection.");

        /// <summary>Subqueries seen as the target of a <c>LEFT JOIN</c>, by reference.</summary>
        private readonly HashSet<SelectExpression> _leftJoinTargets = new(ReferenceEqualityComparer.Instance);

        [return: NotNullIfNotNull(nameof(expression))]
        public override Expression? Visit(Expression? expression)
        {
            switch (expression)
            {
                // ShapedQueryExpression and the split-collection shaper forbid generic child visiting.
                case ShapedQueryExpression shapedQueryExpression:
                    return shapedQueryExpression.Update(
                        Visit(shapedQueryExpression.QueryExpression),
                        Visit(shapedQueryExpression.ShaperExpression));

                case RelationalSplitCollectionShaperExpression splitCollectionShaper:
                    return splitCollectionShaper.Update(
                        splitCollectionShaper.ParentIdentifier,
                        splitCollectionShaper.ChildIdentifier,
                        (SelectExpression)Visit(splitCollectionShaper.SelectExpression),
                        Visit(splitCollectionShaper.InnerShaper));

                case UpdateExpression or DeleteExpression:
                    return expression;

                case SelectExpression selectExpression:
                {
                    // Record this select's LEFT JOIN targets before descending, so we recognise one when
                    // VisitChildren reaches it.
                    foreach (var table in selectExpression.Tables)
                    {
                        if (table is LeftJoinExpression { Table: SelectExpression target })
                        {
                            _leftJoinTargets.Add(target);
                        }
                    }

                    if (_leftJoinTargets.Contains(selectExpression))
                    {
                        Guard(selectExpression);
                    }

                    return base.VisitExtension(selectExpression);
                }

                default:
                    return base.Visit(expression);
            }
        }

        private void Guard(SelectExpression selectExpression)
        {
            // Anchor on something non-NULL for any row that actually matched: the grouping key when the subquery
            // groups (referencing anything else there would not be legal), otherwise any projected column.
            var anchor = selectExpression.GroupBy.FirstOrDefault()
                ?? selectExpression.Projection.Select(p => p.Expression).OfType<ColumnExpression>().FirstOrDefault();

            if (anchor is null)
            {
                return;
            }

            var projections = (List<ProjectionExpression>)SelectExpressionProjectionField.GetValue(selectExpression)!;

            for (var i = 0; i < projections.Count; i++)
            {
                var projection = projections[i];

                if (!IsMarker(projection.Expression))
                {
                    continue;
                }

                var guardedExpression = sqlExpressionFactory.Case(
                    [
                        new CaseWhenClause(
                            sqlExpressionFactory.IsNull(anchor),
                            sqlExpressionFactory.Constant(
                                null,
                                projection.Expression.Type,
                                projection.Expression.TypeMapping ?? anchor.TypeMapping))
                    ],
                    projection.Expression);

                projections[i] = new ProjectionExpression(guardedExpression, projection.Alias);
            }
        }

        /// <summary>
        ///     Whether this projection is EF's synthetic null marker.
        ///     <para>
        ///         Deliberately narrow. Jet's post-join evaluation mis-handles <em>every</em> row-independent
        ///         projection, but only the marker is load-bearing: EF reads it to decide whether the row matched.
        ///         A user's own constant — <c>select new { c = "BFG" }</c> over a correlated collection — is
        ///         projected as a <see cref="SqlConstantExpression" /> and is never consulted for null-ness (the
        ///         identifier column decides that), so guarding it would only churn SQL and its baselines.
        ///     </para>
        ///     <para>
        ///         The marker is distinguishable because EF injects it as a raw <see cref="SqlFragmentExpression" />
        ///         (<c>1 AS marker</c>, dotnet/efcore PR #38479) rather than as a translated constant.
        ///     </para>
        /// </summary>
        private static bool IsMarker(SqlExpression expression)
            => expression is SqlFragmentExpression;
    }
}
