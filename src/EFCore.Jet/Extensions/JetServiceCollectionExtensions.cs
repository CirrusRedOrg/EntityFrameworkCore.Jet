// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata.Conventions;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Migrations;
using EntityFrameworkCore.Jet.Migrations.Internal;
using EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;
using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.Jet.Query.Sql.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Update.Internal;
using EntityFrameworkCore.Jet.ValueGeneration.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.ExpressionTranslators;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Extensions.DependencyInjection
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="IServiceCollection" />.
    /// </summary>
    public static class JetServiceCollectionExtensions
    {
        /// <summary>
        ///     <para>
        ///         Adds the services required by the Microsoft Jet database provider for Entity Framework
        ///         to an <see cref="IServiceCollection" />. You use this method when using dependency injection
        ///         in your application, such as with ASP.NET. For more information on setting up dependency
        ///         injection, see http://go.microsoft.com/fwlink/?LinkId=526890.
        ///     </para>
        ///     <para>
        ///         You only need to use this functionality when you want Entity Framework to resolve the services it uses
        ///         from an external dependency injection container. If you are not using an external
        ///         dependency injection container, Entity Framework will take care of creating the services it requires.
        ///     </para>
        /// </summary>
        /// <example>
        ///     <code>
        ///           public void ConfigureServices(IServiceCollection services)
        ///           {
        ///               var connectionString = "connection string to database";
        /// 
        ///               services
        ///                   .AddEntityFrameworkJet()
        ///                   .AddDbContext&lt;MyContext&gt;((serviceProvider, options) =>
        ///                       options.UseJet(connectionString)
        ///                              .UseInternalServiceProvider(serviceProvider));
        ///           }
        ///       </code>
        /// </example>
        /// <param name="serviceCollection"> The <see cref="IServiceCollection" /> to add services to. </param>
        /// <returns>
        ///     The same service collection so that multiple calls can be chained.
        /// </returns>
        public static IServiceCollection AddEntityFrameworkJet([NotNull] this IServiceCollection serviceCollection)
        {
            Check.NotNull(serviceCollection, nameof(serviceCollection));

            var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
                .TryAdd<IDatabaseProvider, DatabaseProvider<JetOptionsExtension>>()
                .TryAdd<IValueGeneratorCache>(p => p.GetService<IJetValueGeneratorCache>())
                .TryAdd<IRelationalTypeMappingSource, JetTypeMappingSource>()
                
                .TryAdd<IEntityMaterializerSource, JetEntityMaterializerSource>()

                .TryAdd<ISqlGenerationHelper, JetSqlGenerationHelper>()
                .TryAdd<IMigrationsAnnotationProvider, JetMigrationsAnnotationProvider>()
                .TryAdd<IModelValidator, JetModelValidator>()
                .TryAdd<IConventionSetBuilder, JetConventionSetBuilder>()
                .TryAdd<IUpdateSqlGenerator>(p => p.GetService<IJetUpdateSqlGenerator>())
                .TryAdd<IModificationCommandBatchFactory, JetModificationCommandBatchFactory>()
                .TryAdd<IValueGeneratorSelector, JetValueGeneratorSelector>()
                .TryAdd<IRelationalConnection>(p => p.GetService<IJetRelationalConnection>())
                .TryAdd<IMigrationsSqlGenerator, JetMigrationsSqlGenerator>()
                .TryAdd<IRelationalDatabaseCreator, JetDatabaseCreator>()
                .TryAdd<IHistoryRepository, JetHistoryRepository>()
                .TryAdd<ICompiledQueryCacheKeyGenerator, JetCompiledQueryCacheKeyGenerator>()
                .TryAdd<IQueryCompilationContextFactory, JetQueryCompilationContextFactory>()
                .TryAdd<IMemberTranslator, JetCompositeMemberTranslator>()
                .TryAdd<ICompositeMethodCallTranslator, JetCompositeMethodCallTranslator>()
                .TryAdd<IQuerySqlGeneratorFactory, JetQuerySqlGeneratorFactory>()
                .TryAdd<ISingletonOptions, IJetOptions>(p => p.GetService<IJetOptions>())
                .TryAddProviderSpecificServices(
                    b => b
                        .TryAddSingleton<IJetValueGeneratorCache, JetValueGeneratorCache>()
                        .TryAddSingleton<IJetOptions, JetOptions>()
                        .TryAddScoped<IJetUpdateSqlGenerator, JetUpdateSqlGenerator>()
                        .TryAddScoped<IJetSequenceValueGeneratorFactory, JetSequenceValueGeneratorFactory>()
                        .TryAddScoped<IJetRelationalConnection, JetRelationalConnection>());

            builder.TryAddCoreServices();

            return serviceCollection;
        }
    }
}
