// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Jet;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    public class JetConventionSetBuilder : RelationalConventionSetBuilder
    {
        public JetConventionSetBuilder(
            [NotNull] ProviderConventionSetBuilderDependencies dependencies,
            [NotNull] RelationalConventionSetBuilderDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
        }

        public override ConventionSet CreateConventionSet()
        {
            var conventionSet = base.CreateConventionSet();

            var valueGenerationStrategyConvention = new JetValueGenerationStrategyConvention(Dependencies, RelationalDependencies);

            conventionSet.ModelInitializedConventions.Add(valueGenerationStrategyConvention);
            conventionSet.ModelInitializedConventions.Add(
                new RelationalMaxIdentifierLengthConvention(64, Dependencies, RelationalDependencies));

            ValueGenerationConvention valueGenerationConvention =
                new JetValueGenerationConvention(Dependencies, RelationalDependencies);

            ReplaceConvention(conventionSet.EntityTypeBaseTypeChangedConventions, valueGenerationConvention);
            ReplaceConvention(conventionSet.EntityTypeAnnotationChangedConventions, (RelationalValueGenerationConvention) valueGenerationConvention);
            ReplaceConvention(conventionSet.EntityTypePrimaryKeyChangedConventions, valueGenerationConvention);
            ReplaceConvention(conventionSet.ForeignKeyAddedConventions, valueGenerationConvention);
            ReplaceConvention(conventionSet.ForeignKeyRemovedConventions, valueGenerationConvention);

            StoreGenerationConvention storeGenerationConvention = new JetStoreGenerationConvention(Dependencies, RelationalDependencies);

            ReplaceConvention(conventionSet.PropertyAnnotationChangedConventions, storeGenerationConvention);
            ReplaceConvention(conventionSet.PropertyAnnotationChangedConventions, (RelationalValueGenerationConvention) valueGenerationConvention);

            ConventionSet.AddBefore(
                conventionSet.ModelFinalizedConventions,
                valueGenerationStrategyConvention,
                typeof(ValidatingConvention));
            
            ReplaceConvention(conventionSet.ModelFinalizedConventions, storeGenerationConvention);

            return conventionSet;
        }

        public static ConventionSet Build()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddDbContext<DbContext>((p, o) => o
                    .UseJetWithoutPredefinedDataAccessProviderFactory<DbContext>(
                        JetConnection.GetConnectionString("Jet.accdb", DataAccessType.Odbc))
                    .UseInternalServiceProvider(p))
                .BuildServiceProvider();

            using var serviceScope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            using var context = serviceScope.ServiceProvider.GetService<DbContext>();
            return ConventionSet.CreateConventionSet(context);
        }
    }
}