using System;
using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    /// <summary>
    ///     A convention that ensures that properties aren't configured to have a default value, as computed column
    ///     or using a <see cref="JetValueGenerationStrategy" /> at the same time.
    /// </summary>
    public class JetStoreGenerationConvention : StoreGenerationConvention
    {
        /// <summary>
        ///     Creates a new instance of <see cref="JetStoreGenerationConvention" />.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this convention. </param>
        /// <param name="relationalDependencies">  Parameter object containing relational dependencies for this convention. </param>
        public JetStoreGenerationConvention(
            [NotNull] ProviderConventionSetBuilderDependencies dependencies,
            [NotNull] RelationalConventionSetBuilderDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
        }

        /// <summary>
        ///     Called after an annotation is changed on a property.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property. </param>
        /// <param name="name"> The annotation name. </param>
        /// <param name="annotation"> The new annotation. </param>
        /// <param name="oldAnnotation"> The old annotation.  </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public override void ProcessPropertyAnnotationChanged(
            IConventionPropertyBuilder propertyBuilder,
            string name,
            IConventionAnnotation annotation,
            IConventionAnnotation oldAnnotation,
            IConventionContext<IConventionAnnotation> context)
        {
            if (annotation == null
                || oldAnnotation?.Value != null)
            {
                return;
            }

            var configurationSource = annotation.GetConfigurationSource();
            var fromDataAnnotation = configurationSource != ConfigurationSource.Convention;
            switch (name)
            {
                case RelationalAnnotationNames.DefaultValue:
                    if (propertyBuilder.HasValueGenerationStrategy(null, fromDataAnnotation) == null
                        && propertyBuilder.HasDefaultValue(null, fromDataAnnotation) != null)
                    {
                        context.StopProcessing();
                        return;
                    }

                    break;
                case RelationalAnnotationNames.DefaultValueSql:
                    if (propertyBuilder.HasValueGenerationStrategy(null, fromDataAnnotation) == null
                        && propertyBuilder.HasDefaultValueSql(null, fromDataAnnotation) != null)
                    {
                        context.StopProcessing();
                        return;
                    }

                    break;
                case RelationalAnnotationNames.ComputedColumnSql:
                    if (propertyBuilder.HasValueGenerationStrategy(null, fromDataAnnotation) == null
                        && propertyBuilder.HasComputedColumnSql(null, fromDataAnnotation) != null)
                    {
                        context.StopProcessing();
                        return;
                    }

                    break;
                case JetAnnotationNames.ValueGenerationStrategy:
                    if ((propertyBuilder.HasDefaultValue(null, fromDataAnnotation) == null
                            | propertyBuilder.HasDefaultValueSql(null, fromDataAnnotation) == null
                            | propertyBuilder.HasComputedColumnSql(null, fromDataAnnotation) == null)
                        && propertyBuilder.HasValueGenerationStrategy(null, fromDataAnnotation) != null)
                    {
                        context.StopProcessing();
                        return;
                    }

                    break;
            }

            base.ProcessPropertyAnnotationChanged(propertyBuilder, name, annotation, oldAnnotation, context);
        }
    }
}