// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.Internal
{
    public class JetQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
    {
        private static readonly FieldInfo SelectExpressionIdentifierField = typeof(SelectExpression).GetField(
            "_identifier",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SelectExpression._identifier.");

        private readonly IRelationalTypeMappingSource _relationalTypeMappingSource;
        private readonly IJetOptions _options;
        private readonly SkipWithoutOrderByInSplitQueryVerifier _skipWithoutOrderByInSplitQueryVerifier = new();
        private readonly JetLiftOrderByPostprocessor _liftOrderByPostprocessor;
        private readonly JetSkipTakePostprocessor _skipTakePostprocessor;

        public JetQueryTranslationPostprocessor(
            QueryTranslationPostprocessorDependencies dependencies,
            RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
            RelationalQueryCompilationContext queryCompilationContext,
            IRelationalTypeMappingSource relationalTypeMappingSource,
            IJetOptions options)
            : base(dependencies, relationalDependencies, queryCompilationContext)
        {
            _relationalTypeMappingSource = relationalTypeMappingSource;
            _options = options;
            _liftOrderByPostprocessor = new JetLiftOrderByPostprocessor(relationalTypeMappingSource, relationalDependencies.SqlExpressionFactory, queryCompilationContext.SqlAliasManager);
            _skipTakePostprocessor = new JetSkipTakePostprocessor(relationalTypeMappingSource,
                relationalDependencies.SqlExpressionFactory, ((RelationalQueryCompilationContext)QueryCompilationContext).QuerySplittingBehavior);
        }

        public override Expression Process(Expression query)
        {
            query = _skipTakePostprocessor.Process(query);
            //query = _liftOrderByPostprocessor.Process(query);
            query = base.Process(query);

            var identifiers = GetIdentifiers(query);

            if (identifiers.Count > 0
                && query is ShapedQueryExpression { QueryExpression: SelectExpression selectExpression }
                && !selectExpression.Orderings.Any(
                    ordering => ordering.Expression.Equals(identifiers[^1].Column)) && selectExpression.Orderings.Any())
            {
                selectExpression.AppendOrdering(
                    new OrderingExpression(identifiers[^1].Column, ascending: true));
            }

            //query = _skipTakePostprocessor.Process(query);
            if (_options.EnableMillisecondsSupport)
            {
                query = new JetDateTimeExpressionVisitor(RelationalDependencies.SqlExpressionFactory, _relationalTypeMappingSource).Visit(query);
            }
            //query = _skipWithoutOrderByInSplitQueryVerifier.Visit(query);
            //query = _skipTakePostprocessor.Process(query);
            query = _liftOrderByPostprocessor.Process(query);

            return query;
        }

        private static IReadOnlyList<(ColumnExpression Column, ValueComparer Comparer)> GetIdentifiers(Expression query)
        {
            if (query is not ShapedQueryExpression { QueryExpression: SelectExpression selectExpression })
            {
                return [];
            }

            return (IReadOnlyList<(ColumnExpression Column, ValueComparer Comparer)>)SelectExpressionIdentifierField.GetValue(selectExpression)!;
        }

        private sealed class SkipWithoutOrderByInSplitQueryVerifier : ExpressionVisitor
        {
            [return: NotNullIfNotNull("expression")]
            public override Expression? Visit(Expression? expression)
            {
                switch (expression)
                {
                    case ShapedQueryExpression shapedQueryExpression:
                        Visit(shapedQueryExpression.ShaperExpression);
                        return shapedQueryExpression;

                    case RelationalSplitCollectionShaperExpression relationalSplitCollectionShaperExpression:
                        foreach (var table in relationalSplitCollectionShaperExpression.SelectExpression.Tables)
                        {
                            Visit(table);
                        }

                        Visit(relationalSplitCollectionShaperExpression.InnerShaper);

                        return relationalSplitCollectionShaperExpression;

                    case SelectExpression { Offset: not null, Orderings.Count: 0 }:
                        throw new InvalidOperationException(JetStrings.SplitQueryOffsetWithoutOrderBy);

                    case UpdateExpression or DeleteExpression:
                        return expression;

                    default:
                        return base.Visit(expression);
                }
            }
        }
    }
}
