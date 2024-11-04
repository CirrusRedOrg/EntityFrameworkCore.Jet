// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Migrations.Internal;
using EntityFrameworkCore.Jet.Query;
using EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;
using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.Jet.Query.Sql.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Update.Internal;
using EntityFrameworkCore.Jet.ValueGeneration.Internal;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="IServiceCollection" />.
    /// </summary>
    public static class JetServiceCollectionExtensions
    {
        public static IServiceCollection AddEntityFrameworkJet(this IServiceCollection serviceCollection)
        {
            Check.NotNull(serviceCollection, nameof(serviceCollection));

            var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
                .TryAdd<LoggingDefinitions, JetLoggingDefinitions>()
                .TryAdd<IDatabaseProvider, DatabaseProvider<JetOptionsExtension>>()
                .TryAdd<IRelationalTypeMappingSource, JetTypeMappingSource>()
                .TryAdd<ISqlGenerationHelper, JetSqlGenerationHelper>()
                .TryAdd<IRelationalAnnotationProvider, JetAnnotationProvider>()
                .TryAdd<IMigrationsAnnotationProvider, JetMigrationsAnnotationProvider>()
                .TryAdd<IModelValidator, JetModelValidator>()
                .TryAdd<IProviderConventionSetBuilder, JetConventionSetBuilder>()
                .TryAdd<IUpdateSqlGenerator>(p => p.GetRequiredService<IJetUpdateSqlGenerator>())
                .TryAdd<IModificationCommandBatchFactory, JetModificationCommandBatchFactory>()
                .TryAdd<IValueGeneratorSelector, JetValueGeneratorSelector>()
                .TryAdd<IRelationalConnection>(p => p.GetRequiredService<IJetRelationalConnection>())
                .TryAdd<IMigrationsSqlGenerator, JetMigrationsSqlGenerator>()
                .TryAdd<IRelationalDatabaseCreator, JetDatabaseCreator>()
                .TryAdd<IMigrationCommandExecutor, JetMigrationCommandExecutor>()
                .TryAdd<IHistoryRepository, JetHistoryRepository>()
                .TryAdd<ICompiledQueryCacheKeyGenerator, JetCompiledQueryCacheKeyGenerator>()
                .TryAdd<IExecutionStrategyFactory, JetExecutionStrategyFactory>()
                .TryAdd<ISingletonOptions, IJetOptions>(p => p.GetRequiredService<IJetOptions>())
                .TryAdd<IQueryCompilationContextFactory, JetQueryCompilationContextFactory>()
                .TryAdd<IMethodCallTranslatorProvider, JetMethodCallTranslatorProvider>()
                .TryAdd<IAggregateMethodCallTranslatorProvider, JetAggregateMethodCallTranslatorProvider>()
                .TryAdd<IMemberTranslatorProvider, JetMemberTranslatorProvider>()
                .TryAdd<IQuerySqlGeneratorFactory, JetQuerySqlGeneratorFactory>()
                .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory, JetSqlTranslatingExpressionVisitorFactory>()
                .TryAdd<ISqlExpressionFactory, JetSqlExpressionFactory>()
                .TryAdd<IQueryTranslationPostprocessorFactory, JetQueryTranslationPostprocessorFactory>()
                .TryAdd<IRelationalTransactionFactory, JetTransactionFactory>()
                .TryAdd<IRelationalParameterBasedSqlProcessorFactory, JetParameterBasedSqlProcessorFactory>()
                .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, JetQueryableMethodTranslatingExpressionVisitorFactory>()
                .TryAddProviderSpecificServices(
                    b => b
                        .TryAddSingleton<IJetOptions, JetOptions>()
                        .TryAddSingleton<IJetUpdateSqlGenerator, JetUpdateSqlGenerator>()
                        .TryAddScoped<IJetRelationalConnection, JetRelationalConnection>());

            builder.TryAddCoreServices();

            return serviceCollection;
        }
    }
}
