using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.LibRed.Query.Internal
{
    public class LibRedQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
    {
        private static readonly FieldInfo SelectExpressionIdentifierField = typeof(SelectExpression).GetField(
            "_identifier",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SelectExpression._identifier.");

        private readonly IRelationalTypeMappingSource _relationalTypeMappingSource;
        private readonly JetLiftOrderByPostprocessor _liftOrderByPostprocessor;
        private readonly JetSkipTakePostprocessor _skipTakePostprocessor;
        private readonly LibRedSqlMode _sqlMode;

        public LibRedQueryTranslationPostprocessor(
            QueryTranslationPostprocessorDependencies dependencies,
            RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
            RelationalQueryCompilationContext queryCompilationContext,
            IRelationalTypeMappingSource relationalTypeMappingSource,
            LibRedSqlMode sqlMode)
            : base(dependencies, relationalDependencies, queryCompilationContext)
        {
            _relationalTypeMappingSource = relationalTypeMappingSource;
            _sqlMode = sqlMode;
            _liftOrderByPostprocessor = new JetLiftOrderByPostprocessor(relationalTypeMappingSource, relationalDependencies.SqlExpressionFactory, queryCompilationContext.SqlAliasManager);
            _skipTakePostprocessor = new JetSkipTakePostprocessor(relationalTypeMappingSource,
                relationalDependencies.SqlExpressionFactory, ((RelationalQueryCompilationContext)QueryCompilationContext).QuerySplittingBehavior);
        }

        public override Expression Process(Expression query)
        {
            if (_sqlMode == LibRedSqlMode.Compatible)
            {
                query = _skipTakePostprocessor.Process(query);
            }

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

            if (_sqlMode == LibRedSqlMode.Compatible)
            {
                query = _liftOrderByPostprocessor.Process(query);
            }

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
    }
}
