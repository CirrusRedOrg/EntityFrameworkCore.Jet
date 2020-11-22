// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.Metadata;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    /// <summary>
    ///     A convention that configures the default model <see cref="JetValueGenerationStrategy" /> as
    ///     <see cref="JetValueGenerationStrategy.IdentityColumn" />.
    /// </summary>
    public class JetValueGenerationStrategyConvention : IModelInitializedConvention, IModelFinalizedConvention
    {
        /// <summary>
        ///     Creates a new instance of <see cref="JetValueGenerationStrategyConvention" />.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this convention. </param>
        /// <param name="relationalDependencies">  Parameter object containing relational dependencies for this convention. </param>
        public JetValueGenerationStrategyConvention(
            [NotNull] ProviderConventionSetBuilderDependencies dependencies,
            [NotNull] RelationalConventionSetBuilderDependencies relationalDependencies)
        {
            Dependencies = dependencies;
        }

        /// <summary>
        ///     Parameter object containing service dependencies.
        /// </summary>
        protected virtual ProviderConventionSetBuilderDependencies Dependencies { get; }

        /// <summary>
        ///     Called after a model is initialized.
        /// </summary>
        /// <param name="modelBuilder"> The builder for the model. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessModelInitialized(
            IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            modelBuilder.HasValueGenerationStrategy(JetValueGenerationStrategy.IdentityColumn);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Called after a model is finalized.
        /// </summary>
        /// <param name="modelBuilder"> The builder for the model. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessModelFinalized(
            IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (var property in entityType.GetDeclaredProperties())
                {
                    // Needed for the annotation to show up in the model snapshot
                    var strategy = property.GetValueGenerationStrategy();
                    if (strategy != JetValueGenerationStrategy.None)
                    {
                        property.Builder.HasValueGenerationStrategy(strategy);
                    }
                }
            }
        }

        public IModel ProcessModelFinalized(IModel model)
        {
            throw new System.NotImplementedException();
        }
    }
}