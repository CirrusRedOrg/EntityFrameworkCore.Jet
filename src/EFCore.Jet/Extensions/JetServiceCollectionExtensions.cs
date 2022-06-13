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
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="IServiceCollection" />.
    /// </summary>
    public static class JetServiceCollectionExtensions
    {
        public static IServiceCollection AddEntityFrameworkJet([NotNull] this IServiceCollection serviceCollection)
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
                .TryAdd<IUpdateSqlGenerator>(p => p.GetService<IJetUpdateSqlGenerator>())
                .TryAdd<IModificationCommandBatchFactory, JetModificationCommandBatchFactory>()
                .TryAdd<IValueGeneratorSelector, JetValueGeneratorSelector>()
                .TryAdd<IRelationalConnection>(p => p.GetService<IJetRelationalConnection>())
                .TryAdd<IMigrationsSqlGenerator, JetMigrationsSqlGenerator>()
                .TryAdd<IRelationalDatabaseCreator, JetDatabaseCreator>()
                .TryAdd<IHistoryRepository, JetHistoryRepository>()
                .TryAdd<ICompiledQueryCacheKeyGenerator, JetCompiledQueryCacheKeyGenerator>()
                .TryAdd<IExecutionStrategyFactory, JetExecutionStrategyFactory>()
                .TryAdd<ISingletonOptions, IJetOptions>(p => p.GetService<IJetOptions>())
                .TryAdd<IMethodCallTranslatorProvider, JetMethodCallTranslatorProvider>()
                .TryAdd<IMemberTranslatorProvider, JetMemberTranslatorProvider>()
                .TryAdd<IQuerySqlGeneratorFactory, JetQuerySqlGeneratorFactory>()
                .TryAdd<ISqlExpressionFactory, JetSqlExpressionFactory>()
                .TryAdd<IQueryTranslationPostprocessorFactory, JetQueryTranslationPostprocessorFactory>()
                .TryAdd<IRelationalTransactionFactory, JetTransactionFactory>()
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
