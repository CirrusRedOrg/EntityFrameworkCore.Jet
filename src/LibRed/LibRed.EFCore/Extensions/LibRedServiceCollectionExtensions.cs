using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Migrations.Internal;
using EntityFrameworkCore.LibRed.Migrations.Internal;
using EntityFrameworkCore.Jet.Query;
using EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;
using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.Jet.Query.Sql.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Update.Internal;
using EntityFrameworkCore.Jet.Utilities;
using EntityFrameworkCore.Jet.ValueGeneration.Internal;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;
using EntityFrameworkCore.LibRed.Internal;
using EntityFrameworkCore.LibRed.Query.Internal;
using EntityFrameworkCore.LibRed.Query.Sql.Internal;
using EntityFrameworkCore.LibRed.Storage.Internal;
using EntityFrameworkCore.LibRed.Update.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>LibRed-specific registration on top of the EFCore.Jet provider services.</summary>
public static class LibRedServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EFCore.LibRed provider services
    /// </summary>
    public static IServiceCollection AddEntityFrameworkLibRed(this IServiceCollection serviceCollection)
    {
        Check.NotNull(serviceCollection, nameof(serviceCollection));

        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, JetLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<LibRedOptionsExtension>>()
            .TryAdd<IRelationalTypeMappingSource, LibRedTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, JetSqlGenerationHelper>()
            .TryAdd<IRelationalAnnotationProvider, JetAnnotationProvider>()
            .TryAdd<IMigrationsAnnotationProvider, JetMigrationsAnnotationProvider>()
            .TryAdd<IModelValidator, JetModelValidator>()
            .TryAdd<IProviderConventionSetBuilder, LibRedConventionSetBuilder>()
            .TryAdd<IUpdateSqlGenerator>(p => p.GetRequiredService<IJetUpdateSqlGenerator>())
            .TryAdd<IModificationCommandBatchFactory, JetModificationCommandBatchFactory>()
            .TryAdd<IValueGeneratorSelector, JetValueGeneratorSelector>()
            .TryAdd<IRelationalConnection>(p => p.GetRequiredService<ILibRedRelationalConnection>())
            .TryAdd<IMigrationsSqlGenerator, LibRedMigrationsSqlGenerator>()
            .TryAdd<IRelationalDatabaseCreator, LibRedDatabaseCreator>()
            .TryAdd<IHistoryRepository, LibRedHistoryRepository>()
            .TryAdd<ICompiledQueryCacheKeyGenerator, JetCompiledQueryCacheKeyGenerator>()
            .TryAdd<IExecutionStrategyFactory, LibRedExecutionStrategyFactory>()
            .TryAdd<ISingletonOptions, ILibRedOptions>(p => p.GetRequiredService<ILibRedOptions>())
            .TryAdd<IQueryCompilationContextFactory, JetQueryCompilationContextFactory>()
            .TryAdd<IMethodCallTranslatorProvider, JetMethodCallTranslatorProvider>()
            .TryAdd<IAggregateMethodCallTranslatorProvider, JetAggregateMethodCallTranslatorProvider>()
            .TryAdd<IMemberTranslatorProvider, JetMemberTranslatorProvider>()
            .TryAdd<IQuerySqlGeneratorFactory, LibRedQuerySqlGeneratorFactory>()
            .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory, JetSqlTranslatingExpressionVisitorFactory>()
            .TryAdd<ISqlExpressionFactory, JetSqlExpressionFactory>()
            .TryAdd<IQueryTranslationPreprocessorFactory, JetQueryTranslationPreprocessorFactory>()
            .TryAdd<IQueryTranslationPostprocessorFactory, LibRedQueryTranslationPostprocessorFactory>()
            .TryAdd<IRelationalTransactionFactory, LibRedTransactionFactory>()
            .TryAdd<IRelationalParameterBasedSqlProcessorFactory, JetParameterBasedSqlProcessorFactory>()
            .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, JetQueryableMethodTranslatingExpressionVisitorFactory>()
            .TryAddProviderSpecificServices(
                b => b
                    .TryAddSingleton<ILibRedOptions, LibRedOptions>()
                    .TryAddSingleton<IJetUpdateSqlGenerator, LibRedUpdateSqlGenerator>()
                    .TryAddScoped<ILibRedRelationalConnection, LibRedRelationalConnection>());

        builder.TryAddCoreServices();

        return serviceCollection;
    }
}
