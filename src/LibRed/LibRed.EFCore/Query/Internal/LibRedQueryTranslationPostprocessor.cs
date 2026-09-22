using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.LibRed.Infrastructure;

namespace EntityFrameworkCore.LibRed.Query.Internal
{
    public class LibRedQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
    {
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

            if (_sqlMode == LibRedSqlMode.Compatible)
            {
                query = _liftOrderByPostprocessor.Process(query);
            }

            return query;
        }
    }
}