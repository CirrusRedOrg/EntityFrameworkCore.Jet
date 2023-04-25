// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    public class JetConventionSetBuilder : RelationalConventionSetBuilder
    {
        private readonly ISqlGenerationHelper _sqlGenerationHelper;
        public JetConventionSetBuilder(
            [NotNull] ProviderConventionSetBuilderDependencies dependencies,
            [NotNull] RelationalConventionSetBuilderDependencies relationalDependencies,
            ISqlGenerationHelper sqlGenerationHelper)
            : base(dependencies, relationalDependencies)
        {
            _sqlGenerationHelper = sqlGenerationHelper;
        }

        public override ConventionSet CreateConventionSet()
        {
            var conventionSet = base.CreateConventionSet();

            conventionSet.Add(new JetValueGenerationStrategyConvention(Dependencies, RelationalDependencies));
            conventionSet.Add(new RelationalMaxIdentifierLengthConvention(64, Dependencies, RelationalDependencies));
            conventionSet.Add(new JetIndexConvention(Dependencies, RelationalDependencies, _sqlGenerationHelper));

            conventionSet.Replace<StoreGenerationConvention>(
                new JetStoreGenerationConvention(Dependencies, RelationalDependencies));
            conventionSet.Replace<ValueGenerationConvention>(
                new JetValueGenerationConvention(Dependencies, RelationalDependencies));

            return conventionSet;
        }

        public static ConventionSet Build()
        {
            using var serviceScope = CreateServiceScope();
            using var context = serviceScope.ServiceProvider.GetRequiredService<DbContext>();
            return ConventionSet.CreateConventionSet(context);
        }

        public static ModelBuilder CreateModelBuilder()
        {
            using var serviceScope = CreateServiceScope();
            using var context = serviceScope.ServiceProvider.GetRequiredService<DbContext>();
            return new ModelBuilder(ConventionSet.CreateConventionSet(context), context.GetService<ModelDependencies>());
        }

        private static IServiceScope CreateServiceScope()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddDbContext<DbContext>((p, o) => o
                    .UseJetWithoutPredefinedDataAccessProvider(
                        JetConnection.GetConnectionString("Jet.accdb", DataAccessProviderType.Odbc))
                    .UseInternalServiceProvider(p))
                .BuildServiceProvider();

            return serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        }
    }
}