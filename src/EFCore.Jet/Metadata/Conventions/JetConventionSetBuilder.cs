// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata.Conventions.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Metadata.Conventions
{
    public class JetConventionSetBuilder : RelationalConventionSetBuilder
    {
        private readonly ISqlGenerationHelper _sqlGenerationHelper;

        public JetConventionSetBuilder(
            [NotNull] RelationalConventionSetBuilderDependencies dependencies,
            [NotNull] ISqlGenerationHelper sqlGenerationHelper)
            : base(dependencies)
        {
            _sqlGenerationHelper = sqlGenerationHelper;
        }

        public override ConventionSet AddConventions(ConventionSet conventionSet)
        {
            Check.NotNull(conventionSet, nameof(conventionSet));

            base.AddConventions(conventionSet);

            var valueGenerationStrategyConvention = new JetValueGenerationStrategyConvention();
            conventionSet.ModelInitializedConventions.Add(valueGenerationStrategyConvention);

            ValueGeneratorConvention valueGeneratorConvention = new JetValueGeneratorConvention();
            ReplaceConvention(conventionSet.BaseEntityTypeChangedConventions, valueGeneratorConvention);

            var jetInMemoryTablesConvention = new JetMemoryOptimizedTablesConvention();
            conventionSet.EntityTypeAnnotationChangedConventions.Add(jetInMemoryTablesConvention);

            ReplaceConvention(conventionSet.PrimaryKeyChangedConventions, valueGeneratorConvention);

            conventionSet.KeyAddedConventions.Add(jetInMemoryTablesConvention);

            ReplaceConvention(conventionSet.ForeignKeyAddedConventions, valueGeneratorConvention);

            ReplaceConvention(conventionSet.ForeignKeyRemovedConventions, valueGeneratorConvention);

            var jetIndexConvention = new JetIndexConvention(_sqlGenerationHelper);
            conventionSet.IndexAddedConventions.Add(jetInMemoryTablesConvention);
            conventionSet.IndexAddedConventions.Add(jetIndexConvention);

            conventionSet.IndexUniquenessChangedConventions.Add(jetIndexConvention);

            conventionSet.IndexAnnotationChangedConventions.Add(jetIndexConvention);

            conventionSet.PropertyNullabilityChangedConventions.Add(jetIndexConvention);

            conventionSet.PropertyAnnotationChangedConventions.Add(jetIndexConvention);
            conventionSet.PropertyAnnotationChangedConventions.Add((JetValueGeneratorConvention)valueGeneratorConvention);

            ReplaceConvention(conventionSet.ModelAnnotationChangedConventions, (RelationalDbFunctionConvention)new JetDbFunctionConvention());

            return conventionSet;
        }

        public static ConventionSet Build()
        {
            var jetTypeMapper = new JetTypeMapper(new RelationalTypeMapperDependencies());

            return new JetConventionSetBuilder(
                    new RelationalConventionSetBuilderDependencies(jetTypeMapper, null, null),
                    new JetSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies()))
                .AddConventions(
                    new CoreConventionSetBuilder(
                            new CoreConventionSetBuilderDependencies(jetTypeMapper))
                        .CreateConventionSet());
        }
    }
}
